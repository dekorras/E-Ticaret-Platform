using Dekorras.Application.Common.Interfaces;
using Dekorras.Infrastructure.Common;

namespace Dekorras.Infrastructure.PaymentProviders;

public sealed class IyzicoPaymentGateway() : ConnectorBase("iyzico", "iyzico", [
    new ConfigFieldDefinition("ApiKey", "API Anahtarı", ConfigFieldType.Password, true),
    new ConfigFieldDefinition("SecretKey", "Gizli Anahtar", ConfigFieldType.Password, true),
    new ConfigFieldDefinition("BaseUrl", "API Adresi", ConfigFieldType.Text, true)]), IPaymentGateway
{
    public Task<PaymentResult> AuthorizeAsync(IReadOnlyDictionary<string, string> config, decimal amountTry, string orderNumber, CancellationToken ct) =>
        Task.FromResult(new PaymentResult(true, $"IYZ-{orderNumber}", null));
    public Task<PaymentResult> CaptureAsync(IReadOnlyDictionary<string, string> config, string providerTransactionReference, decimal amountTry, CancellationToken ct) =>
        Task.FromResult(new PaymentResult(true, providerTransactionReference, null));
    public Task<PaymentResult> RefundAsync(IReadOnlyDictionary<string, string> config, string providerTransactionReference, decimal amountTry, CancellationToken ct) =>
        Task.FromResult(new PaymentResult(true, providerTransactionReference, null));
}

public sealed class PayTrPaymentGateway() : ConnectorBase("paytr", "PayTR", [
    new ConfigFieldDefinition("MerchantId", "Mağaza No", ConfigFieldType.Text, true),
    new ConfigFieldDefinition("MerchantKey", "Mağaza Anahtarı", ConfigFieldType.Password, true),
    new ConfigFieldDefinition("MerchantSalt", "Mağaza Salt", ConfigFieldType.Password, true)]), IPaymentGateway
{
    public Task<PaymentResult> AuthorizeAsync(IReadOnlyDictionary<string, string> config, decimal amountTry, string orderNumber, CancellationToken ct) =>
        Task.FromResult(new PaymentResult(true, $"PTR-{orderNumber}", null));
    public Task<PaymentResult> CaptureAsync(IReadOnlyDictionary<string, string> config, string providerTransactionReference, decimal amountTry, CancellationToken ct) =>
        Task.FromResult(new PaymentResult(true, providerTransactionReference, null));
    public Task<PaymentResult> RefundAsync(IReadOnlyDictionary<string, string> config, string providerTransactionReference, decimal amountTry, CancellationToken ct) =>
        Task.FromResult(new PaymentResult(true, providerTransactionReference, null));
}

public sealed class ParamPaymentGateway() : ConnectorBase("param", "Param POS", [
    new ConfigFieldDefinition("ClientCode", "Client Code", ConfigFieldType.Text, true),
    new ConfigFieldDefinition("ClientUsername", "Client Username", ConfigFieldType.Text, true),
    new ConfigFieldDefinition("ClientPassword", "Client Password", ConfigFieldType.Password, true)]), IPaymentGateway
{
    public Task<PaymentResult> AuthorizeAsync(IReadOnlyDictionary<string, string> config, decimal amountTry, string orderNumber, CancellationToken ct) =>
        Task.FromResult(new PaymentResult(true, $"PRM-{orderNumber}", null));
    public Task<PaymentResult> CaptureAsync(IReadOnlyDictionary<string, string> config, string providerTransactionReference, decimal amountTry, CancellationToken ct) =>
        Task.FromResult(new PaymentResult(true, providerTransactionReference, null));
    public Task<PaymentResult> RefundAsync(IReadOnlyDictionary<string, string> config, string providerTransactionReference, decimal amountTry, CancellationToken ct) =>
        Task.FromResult(new PaymentResult(true, providerTransactionReference, null));
}

public sealed class PayPalPaymentGateway() : ConnectorBase("paypal", "PayPal", [
    new ConfigFieldDefinition("ClientId", "Client Id", ConfigFieldType.Text, true),
    new ConfigFieldDefinition("ClientSecret", "Client Secret", ConfigFieldType.Password, true)]), IPaymentGateway
{
    public Task<PaymentResult> AuthorizeAsync(IReadOnlyDictionary<string, string> config, decimal amountTry, string orderNumber, CancellationToken ct) =>
        Task.FromResult(new PaymentResult(true, $"PP-{orderNumber}", null));
    public Task<PaymentResult> CaptureAsync(IReadOnlyDictionary<string, string> config, string providerTransactionReference, decimal amountTry, CancellationToken ct) =>
        Task.FromResult(new PaymentResult(true, providerTransactionReference, null));
    public Task<PaymentResult> RefundAsync(IReadOnlyDictionary<string, string> config, string providerTransactionReference, decimal amountTry, CancellationToken ct) =>
        Task.FromResult(new PaymentResult(true, providerTransactionReference, null));
}

public sealed class StripePaymentGateway() : ConnectorBase("stripe", "Stripe", [
    new ConfigFieldDefinition("PublishableKey", "Publishable Key", ConfigFieldType.Text, true),
    new ConfigFieldDefinition("SecretKey", "Secret Key", ConfigFieldType.Password, true)]), IPaymentGateway
{
    public Task<PaymentResult> AuthorizeAsync(IReadOnlyDictionary<string, string> config, decimal amountTry, string orderNumber, CancellationToken ct) =>
        Task.FromResult(new PaymentResult(true, $"STR-{orderNumber}", null));
    public Task<PaymentResult> CaptureAsync(IReadOnlyDictionary<string, string> config, string providerTransactionReference, decimal amountTry, CancellationToken ct) =>
        Task.FromResult(new PaymentResult(true, providerTransactionReference, null));
    public Task<PaymentResult> RefundAsync(IReadOnlyDictionary<string, string> config, string providerTransactionReference, decimal amountTry, CancellationToken ct) =>
        Task.FromResult(new PaymentResult(true, providerTransactionReference, null));
}

/// <summary>Firmanın kendi banka hesabına doğrudan havale/EFT - eski sistemde de "Banka Havale/EFT" olarak aktifti.</summary>
public sealed class BankTransferPaymentGateway() : ConnectorBase("bank-transfer", "Banka Havale/EFT", [
    new ConfigFieldDefinition("BankName", "Banka Adı", ConfigFieldType.Text, true),
    new ConfigFieldDefinition("Iban", "IBAN", ConfigFieldType.Text, true),
    new ConfigFieldDefinition("AccountHolder", "Hesap Sahibi", ConfigFieldType.Text, true)]), IPaymentGateway
{
    public Task<PaymentResult> AuthorizeAsync(IReadOnlyDictionary<string, string> config, decimal amountTry, string orderNumber, CancellationToken ct) =>
        Task.FromResult(new PaymentResult(true, $"EFT-{orderNumber}", null)); // manuel tahsilat - admin onayı bekler
    public Task<PaymentResult> CaptureAsync(IReadOnlyDictionary<string, string> config, string providerTransactionReference, decimal amountTry, CancellationToken ct) =>
        Task.FromResult(new PaymentResult(true, providerTransactionReference, null));
    public Task<PaymentResult> RefundAsync(IReadOnlyDictionary<string, string> config, string providerTransactionReference, decimal amountTry, CancellationToken ct) =>
        Task.FromResult(new PaymentResult(true, providerTransactionReference, null));
}
