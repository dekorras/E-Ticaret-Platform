using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Localization;
using MediatR;

namespace Dekorras.Application.Localization.Commands;

/// <summary>Hangfire tarafından periyodik olarak (bkz. plan §7) tetiklenir - temel para biriminden
/// (TRY) her AKTİF yabancı para birimine güncel kuru `IExchangeRateProvider`'dan (şimdilik sabit
/// kurlu bir stub, ileride GERÇEK TCMB/3. parti API) çekip o günün `ExchangeRate` anlık görüntüsünü
/// kaydeder. `GetCurrencyConversionQuery` bu önbelleği tercih eder - her sayfa görüntülemesinde
/// canlı bir kur sorgusu YAPILMAZ.</summary>
public sealed record RefreshExchangeRatesCommand : IRequest<int>;

public sealed class RefreshExchangeRatesCommandHandler(IUnitOfWork unitOfWork, IExchangeRateProvider exchangeRateProvider)
    : IRequestHandler<RefreshExchangeRatesCommand, int>
{
    public async Task<int> Handle(RefreshExchangeRatesCommand request, CancellationToken cancellationToken)
    {
        var currencyRepository = unitOfWork.Repository<Currency>();
        var baseCurrency = currencyRepository.Query().FirstOrDefault(c => c.IsBaseCurrency);
        if (baseCurrency is null) return 0;

        var targetCurrencies = currencyRepository.Query()
            .Where(c => c.IsActive && c.Code != baseCurrency.Code)
            .ToList();

        var rateRepository = unitOfWork.Repository<ExchangeRate>();
        var today = DateTime.UtcNow.Date;
        var updatedCount = 0;

        foreach (var target in targetCurrencies)
        {
            var alreadySnapshotted = rateRepository.Query().Any(r =>
                r.FromCurrencyCode == baseCurrency.Code && r.ToCurrencyCode == target.Code && r.RateDateUtc == today);
            if (alreadySnapshotted) continue; // günde bir kez - aynı gün içinde tekrar çalışırsa yinelenen satır oluşturmaz

            var rate = await exchangeRateProvider.GetRateAsync(baseCurrency.Code, target.Code, cancellationToken);
            await rateRepository.AddAsync(new ExchangeRate(baseCurrency.Code, target.Code, rate, today), cancellationToken);
            updatedCount++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return updatedCount;
    }
}
