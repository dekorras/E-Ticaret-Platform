using Dekorras.Domain.Common;

namespace Dekorras.Domain.Localization;

public class Currency : AuditableEntity
{
    public string Code { get; private set; } = default!; // ISO 4217: TRY, USD, EUR...
    public string Symbol { get; private set; } = default!;
    public bool IsBaseCurrency { get; private set; }
    public bool IsActive { get; private set; }
    public int DecimalDigits { get; private set; }

    private Currency() { }

    public Currency(string code, string symbol, bool isBaseCurrency, int decimalDigits = 2)
    {
        Code = code;
        Symbol = symbol;
        IsBaseCurrency = isBaseCurrency;
        DecimalDigits = decimalDigits;
        IsActive = true;
    }
}

public class ExchangeRate : AuditableEntity
{
    public string FromCurrencyCode { get; private set; } = default!;
    public string ToCurrencyCode { get; private set; } = default!;
    public decimal Rate { get; private set; }
    public DateTime RateDateUtc { get; private set; }

    private ExchangeRate() { }

    public ExchangeRate(string fromCurrencyCode, string toCurrencyCode, decimal rate, DateTime rateDateUtc)
    {
        FromCurrencyCode = fromCurrencyCode;
        ToCurrencyCode = toCurrencyCode;
        Rate = rate;
        RateDateUtc = rateDateUtc;
    }
}
