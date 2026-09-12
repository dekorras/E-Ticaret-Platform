using Dekorras.Application.Localization.Commands;
using Dekorras.Application.Localization.Queries;
using Dekorras.Domain.Localization;
using Dekorras.Infrastructure.ExchangeRates;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Localization;

/// <summary>Gerçek SQL Server'a karşı çalışır - `Domain.Localization.Currency`/`ExchangeRate` ve
/// `IExchangeRateProvider` (Faz 0/1'den beri hazır, hiç Application katmanından çağrılmıyordu)
/// Storefront'un GÖRÜNTÜLEME amaçlı çok para birimi desteğinin (bkz. plan §7) temelini oluşturur.
/// Sipariş/ödeme tutarları bu turdan sonra da HER ZAMAN TRY'de kalır - yalnızca GÖSTERİM değişir.</summary>
public sealed class CurrencyDisplayRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasCurrencyTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Currencies.AddRange(
            new Currency("TRY", "₺", isBaseCurrency: true),
            new Currency("USD", "$", isBaseCurrency: false),
            new Currency("EUR", "€", isBaseCurrency: false));
        await dbContext.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureDeletedAsync();
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task AktifParaBirimleriTemelParaBirimiOnceGelecekSekildeDoner()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var currencies = await new GetActiveCurrenciesQueryHandler(unitOfWork).Handle(new GetActiveCurrenciesQuery(), CancellationToken.None);

        Assert.Equal(3, currencies.Count);
        Assert.Equal("TRY", currencies.First().Code); // temel para birimi her zaman ilk sırada
        Assert.True(currencies.First().IsBaseCurrency);
    }

    [Fact]
    public async Task TemelParaBirimiIcinDonusumOraniDaima1Doner()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);
        var handler = new GetCurrencyConversionQueryHandler(unitOfWork, new ExchangeRateProvider());

        var conversion = await handler.Handle(new GetCurrencyConversionQuery("TRY"), CancellationToken.None);

        Assert.Equal("TRY", conversion.Code);
        Assert.Equal("₺", conversion.Symbol);
        Assert.Equal(1m, conversion.RateFromBase);
    }

    [Fact]
    public async Task YabanciParaBirimiIcinGercekSaglayicidanOranAlinirVeDogruSembolDoner()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);
        var handler = new GetCurrencyConversionQueryHandler(unitOfWork, new ExchangeRateProvider());

        var conversion = await handler.Handle(new GetCurrencyConversionQuery("USD"), CancellationToken.None);

        Assert.Equal("USD", conversion.Code);
        Assert.Equal("$", conversion.Symbol);
        // ExchangeRateProvider'ın sabit tablosu: 1 USD = 34 TRY -> 1 TRY = 1/34 USD.
        Assert.True(conversion.RateFromBase is > 0.029m and < 0.030m);

        // 1.000 TRY'lik bir tutar GÖRÜNTÜLEME için yaklaşık 29.4 USD'ye karşılık gelmeli.
        var displayedAmount = 1000m * conversion.RateFromBase;
        Assert.True(displayedAmount is > 29m and < 30m);
    }

    [Fact]
    public async Task TanimsizVeyaPasifParaBirimiTemelParaBirimineDuser()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);
        var handler = new GetCurrencyConversionQueryHandler(unitOfWork, new ExchangeRateProvider());

        // "XYZ" hiç tanımlı değil - geçersiz bir çerez değeriyle karşılaşınca sessizce temel para
        // birimine (TRY) düşmeli, hata fırlatmamalı.
        var conversion = await handler.Handle(new GetCurrencyConversionQuery("XYZ"), CancellationToken.None);

        Assert.Equal("TRY", conversion.Code);
        Assert.Equal(1m, conversion.RateFromBase);
    }

    [Fact]
    public async Task KurYenilemeIsiHerAktifYabanciParaBirimiIcinBirSnapshotYazarVeAyniGunTekrarlanmaz()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);
        var refreshHandler = new RefreshExchangeRatesCommandHandler(unitOfWork, new ExchangeRateProvider());

        var firstRunCount = await refreshHandler.Handle(new RefreshExchangeRatesCommand(), CancellationToken.None);
        Assert.Equal(2, firstRunCount); // USD ve EUR - TRY hariç 2 aktif yabancı para birimi

        var snapshotCountAfterFirstRun = await dbContext.ExchangeRates.CountAsync();
        Assert.Equal(2, snapshotCountAfterFirstRun);

        // Aynı gün İKİNCİ kez çalıştırmak yinelenen bir satır OLUŞTURMAMALI (günde bir anlık görüntü).
        var secondRunCount = await refreshHandler.Handle(new RefreshExchangeRatesCommand(), CancellationToken.None);
        Assert.Equal(0, secondRunCount);
        var snapshotCountAfterSecondRun = await dbContext.ExchangeRates.CountAsync();
        Assert.Equal(2, snapshotCountAfterSecondRun);
    }

    [Fact]
    public async Task DonusumSorgusuOnbelleklenmisKuruCanliSaglayiciyaTercihEder()
    {
        await using var dbContext = CreateDbContext();

        // Sağlayıcının canlı kuru (1/34 ≈ 0.0294) ile KASITLI OLARAK FARKLI bir önbellek satırı yaz -
        // sorgunun canlı sağlayıcıyı DEĞİL, bu önbelleği döndürdüğünü kanıtlar.
        dbContext.ExchangeRates.Add(new ExchangeRate("TRY", "USD", 0.05m, DateTime.UtcNow.Date));
        await dbContext.SaveChangesAsync();

        var unitOfWork = new UnitOfWork(dbContext);
        var handler = new GetCurrencyConversionQueryHandler(unitOfWork, new ExchangeRateProvider());
        var conversion = await handler.Handle(new GetCurrencyConversionQuery("USD"), CancellationToken.None);

        Assert.Equal(0.05m, conversion.RateFromBase);
    }
}
