using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using Dekorras.Domain.Customers;
using Dekorras.Domain.Integrations;
using Dekorras.Domain.Notifications;
using Dekorras.Domain.Ordering;
using Dekorras.Domain.Payments;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Accounting.Commands;

/// <summary>
/// `Quote.ConvertToOrder(Guid)` Faz 0/1'den beri vardı ama hiçbir yerden çağrılamıyordu - bir
/// teklifi kabul eden bir B2B müşteri için gerçek bir sipariş/ödeme akışı hiç yoktu. Kullanıcı
/// tercihi: `PlaceOrderCommand`'daki AYNI akış (gerçek bir ödeme/kargo sağlayıcısı seçilip Provider
/// Registry üzerinden işlenir) - checkout'un TIPATIP aynısı, yalnızca sepet yerine teklif
/// kalemlerinden beslenir.
///
/// <para><b>Quote/Order arasındaki yapısal fark:</b> bir `QuoteLine` yalnızca serbest metin
/// (`Description`) taşır, gerçek bir `Product`a BAĞLI DEĞİLDİR (B2B teklifler genelde katalogda
/// olmayan özel kalemler içerir) - bu yüzden dönüştürülen `OrderItem`lar `Guid.Empty`'yi
/// ProductId olarak kullanır. Bu GÜVENLİDİR: `GetOrderByIdQuery` (hem Admin hem Storefront sipariş
/// detayı) `OrderItem.ProductName`'i DOĞRUDAN okur, hiçbir yerde `Product` tablosuna JOIN/lookup
/// yapmaz - kod tabanında ProductId'yi gerçekten kullanan (stok düşümü, ürün sayfası linki vb.)
/// TEK yer checkout anıdır, bu akışta hiç tetiklenmez.</para>
///
/// <para><b>Cari Hesap → Müşteri köprüsü:</b> bir `LedgerAccount` bir e-ticaret siparişinden
/// otomatik oluşmuşsa zaten `LinkedCustomerId`'ye sahiptir (bkz. `OrderCompletedEventHandler`);
/// muhasebeci tarafından ELLE oluşturulmuşsa (hiç sipariş vermemiş saf bir B2B cari) bu alan
/// boştur - bu durumda burada girilen bilgilerle SENTETİK bir Customer/Address oluşturulup
/// `LedgerAccount.LinkCustomer` ile geriye bağlanır (bir SONRAKİ teklif AYNI müşteriyi kullanabilsin
/// diye) - `PlaceOrderCommand`'ın misafir müşteri deseniyle AYNI mantık.</para>
/// </summary>
public sealed record ConvertQuoteToOrderCommand(
    Guid QuoteId,
    string FullName,
    string Email,
    string PhoneNumber,
    string CountryCode,
    string City,
    string AddressLine1,
    string PaymentProviderKey,
    string CargoProviderKey) : IRequest<ConvertQuoteToOrderResult>;

public sealed record ConvertQuoteToOrderResult(Guid OrderId, string OrderNumber, decimal ShippingCostTry, bool PaymentAuthorized, string? PaymentMessage);

