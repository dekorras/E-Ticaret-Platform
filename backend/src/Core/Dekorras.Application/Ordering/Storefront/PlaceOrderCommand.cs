using Dekorras.Application.Catalog;
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

public sealed class PlaceOrderCommandHandler(IUnitOfWork unitOfWork, IProviderRegistry providerRegistry, ISecretProtector secretProtector, IEmailSender emailSender)
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
        foreach (var item in cart.Items)
        {
            var product = await productRepository.GetByIdAsync(item.ProductId, cancellationToken);
            if (product is null) continue;

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

            order.AddItem(product.Id, productName, unitPriceTry, item.Quantity, product.TaxRatePercentage, item.VariantId);
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
            if (appliedCoupon is not null && appliedCoupon.IsValidNow(DateTime.UtcNow))
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

        return new PlaceOrderResult(order.Id, order.OrderNumber, rateQuote.PriceTry, authResult.Success, authResult.FailureReason);
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
            bodyHtml = ApplyPlaceholders(bodyHtml, order, customerName);

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
