namespace Dekorras.Application.Common.Interfaces;

public sealed record PaymentResult(bool Success, string? ProviderTransactionReference, string? FailureReason);

/// <summary>Ödeme sağlayıcısı sözleşmesi: iyzico, PayTR, Param, PayPal, Stripe vb. bunu implemente eder.
/// Checkout'ta hangi sağlayıcının kullanılacağına, aktif olanlar arasından, MÜŞTERİ karar verir.</summary>
public interface IPaymentGateway : IIntegrationConnector
{
    Task<PaymentResult> AuthorizeAsync(IReadOnlyDictionary<string, string> config, decimal amountTry, string orderNumber, CancellationToken cancellationToken);
    Task<PaymentResult> CaptureAsync(IReadOnlyDictionary<string, string> config, string providerTransactionReference, decimal amountTry, CancellationToken cancellationToken);
    Task<PaymentResult> RefundAsync(IReadOnlyDictionary<string, string> config, string providerTransactionReference, decimal amountTry, CancellationToken cancellationToken);
}
