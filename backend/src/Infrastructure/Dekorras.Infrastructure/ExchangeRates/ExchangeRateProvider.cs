using Dekorras.Application.Common.Interfaces;

namespace Dekorras.Infrastructure.ExchangeRates;

/// <summary>
/// TODO: TCMB (Türkiye Cumhuriyet Merkez Bankası) günlük kur servisi veya üçüncü parti bir kur
/// API'si ile değiştirilecek; Hangfire ile periyodik güncellenir (bkz. plan §7). Şimdilik sabit
/// kurlarla çalışır ki storefront/checkout akışı geliştirme ortamında kesintisiz test edilebilsin.
/// </summary>
public sealed class ExchangeRateProvider : IExchangeRateProvider
{
    private static readonly Dictionary<string, decimal> FixedRatesToTry = new()
    {
        ["TRY"] = 1m,
        ["USD"] = 34m,
        ["EUR"] = 37m,
        ["GBP"] = 43m
    };

    public Task<decimal> GetRateAsync(string fromCurrencyCode, string toCurrencyCode, CancellationToken cancellationToken)
    {
        if (fromCurrencyCode == toCurrencyCode) return Task.FromResult(1m);

        var fromRate = FixedRatesToTry.GetValueOrDefault(fromCurrencyCode, 1m);
        var toRate = FixedRatesToTry.GetValueOrDefault(toCurrencyCode, 1m);
        return Task.FromResult(fromRate / toRate);
    }
}
