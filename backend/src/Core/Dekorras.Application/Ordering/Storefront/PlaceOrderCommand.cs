using System.Text.Json;
using Dekorras.Application.Catalog;
using Dekorras.Application.WallCovering;
using Dekorras.Domain.WallCovering;
using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using Dekorras.Domain.Integrations;
using Dekorras.Domain.Marketing;
using Dekorras.Domain.Notifications;
using Dekorras.Domain.Ordering;
using Dekorras.Domain.Payments;
using Dekorras.Domain.Shipping;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Ordering.Storefront;

/// <summary>
/// Hem misafir alışverişini (bkz. plan §2.2 "misafir alışverişi desteği aktif") hem de kayıtlı
/// müşteri siparişini destekler. <paramref name="IdentityUserId"/> null ise (misafir), checkout
/// sırasında girilen bilgilerle hafif bir Customer/Address kaydı oluşturulur. Dolu ise, ilgili
/// Identity kullanıcısına bağlı MEVCUT Customer kaydı kullanılır (her siparişte yeni bir müşteri
/// yaratılmaz) ve girilen adres o müşteriye yeni bir Address olarak eklenir.
/// </summary>
public sealed record PlaceOrderCommand(
    string SessionKey,
    string? IdentityUserId,
    string FullName,
    string Email,
    string PhoneNumber,
    string CountryCode,
    string City,
    string AddressLine1,
    string PaymentProviderKey,
    string CargoProviderKey) : IRequest<PlaceOrderResult>;

public sealed record PlaceOrderResult(Guid OrderId, string OrderNumber, decimal ShippingCostTry, bool PaymentAuthorized, string? PaymentMessage);

public sealed class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        RuleFor(x => x.SessionKey).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.PhoneNumber).NotEmpty();
        RuleFor(x => x.CountryCode).NotEmpty().Length(2);
        RuleFor(x => x.City).NotEmpty();
        RuleFor(x => x.AddressLine1).NotEmpty();
        RuleFor(x => x.PaymentProviderKey).NotEmpty();
        RuleFor(x => x.CargoProviderKey).NotEmpty();
    }
}

