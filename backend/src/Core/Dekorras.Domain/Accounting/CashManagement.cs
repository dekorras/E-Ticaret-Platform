using Dekorras.Domain.Common;

namespace Dekorras.Domain.Accounting;

public class CashRegister : AuditableEntity
{
    public string Name { get; private set; } = default!; // Kasa
    public decimal BalanceTry { get; private set; }

    private CashRegister() { }

    public CashRegister(string name) => Name = name;

    public void Deposit(decimal amountTry) => BalanceTry += amountTry;
    public void Withdraw(decimal amountTry)
    {
        if (amountTry > BalanceTry) throw new DomainException("Kasa bakiyesi yetersiz.");
        BalanceTry -= amountTry;
    }
}

public class BankAccount : AuditableEntity
{
    public string BankName { get; private set; } = default!;
    public string Iban { get; private set; } = default!;
    public decimal BalanceTry { get; private set; }

    private BankAccount() { }

    public BankAccount(string bankName, string iban) { BankName = bankName; Iban = iban; }

    public void Deposit(decimal amountTry) => BalanceTry += amountTry;
    public void Withdraw(decimal amountTry)
    {
        if (amountTry > BalanceTry) throw new DomainException("Banka hesabı bakiyesi yetersiz.");
        BalanceTry -= amountTry;
    }
}

public enum PaperInstrumentStatus { Pending, Collected, Bounced } // Beklemede / Tahsil Edildi / Karşılıksız

public class Check : AuditableEntity // Çek
{
    public Guid LedgerAccountId { get; private set; }
    public string CheckNumber { get; private set; } = default!;
    public decimal AmountTry { get; private set; }
    public DateTime DueDateUtc { get; private set; }
    public PaperInstrumentStatus Status { get; private set; } = PaperInstrumentStatus.Pending;

    private Check() { }

    public Check(Guid ledgerAccountId, string checkNumber, decimal amountTry, DateTime dueDateUtc)
    {
        LedgerAccountId = ledgerAccountId;
        CheckNumber = checkNumber;
        AmountTry = amountTry;
        DueDateUtc = dueDateUtc;
    }

    public void MarkCollected() => Status = PaperInstrumentStatus.Collected;
    public void MarkBounced() => Status = PaperInstrumentStatus.Bounced;
}

public class PromissoryNote : AuditableEntity // Senet
{
    public Guid LedgerAccountId { get; private set; }
    public string NoteNumber { get; private set; } = default!;
    public decimal AmountTry { get; private set; }
    public DateTime DueDateUtc { get; private set; }
    public PaperInstrumentStatus Status { get; private set; } = PaperInstrumentStatus.Pending;

    private PromissoryNote() { }

    public PromissoryNote(Guid ledgerAccountId, string noteNumber, decimal amountTry, DateTime dueDateUtc)
    {
        LedgerAccountId = ledgerAccountId;
        NoteNumber = noteNumber;
        AmountTry = amountTry;
        DueDateUtc = dueDateUtc;
    }

    public void MarkCollected() => Status = PaperInstrumentStatus.Collected;
    public void MarkBounced() => Status = PaperInstrumentStatus.Bounced;
}

public class Expense : AuditableEntity
{
    public string CategoryName { get; private set; } = default!;
    public decimal AmountTry { get; private set; }
    public DateTime ExpenseDateUtc { get; private set; }
    public string? Description { get; private set; }

    private Expense() { }

    public Expense(string categoryName, decimal amountTry, DateTime expenseDateUtc, string? description)
    {
        CategoryName = categoryName;
        AmountTry = amountTry;
        ExpenseDateUtc = expenseDateUtc;
        Description = description;
    }
}
