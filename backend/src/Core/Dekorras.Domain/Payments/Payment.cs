using Dekorras.Domain.Common;

namespace Dekorras.Domain.Payments;

public enum PaymentStatus { Pending, Authorized, Captured, Failed, Refunded }
public enum TransactionType { Authorization, Capture, Refund, Void }

public class Payment : AuditableEntity
{
    public Guid OrderId { get; private set; }
    public Guid IntegrationProviderId { get; private set; } // ödeme sağlayıcısı - Integrations.IntegrationProvider
    public decimal AmountTry { get; private set; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;

    private readonly List<Transaction> _transactions = [];
    public IReadOnlyCollection<Transaction> Transactions => _transactions.AsReadOnly();

    private Payment() { }

    public Payment(Guid orderId, Guid integrationProviderId, decimal amountTry)
    {
        OrderId = orderId;
        IntegrationProviderId = integrationProviderId;
        AmountTry = amountTry;
    }

    public void RecordTransaction(TransactionType type, decimal amountTry, string? providerReference, bool isSuccess)
    {
        _transactions.Add(new Transaction(Id, type, amountTry, providerReference, isSuccess));
        Status = (type, isSuccess) switch
        {
            (TransactionType.Authorization, true) => PaymentStatus.Authorized,
            (TransactionType.Capture, true) => PaymentStatus.Captured,
            (TransactionType.Refund, true) => PaymentStatus.Refunded,
            (_, false) => PaymentStatus.Failed,
            _ => Status
        };
    }
}

public class Transaction : BaseEntity
{
    public Guid PaymentId { get; private set; }
    public TransactionType Type { get; private set; }
    public decimal AmountTry { get; private set; }
    public string? ProviderTransactionReference { get; private set; }
    public bool IsSuccess { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    private Transaction() { }

    public Transaction(Guid paymentId, TransactionType type, decimal amountTry, string? providerTransactionReference, bool isSuccess)
    {
        PaymentId = paymentId;
        Type = type;
        AmountTry = amountTry;
        ProviderTransactionReference = providerTransactionReference;
        IsSuccess = isSuccess;
    }
}