public sealed class ConvertQuoteToOrderCommandValidator : AbstractValidator<ConvertQuoteToOrderCommand>
{
    public ConvertQuoteToOrderCommandValidator()
    {
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

public sealed class ConvertQuoteToOrderCommandHandler(IUnitOfWork unitOfWork, IProviderRegistry providerRegistry, ISecretProtector secretProtector, IEmailSender emailSender)
    : IRequestHandler<ConvertQuoteToOrderCommand, ConvertQuoteToOrderResult>
{
    // Katalog dışı serbest metin kalemler için gerçek bir ürün ağırlığı yok - bkz.
    // PlaceOrderCommand.DefaultItemWeightKg'deki AYNI varsayılan/gerekçe.
    private const decimal DefaultItemWeightKg = 1m;

    public async Task<ConvertQuoteToOrderResult> Handle(ConvertQuoteToOrderCommand request, CancellationToken cancellationToken)
    {
        var quoteRepository = unitOfWork.Repository<Quote>();
        var quote = await quoteRepository.GetByIdAsync(request.QuoteId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.QuoteId}' numaralı teklif bulunamadı.");

        if (quote.ConvertedToOrderId is not null)
            throw new InvalidOperationException("Bu teklif zaten bir siparişe dönüştürülmüş.");
        if (quote.ValidUntilUtc < DateTime.UtcNow)
            throw new InvalidOperationException("Bu teklifin süresi dolmuş, siparişe dönüştürülemez.");

        await quoteRepository.LoadCollectionAsync(quote, q => q.Lines, cancellationToken);
        if (quote.Lines.Count == 0)
            throw new InvalidOperationException("Teklifte hiç kalem yok.");

        var providerRepository = unitOfWork.Repository<IntegrationProvider>();

        var paymentProvider = GetActiveProviderOrThrow(providerRepository, request.PaymentProviderKey, ProviderCategory.Payment, "ödeme sağlayıcısı");
        await providerRepository.LoadCollectionAsync(paymentProvider, p => p.ConfigFields, cancellationToken);
        var paymentConfig = DecryptConfig(paymentProvider, secretProtector);

        var cargoProvider = GetActiveProviderOrThrow(providerRepository, request.CargoProviderKey, ProviderCategory.Cargo, "kargo sağlayıcısı");
        await providerRepository.LoadCollectionAsync(cargoProvider, p => p.ConfigFields, cancellationToken);
        var cargoConfig = DecryptConfig(cargoProvider, secretProtector);

        var ledgerAccountRepository = unitOfWork.Repository<LedgerAccount>();
        var ledgerAccount = await ledgerAccountRepository.GetByIdAsync(quote.LedgerAccountId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{quote.LedgerAccountId}' numaralı cari hesap bulunamadı.");

        var customerRepository = unitOfWork.Repository<Customer>();
        var customer = ledgerAccount.LinkedCustomerId is Guid linkedCustomerId
            ? await customerRepository.GetByIdAsync(linkedCustomerId, cancellationToken)
            : null;

        if (customer is null)
        {
            // BİLİNÇLİ OLARAK "Kurumsal" - PlaceOrderCommand'ın misafir varsayılanı "Bireysel"in
            // AKSİNE, bir Cari Hesap'tan (B2B teklif) doğan bir müşteri neredeyse her zaman
            // kurumsaldır.
            var customerGroupId = unitOfWork.Repository<CustomerGroup>().Query()
                .Where(g => g.Name == "Kurumsal")
                .Select(g => (Guid?)g.Id)
                .FirstOrDefault() ?? throw new InvalidOperationException("'Kurumsal' müşteri grubu bulunamadı.");

            var identityUserId = $"ledger-{Guid.NewGuid():N}";
            customer = new Customer(identityUserId, request.FullName, request.Email, customerGroupId);
            customer.UpdateContact(request.FullName, request.PhoneNumber);
            await customerRepository.AddAsync(customer, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            ledgerAccount.LinkCustomer(customer.Id);
        }

        var address = new Address(customer.Id, request.FullName, request.CountryCode, request.City, request.AddressLine1, request.PhoneNumber);
        await unitOfWork.Repository<Address>().AddAsync(address, cancellationToken);

        var orderNumber = $"TEKLIF-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}";
        var order = new Order(orderNumber, customer.Id, OrderSource.AdminQuote, address.Id, address.Id);

        var totalWeightKg = 0m;
        foreach (var line in quote.Lines)
        {
            // KDV oranı BİLİNÇLİ OLARAK %0 - QuoteLine'da hiç tutulmuyor (Quote.TotalAmountTry
            // zaten vergisiz hesaplanıyor, bkz. Quote.AddLine), bu yüzden Order.SubTotalTry
            // teklifteki tutarla TUTARLI kalır.
            order.AddItem(Guid.Empty, line.Description, line.UnitPriceTry, line.Quantity, taxRatePercentage: 0m);
            totalWeightKg += DefaultItemWeightKg * line.Quantity;
        }

        var cargoGateway = providerRegistry.GetCargoProvider(request.CargoProviderKey);
        var rateQuote = await cargoGateway.GetRateAsync(cargoConfig, request.CountryCode, totalWeightKg, cancellationToken);
        order.SetShippingCost(cargoProvider.Id, rateQuote.PriceTry);

        await unitOfWork.Repository<Order>().AddAsync(order, cancellationToken);
        quote.ConvertToOrder(order.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var paymentGateway = providerRegistry.GetPaymentGateway(request.PaymentProviderKey);
        var authResult = await paymentGateway.AuthorizeAsync(paymentConfig, order.GrandTotalTry, order.OrderNumber, cancellationToken);

        var payment = new Payment(order.Id, paymentProvider.Id, order.GrandTotalTry);
        payment.RecordTransaction(TransactionType.Authorization, order.GrandTotalTry, authResult.ProviderTransactionReference, authResult.Success);
        await unitOfWork.Repository<Payment>().AddAsync(payment, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await SendOrderConfirmationEmailAsync(order, request.Email, request.FullName, cancellationToken);

        return new ConvertQuoteToOrderResult(order.Id, order.OrderNumber, rateQuote.PriceTry, authResult.Success, authResult.FailureReason);
    }

    /// <summary>Bkz. PlaceOrderCommand.SendOrderConfirmationEmailAsync'teki AYNI desen - e-posta
    /// gönderimindeki bir hata sipariş oluşturmayı BOZMAMALI, izole edilmiştir.</summary>
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
            success = false;
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