public sealed class PlaceOrderCommandHandler(IUnitOfWork unitOfWork, IProviderRegistry providerRegistry, ISecretProtector secretProtector, IEmailSender emailSender, IPricingService pricingService)
    : IRequestHandler<PlaceOrderCommand, PlaceOrderResult>
{
    // Admin'de "Ağırlık (kg)" alanı boş bırakılan (Product.Weight == null) ürünler için kargo
    // teklifi yine de hesaplanabilsin diye makul bir varsayılan kullanılır.
    private const decimal DefaultItemWeightKg = 1m;

    public async Task<PlaceOrderResult> Handle(PlaceOrderCommand request, CancellationToken cancellationToken)
    {
        var cartRepository = unitOfWork.Repository<Cart>();
        var cart = cartRepository.Query().FirstOrDefault(c => c.SessionKey == request.SessionKey)
            ?? throw new InvalidOperationException("Sepet boş.");

        await cartRepository.LoadCollectionAsync(cart, c => c.Items, cancellationToken);
        if (cart.Items.Count == 0)
            throw new InvalidOperationException("Sepet boş.");

        // Konfigüratörden önce ÖLÇÜSÜZ (eski sabit fiyatla) eklenmiş duvar kağıdı/poster satırı siparişe dönüştürülemez:
        // ürün artık m² fiyatıyla, ölçüye özel üretiliyor. Müşteri ölçü seçmeli ya da satırı kaldırmalı.
        var legacyWall = GetCartQueryHandler.LegacyWallProductIds(unitOfWork, cart.Items.Where(i => !i.IsConfigured).Select(i => i.ProductId));
        if (legacyWall.Count > 0)
        {
            var names = unitOfWork.Repository<Product>().Query().Where(p => legacyWall.Contains(p.Id))
                .Select(p => p.Translations.Where(t => t.LanguageCode == "tr").Select(t => t.Name).FirstOrDefault() ?? p.ProductCode).ToList();
            throw new InvalidOperationException($"Sepetinizde ölçüsü seçilmemiş ölçüye özel ürün var: {string.Join(", ", names)}. Lütfen sepetten ölçüsünü seçin veya ürünü kaldırın.");
        }

        var providerRepository = unitOfWork.Repository<IntegrationProvider>();

        var paymentProvider = GetActiveProviderOrThrow(providerRepository, request.PaymentProviderKey, ProviderCategory.Payment, "ödeme sağlayıcısı");
        await providerRepository.LoadCollectionAsync(paymentProvider, p => p.ConfigFields, cancellationToken);
        var paymentConfig = DecryptConfig(paymentProvider, secretProtector);

        var cargoProvider = GetActiveProviderOrThrow(providerRepository, request.CargoProviderKey, ProviderCategory.Cargo, "kargo sağlayıcısı");
        await providerRepository.LoadCollectionAsync(cargoProvider, p => p.ConfigFields, cancellationToken);
        var cargoConfig = DecryptConfig(cargoProvider, secretProtector);

        var customerRepository = unitOfWork.Repository<Customer>();
        var customer = string.IsNullOrEmpty(request.IdentityUserId)
            ? null
            : customerRepository.Query().FirstOrDefault(c => c.IdentityUserId == request.IdentityUserId);

        if (customer is null)
        {
            // Customers.Email benzersiz indekslidir: aynı e-postayla ikinci misafir siparişi daha önce
            // DbUpdateException ile çöküyordu. Önceki misafir kaydı yeniden kullanılır; e-posta kayıtlı
            // (üye) bir hesaba aitse sipariş o hesaba iliştirilMEZ - başkasının e-postasını yazan biri
            // onun "Siparişlerim" sayfasına sipariş ekleyememeli.
            var normalizedEmail = request.Email.Trim().ToLower();
            var existingByEmail = customerRepository.Query().FirstOrDefault(c => c.Email.ToLower() == normalizedEmail);
            if (existingByEmail is not null)
            {
                if (!existingByEmail.IdentityUserId.StartsWith("guest-"))
                    throw new InvalidOperationException("Bu e-posta adresiyle kayıtlı bir hesap var. Lütfen giriş yaparak devam edin.");
                customer = existingByEmail;
                customer.UpdateContact(request.FullName, request.PhoneNumber);
            }
        }

        if (customer is null)
        {
            var customerGroupId = unitOfWork.Repository<CustomerGroup>().Query()
                .Where(g => g.Name == "Bireysel")
                .Select(g => (Guid?)g.Id)
                .FirstOrDefault() ?? throw new InvalidOperationException("'Bireysel' müşteri grubu bulunamadı.");

            // Misafir siparişi: kayıtlı bir Identity hesabı yoksa, IdentityUserId alanına yalnızca
            // bu siparişi ileride bir hesaba bağlayabilmek için sentetik bir tanımlayıcı yazılır.
            var identityUserId = request.IdentityUserId ?? $"guest-{Guid.NewGuid():N}";
            customer = new Customer(identityUserId, request.FullName, request.Email, customerGroupId);
            customer.UpdateContact(request.FullName, request.PhoneNumber);
            await customerRepository.AddAsync(customer, cancellationToken);
        }

        var address = new Address(customer.Id, request.FullName, request.CountryCode, request.City, request.AddressLine1, request.PhoneNumber);
        await unitOfWork.Repository<Address>().AddAsync(address, cancellationToken);

        var orderNumber = $"WEB-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}";
        var order = new Order(orderNumber, customer.Id, OrderSource.Web, address.Id, address.Id);

        var productRepository = unitOfWork.Repository<Product>();
        var totalWeightKg = 0m;
        var hsCodes = new List<string>();
        var itemNames = new List<string>();

        // Ölçüye özel duvar kağıdı kuralları (bkz. Application.WallCovering.CartTotalsCalculator):
        // tutkal satırları EN SONA alınır ki ücretsiz tutkal eşiği, diğer tüm kalemlerin checkout
        // anında yeniden hesaplanmış ara toplamına (order.SubTotalTry) göre değerlendirilebilsin.
        var wallSettings = WallCoveringSettings.Load(unitOfWork);
        var glueProductIds = CartTotalsCalculator.GlueProductIds(unitOfWork);
        var requiresGlue = CartTotalsCalculator.RequiresGlueResolver(unitOfWork);
        bool IsGlueLine(CartItem i) => !i.IsConfigured && glueProductIds.Contains(i.ProductId);
        var glueDemand = cart.Items.Where(i => i.IsConfigured && requiresGlue(i.ConfigurationJson)).Sum(i => i.Quantity);
        var glueQuantityInCart = cart.Items.Where(IsGlueLine).Sum(i => i.Quantity);
        int? freeGlueUnitsRemaining = null;

        foreach (var item in cart.Items.OrderBy(i => IsGlueLine(i) ? 1 : 0))
        {
            var product = await productRepository.GetByIdAsync(item.ProductId, cancellationToken);
            if (product is null) continue;

            if (item.IsConfigured)
            {
                // Fiyat checkout ANINDA güncel malzeme fiyatıyla yeniden hesaplanır; istemciden veya
                // sepetten gelen tutar kullanılmaz. Konfigürasyon ve kırılım siparişe DONDURULUR.
                var configuration = WallConfiguration.FromJson(item.ConfigurationJson)
                    ?? throw new InvalidOperationException("Sepetteki ölçüye özel ürünün konfigürasyonu okunamadı.");
                var quote = pricingService.QuoteLine(product.Id, configuration, 1);
                if (!quote.IsValid)
                    throw new InvalidOperationException($"'{quote.ProductName}': {string.Join(" ", quote.Errors.Values)}");

                var line = quote.Line! with { Quantity = item.Quantity, LineTotal = quote.Line!.UnitPrice * item.Quantity };
                var configuredName = $"{quote.ProductName} ({line.WidthCm:0.#}×{line.HeightCm:0.#} cm, {line.MaterialName})";
                order.AddConfiguredItem(product.Id, configuredName, line.UnitPrice, item.Quantity, quote.TaxRatePercentage,
                    configuration.ToJson(), JsonSerializer.Serialize(line));

                // VARSAYIM: ölçüye özel ürünün kargo ağırlığı, faturalanan m² × malzeme gramajıdır (+ %20 ambalaj).
                totalWeightKg += line.BilledAreaM2 * quote.Material!.WeightGsm / 1000m * 1.2m * item.Quantity;
                itemNames.Add(configuredName);
                continue;
            }

            // product.Translations pasif gezinme ile dokunulamaz (bkz. README "Önemli mimari not") -
            // ayrı bir sorgu ile alınıyor. Sipariş kalemine müşterinin gördüğü ürün ADI yazılır,
            // SKU/ürün kodu değil.
            var productName = unitOfWork.Repository<ProductTranslation>().Query()
                .Where(t => t.ProductId == product.Id && t.LanguageCode == "tr")
                .Select(t => t.Name)
                .FirstOrDefault() ?? product.ProductCode;

            // Fiyat, checkout ANINDA yeniden hesaplanır - sepete eklendiğinden beri değişmiş
            // olabilir, tıpkı diğer fiyat bileşenleri gibi. Öncelik: toplu alım kademesi (bkz.
            // Domain.Catalog.QuantityDiscount) > müşteri grubuna özel fiyat (bkz.
            // ProductPricingHelper - plan §2.6 B2B/B2C farklı fiyatlandırma) > taban fiyat.
            var unitPriceTry = ProductPricingHelper.ResolveUnitPriceTry(unitOfWork, product.Id, product.BasePriceTry, item.Quantity, customer.CustomerGroupId);

            if (item.VariantId is Guid variantId)
            {
                // Fiyat/etiket sepetten DEĞİL, checkout ANINDA fresh okunur - tıpkı taban ürün
                // fiyatı gibi (sepete eklendiğinden beri değişmiş olabilir).
                var variant = unitOfWork.Repository<ProductVariant>().Query().FirstOrDefault(v => v.Id == variantId);
                if (variant is not null)
                {
                    unitPriceTry += variant.PriceAdjustmentTry ?? 0m;
                    productName = $"{productName} ({variant.OptionName})";

                    // Varyantlar taban ürünün TrackStock bayrağından bağımsız olarak KENDİ
                    // stoklarını taşır (bkz. Domain.Catalog.ProductVariant) - satış anında düşülür.
                    if (variant.StockQuantity < item.Quantity)
                        throw new InvalidOperationException($"'{productName}' için yeterli stok yok. Mevcut: {variant.StockQuantity}, istenen: {item.Quantity}.");

                    variant.UpdateStock(variant.StockQuantity - item.Quantity);
                }
            }
            else if (product.TrackStock)
            {
                // Varyantsız ürünlerde stok taban üründen düşülür - varyantlı bir üründe taban
                // stok alanı kullanılmaz (her varyant kendi stokunu ayrı taşır).
                if (product.StockQuantity < item.Quantity)
                    throw new InvalidOperationException($"'{productName}' için yeterli stok yok. Mevcut: {product.StockQuantity}, istenen: {item.Quantity}.");

                product.UpdateStock(product.StockQuantity - item.Quantity);
            }

            if (IsGlueLine(item))
            {
                // Tutkal satırları döngünün sonunda: order.SubTotalTry artık tutkal HARİÇ ara toplamdır.
                freeGlueUnitsRemaining ??= CartTotalsCalculator.GlueFreeUnits(order.SubTotalTry, glueDemand, glueQuantityInCart, wallSettings.GlueFreeThresholdTry);
                var freeUnits = Math.Min(freeGlueUnitsRemaining.Value, item.Quantity);
                if (freeUnits > 0)
                {
                    order.AddItem(product.Id, $"{productName} (ücretsiz)", 0m, freeUnits, product.TaxRatePercentage, item.VariantId);
                    freeGlueUnitsRemaining -= freeUnits;
                }
                if (item.Quantity - freeUnits > 0)
                    order.AddItem(product.Id, productName, unitPriceTry, item.Quantity - freeUnits, product.TaxRatePercentage, item.VariantId);
            }
            else
            {
                order.AddItem(product.Id, productName, unitPriceTry, item.Quantity, product.TaxRatePercentage, item.VariantId);
            }
            totalWeightKg += (product.Weight ?? DefaultItemWeightKg) * item.Quantity;
            if (!string.IsNullOrWhiteSpace(product.HsCode)) hsCodes.Add(product.HsCode);
            itemNames.Add(productName);
        }

        // Türkiye dışına gönderilen bir sipariş için gümrük beyanı taslağı otomatik oluşturulur
        // (bkz. plan §7 - "Ürünlerde HS/GTİP kodu alanı"). Yalnızca bir TASLAK - proforma fatura
        // üretimi ve %0 KDV istisnası hesaplama mantığı bilinçli olarak kapsam dışı, ayrı bir
        // tasarım kararı gerektiriyor. Admin gerekirse (ör. bir üründe HS kodu hiç girilmemişse)
        // `UpdateCustomsDeclarationCommand` ile düzeltebilir.
        if (!string.Equals(request.CountryCode, "TR", StringComparison.OrdinalIgnoreCase))
        {
            var hsCodeSummary = hsCodes.Count > 0 ? string.Join(", ", hsCodes.Distinct()) : "Belirtilmemiş - Admin tarafından girilmeli";
            var contentDescription = string.Join(", ", itemNames.Distinct());
            var customsDeclaration = new CustomsDeclaration(order.Id, hsCodeSummary, order.SubTotalTry, contentDescription);
            await unitOfWork.Repository<CustomsDeclaration>().AddAsync(customsDeclaration, cancellationToken);
        }

        // Kupon, checkout ANINDA yeniden doğrulanır (sepete eklendiğinden beri süresi dolmuş veya
        // kullanım limitine ulaşılmış olabilir) - GetCartQuery'deki önizleme yalnızca göstergedir.
        Coupon? appliedCoupon = null;
        if (cart.CouponCode is not null)
        {
            appliedCoupon = unitOfWork.Repository<Coupon>().Query().FirstOrDefault(c => c.Code == cart.CouponCode);
            if (appliedCoupon is not null && appliedCoupon.IsValidNow(DateTime.UtcNow) && IsWithinPerUserLimit(appliedCoupon, request.Email))
            {
                var discount = appliedCoupon.CalculateDiscount(order.SubTotalTry);
                order.ApplyCoupon(appliedCoupon.Code, discount);
            }
            else
            {
                appliedCoupon = null; // artık geçersiz - sessizce indirim uygulanmaz
            }
        }

        // Kampanya: yalnızca bir KUPON kodu uygulanMAMIŞSA denenir (ikisinin birlikte nasıl
        // birleşeceği ayrı bir tasarım kararı gerektirir - bkz. Domain.Marketing.Campaign belgesi
        // ve backend/README.md). Koşulları (şu an yalnızca MinCartTotal) sağlayan, en yüksek
        // indirimi veren aktif kampanya otomatik uygulanır - müşteri hiçbir kod girmez.
        Campaign? appliedCampaign = null;
        if (appliedCoupon is null)
        {
            var now = DateTime.UtcNow;
            var candidateCampaigns = unitOfWork.Repository<Campaign>().Query()
                .Where(c => c.IsActive && now >= c.StartsAtUtc && now <= c.EndsAtUtc)
                .ToList()
                .Where(c => (c.UsageLimit is null || c.UsageCount < c.UsageLimit) && c.MeetsRules(order.SubTotalTry))
                .ToList();

            appliedCampaign = candidateCampaigns
                .OrderByDescending(c => c.CalculateDiscount(order.SubTotalTry))
                .FirstOrDefault();

            if (appliedCampaign is not null)
                order.ApplyCampaignDiscount(appliedCampaign.Id, appliedCampaign.CalculateDiscount(order.SubTotalTry));
        }

        var cargoGateway = providerRegistry.GetCargoProvider(request.CargoProviderKey);
        var rateQuote = await cargoGateway.GetRateAsync(cargoConfig, request.CountryCode, totalWeightKg, cancellationToken);
        order.SetShippingCost(cargoProvider.Id, rateQuote.PriceTry);

        // Ücretsiz kargo eşiği (admin ayarı, varsayılan kapalı) indirim SONRASI ara toplama göre.
        if (CartTotalsCalculator.IsFreeShipping(order.SubTotalTry - order.DiscountTotalTry, wallSettings.FreeShippingThresholdTry))
            order.SetShippingCost(cargoProvider.Id, 0m);

        // Hediye çeki, checkout ANINDA yeniden doğrulanır (bkz. GetCartQuery'deki önizleme - kargo
        // ücreti orada henüz bilinmiyordu). Coupon/Campaign'in AKSİNE bir indirim değil bir ödeme
        // yöntemidir - ikisiyle BİRLİKTE kullanılabilir (bkz. Order.ApplyGiftVoucher belgesi).
        GiftVoucher? appliedGiftVoucher = null;
        decimal giftVoucherAmountApplied = 0m;
        if (cart.GiftVoucherCode is not null)
        {
            appliedGiftVoucher = unitOfWork.Repository<GiftVoucher>().Query().FirstOrDefault(v => v.Code == cart.GiftVoucherCode);
            if (appliedGiftVoucher is not null && appliedGiftVoucher.IsUsable())
            {
                giftVoucherAmountApplied = Math.Min(appliedGiftVoucher.RemainingBalanceTry, order.GrandTotalTry);
                order.ApplyGiftVoucher(appliedGiftVoucher.Code, giftVoucherAmountApplied);
            }
            else
            {
                appliedGiftVoucher = null; // artık kullanılamaz - sessizce uygulanmaz
            }
        }

        await unitOfWork.Repository<Order>().AddAsync(order, cancellationToken);
        // Ölçüye özel kalemler için onay önizlemesi + üretim dosyası kayıtları siparişle AYNI işlemde açılır.
        await WallOrderSupport.CreateProductionTasksAsync(unitOfWork, order, cancellationToken);
        appliedCoupon?.RegisterUsage(); // Update() BİLİNÇLİ OLARAK çağrılmaz - yalnızca skaler bir alan değişiyor.
        appliedCampaign?.RegisterUsage();
        if (giftVoucherAmountApplied > 0) appliedGiftVoucher!.Redeem(giftVoucherAmountApplied);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var paymentGateway = providerRegistry.GetPaymentGateway(request.PaymentProviderKey);
        var authResult = await paymentGateway.AuthorizeAsync(paymentConfig, order.GrandTotalTry, order.OrderNumber, cancellationToken);

        var payment = new Payment(order.Id, paymentProvider.Id, order.GrandTotalTry);
        payment.RecordTransaction(TransactionType.Authorization, order.GrandTotalTry, authResult.ProviderTransactionReference, authResult.Success);
        await unitOfWork.Repository<Payment>().AddAsync(payment, cancellationToken);

        // Update(cart) BİLİNÇLİ OLARAK çağrılmaz - bkz. TransitionOrderStatusCommand'daki not.
        cart.Clear();

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await SendOrderConfirmationEmailAsync(order, request.Email, request.FullName, cancellationToken);

        return new PlaceOrderResult(order.Id, order.OrderNumber, order.ShippingTotalTry, authResult.Success, authResult.FailureReason);
    }

    /// <summary>Kuponun kişi başı limiti: aynı e-posta adresine sahip müşterilerin bu kuponla verdiği
    /// önceki siparişler sayılır. Misafir siparişlerinde her seferinde yeni Customer oluştuğu için
    /// müşteri kimliği yerine e-posta kullanılır.</summary>
    private bool IsWithinPerUserLimit(Coupon coupon, string email)
    {
        if (coupon.PerUserLimit is not int limit) return true;

        var normalizedEmail = email.Trim().ToLower();
        var used = unitOfWork.Repository<Order>().Query()
            .Where(o => o.CouponCode == coupon.Code && o.Status != OrderStatus.Cancelled)
            .Join(unitOfWork.Repository<Customer>().Query(), o => o.CustomerId, c => c.Id, (o, c) => c.Email)
            .Count(e => e.ToLower() == normalizedEmail);
        return used < limit;
    }

    /// <summary>`IEmailSender`/`EmailTemplate`/`NotificationLog` Faz 0/1'den beri hazırdı (Infrastructure
    /// katmanında GERÇEK bir SMTP çağrısı YERİNE yalnızca loglayan bir stub ile), ama Application
    /// katmanının hiçbir yerinden ÇAĞRILMIYORDU - müşteri hiçbir zaman bir sipariş onayı almıyordu.
    /// Checkout'un e-posta gönderiminde bir hata yüzünden BAŞARISIZ OLMAMASI için tamamen izole
    /// edilmiştir (ayrı bir SaveChangesAsync, try/catch içinde).</summary>
    private async Task SendOrderConfirmationEmailAsync(Order order, string toEmail, string customerName, CancellationToken cancellationToken)
    {
        const string templateKey = "OrderConfirmation";
        var success = false;

        try
        {
            var template = unitOfWork.Repository<EmailTemplate>().Query()
                .FirstOrDefault(t => t.Key == templateKey && t.LanguageCode == "tr");

            var subject = template?.Subject ?? "Siparişiniz Alındı - {{OrderNumber}}";
            var bodyHtml = template?.BodyHtml
                ?? "<p>Sayın {{CustomerName}},</p><p>{{OrderNumber}} numaralı siparişiniz alınmıştır. Tutar: {{GrandTotal}} ₺.</p>";

            subject = ApplyPlaceholders(subject, order, customerName);
            bodyHtml = ApplyPlaceholders(bodyHtml, order, customerName) + WallPreviewLinksHtml(order);

            await emailSender.SendAsync(toEmail, subject, bodyHtml, cancellationToken);
            success = true;
        }
        catch
        {
            success = false; // e-posta gönderimi BAŞARISIZ olsa bile checkout ZATEN tamamlanmıştır
        }

        await unitOfWork.Repository<NotificationLog>().AddAsync(
            new NotificationLog(NotificationChannel.Email, toEmail, templateKey, success), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Ölçüye özel kalemler için "Siparişini duvarında gör" bağlantıları (spec 1.6.6-A, kaynak: e-posta).</summary>
    private string WallPreviewLinksHtml(Order order)
    {
        var configured = order.Items.Where(i => i.ConfigurationJson is not null).ToList();
        if (configured.Count == 0) return "";

        var ids = configured.Select(i => i.ProductId).ToList();
        var slugs = unitOfWork.Repository<Product>().Query().Where(p => ids.Contains(p.Id)).ToDictionary(p => p.Id, p => p.Slug);
        var baseUrl = WallOrderSupport.BaseUrl(WallCoveringSettings.Load(unitOfWork));
        var links = configured.Where(i => slugs.ContainsKey(i.ProductId)).Select(i =>
            $"<li><a href=\"{System.Net.WebUtility.HtmlEncode(WallPreviewUrl.Build(slugs[i.ProductId], WallConfiguration.FromJson(i.ConfigurationJson), source: "e-posta", baseUrl: baseUrl))}\">" +
            $"{System.Net.WebUtility.HtmlEncode(i.ProductName)}</a></li>");
        return $"<p><strong>Siparişini duvarında gör:</strong></p><ul>{string.Concat(links)}</ul>" +
               "<p>Ölçüye özel ürünleriniz için baskı öncesi onay önizlemesi ayrıca e-postayla gönderilecektir.</p>";
    }

    private static string ApplyPlaceholders(string text, Order order, string customerName) => text
        .Replace("{{OrderNumber}}", order.OrderNumber)
        .Replace("{{CustomerName}}", customerName)
        .Replace("{{GrandTotal}}", order.GrandTotalTry.ToString("N2"));

    private static IntegrationProvider GetActiveProviderOrThrow(IRepository<IntegrationProvider> repository, string providerKey, ProviderCategory category, string label)
    {
        var provider = repository.Query().FirstOrDefault(p => p.ProviderKey == providerKey && p.Category == category)
            ?? throw new InvalidOperationException($"Seçilen {label} bulunamadı.");

        if (provider.Status != ProviderStatus.Active)
            throw new InvalidOperationException($"Seçilen {label} şu anda aktif değil.");

        return provider;
    }

    private static Dictionary<string, string> DecryptConfig(IntegrationProvider provider, ISecretProtector secretProtector) =>
        provider.ConfigFields.ToDictionary(f => f.FieldKey, f => secretProtector.Unprotect(f.EncryptedValue));
}
