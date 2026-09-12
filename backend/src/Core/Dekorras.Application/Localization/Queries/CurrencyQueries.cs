using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Localization;
using MediatR;

namespace Dekorras.Application.Localization.Queries;

public sealed record CurrencyDto(string Code, string Symbol, bool IsBaseCurrency, int DecimalDigits);

public sealed record GetActiveCurrenciesQuery : IRequest<IReadOnlyCollection<CurrencyDto>>;

public sealed class GetActiveCurrenciesQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetActiveCurrenciesQuery, IReadOnlyCollection<CurrencyDto>>
{
    public Task<IReadOnlyCollection<CurrencyDto>> Handle(GetActiveCurrenciesQuery request, CancellationToken cancellationToken)
    {
        var currencies = unitOfWork.Repository<Currency>().Query()
            .Where(c => c.IsActive)
            .OrderByDescending(c => c.IsBaseCurrency)
            .ThenBy(c => c.Code)
            .Select(c => new CurrencyDto(c.Code, c.Symbol, c.IsBaseCurrency, c.DecimalDigits))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<CurrencyDto>>(currencies);
    }
}

/// <summary>Storefront'un GÖRÜNTÜLEME amaçlı para birimi dönüşümü için tek durak noktası (bkz. plan
/// §7: "Ürün fiyatları temel para biriminde (TRY) tutulur; storefront ... güncel kur üzerinden
/// müşterinin seçtiği para biriminde gösterim yapar"). Yalnızca GÖRÜNTÜLEME - sipariş/ödeme tutarları
/// HER ZAMAN temel para biriminde (TRY) hesaplanır ve tahsil edilir, bu sorgu o değerleri DEĞİŞTİRMEZ.</summary>
public sealed record CurrencyConversionDto(string Code, string Symbol, int DecimalDigits, decimal RateFromBase);

public sealed record GetCurrencyConversionQuery(string TargetCurrencyCode) : IRequest<CurrencyConversionDto>;

public sealed class GetCurrencyConversionQueryHandler(IUnitOfWork unitOfWork, IExchangeRateProvider exchangeRateProvider)
    : IRequestHandler<GetCurrencyConversionQuery, CurrencyConversionDto>
{
    public async Task<CurrencyConversionDto> Handle(GetCurrencyConversionQuery request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Currency>();
        var baseCurrency = repository.Query().FirstOrDefault(c => c.IsBaseCurrency);
        var target = repository.Query().FirstOrDefault(c => c.Code == request.TargetCurrencyCode && c.IsActive);

        if (baseCurrency is null || target is null || target.Code == baseCurrency.Code)
        {
            var fallback = target ?? baseCurrency;
            return new CurrencyConversionDto(fallback?.Code ?? request.TargetCurrencyCode, fallback?.Symbol ?? "₺", fallback?.DecimalDigits ?? 2, 1m);
        }

        // `RefreshExchangeRatesCommand`'ın Hangfire ile periyodik yazdığı EN GÜNCEL önbelleklenmiş kur
        // tercih edilir (bkz. plan §7 - "Hangfire ile periyodik güncellenir"); henüz hiç iş çalışmadıysa
        // (taze bir ortam) sağlayıcıdan CANLI bir kur çekilir - bu, önceki davranışla TAM uyumludur.
        var cachedRate = unitOfWork.Repository<ExchangeRate>().Query()
            .Where(r => r.FromCurrencyCode == baseCurrency.Code && r.ToCurrencyCode == target.Code)
            .OrderByDescending(r => r.RateDateUtc)
            .Select(r => (decimal?)r.Rate)
            .FirstOrDefault();

        var rate = cachedRate ?? await exchangeRateProvider.GetRateAsync(baseCurrency.Code, target.Code, cancellationToken);
        return new CurrencyConversionDto(target.Code, target.Symbol, target.DecimalDigits, rate);
    }
}
