using Dekorras.Domain.Common;

namespace Dekorras.Domain.Accounting;

public enum LedgerAccountType { Customer, Supplier, Both }

/// <summary>Cari hesap — müşteri ve tedarikçi ortak yapıdadır (BizimHesap'taki gibi).</summary>
public class LedgerAccount : AuditableEntity
{
    public string Name { get; private set; } = default!;
    public LedgerAccountType Type { get; private set; }
    public string? TaxOffice { get; private set; }
    public string? TaxNumber { get; private set; }
    public string? Phone { get; private set; }
    public Guid? LinkedCustomerId { get; private set; } // e-ticaret Customer ile eşleşiyorsa

    public decimal OpenBalanceTry { get; private set; } // Açık Bakiye
    public decimal CheckNoteBalanceTry { get; private set; } // Çek/Senet Bakiyesi
    public bool IsActive { get; private set; } = true;

    private readonly List<LedgerTransaction> _transactions = [];
    public IReadOnlyCollection<LedgerTransaction> Transactions => _transactions.AsReadOnly();

    private LedgerAccount() { }

    public LedgerAccount(string name, LedgerAccountType type, Guid? linkedCustomerId = null)
    {
        Name = name;
        Type = type;
        LinkedCustomerId = linkedCustomerId;
    }

    public void RecordTransaction(LedgerTransactionDirection direction, decimal amountTry, string description, Guid? relatedOrderId = null)
    {
        _transactions.Add(new LedgerTransaction(Id, direction, amountTry, description, relatedOrderId));
        OpenBalanceTry += direction == LedgerTransactionDirection.Debit ? amountTry : -amountTry;
    }

    public void SetTaxInfo(string? taxOffice, string? taxNumber)
    {
        TaxOffice = taxOffice;
        TaxNumber = taxNumber;
    }

    public void SetPhone(string? phone) => Phone = phone;

    /// <summary>Bekleyen çek/senet toplamını günceller - bir çek/senet eklendiğinde pozitif,
    /// tahsil/karşılıksız olarak işaretlendiğinde (artık "bekleyen" olmadığı için) negatif
    /// bir delta ile çağrılır (bkz. Commands/CheckCommands.cs, PromissoryNoteCommands.cs).</summary>
    public void AdjustCheckNoteBalance(decimal deltaTry) => CheckNoteBalanceTry += deltaTry;

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    /// <summary>Bir cari hesap elle (bir e-ticaret siparişinden OTOMATİK değil, muhasebeci
    /// tarafından) oluşturulmuşsa `LinkedCustomerId` başlangıçta boş kalır - bkz.
    /// `ConvertQuoteToOrderCommand`, bir teklif siparişe dönüştürülürken bu cari hesap için
    /// sentetik bir Customer oluşturulup buradan geriye bağlanır (bir SONRAKİ teklif AYNI
    /// müşteriyi yeniden kullanabilsin diye).</summary>
    public void LinkCustomer(Guid customerId) => LinkedCustomerId = customerId;
}

public enum LedgerTransactionDirection { Debit, Credit } // Borç / Alacak

public class LedgerTransaction : BaseEntity
{
    public Guid LedgerAccountId { get; private set; }
    public LedgerTransactionDirection Direction { get; private set; }
    public decimal AmountTry { get; private set; }
    public string Description { get; private set; } = default!;
    public Guid? RelatedOrderId { get; private set; }
    public DateTime TransactionDateUtc { get; private set; } = DateTime.UtcNow;

    private LedgerTransaction() { }

    public LedgerTransaction(Guid ledgerAccountId, LedgerTransactionDirection direction, decimal amountTry, string description, Guid? relatedOrderId)
    {
        LedgerAccountId = ledgerAccountId;
        Direction = direction;
        AmountTry = amountTry;
        Description = description;
        RelatedOrderId = relatedOrderId;
    }
}
