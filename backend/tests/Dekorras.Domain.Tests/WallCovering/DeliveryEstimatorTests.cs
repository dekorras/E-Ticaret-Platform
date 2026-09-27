using Dekorras.Domain.WallCovering;

namespace Dekorras.Domain.Tests.WallCovering;

public class DeliveryEstimatorTests
{
    // Türkiye UTC+3: 07:00 UTC = 10:00 TR, 12:00 UTC = 15:00 TR
    private static DateTime Utc(int year, int month, int day, int hour) => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PazartesiSabah_AyniGunUretimeBaslar()
    {
        var e = DeliveryEstimator.Estimate(Utc(2026, 10, 5, 7));

        Assert.Equal(new DateOnly(2026, 10, 5), e.ProductionStart);
        Assert.Equal(new DateOnly(2026, 10, 6), e.EarliestDelivery);
        Assert.Equal(new DateOnly(2026, 10, 9), e.LatestDelivery);
    }

    [Fact]
    public void Saat14Sonrasi_ErtesiIsGunuBaslar()
    {
        var e = DeliveryEstimator.Estimate(Utc(2026, 10, 5, 12));

        Assert.Equal(new DateOnly(2026, 10, 6), e.ProductionStart);
        Assert.Equal(new DateOnly(2026, 10, 7), e.EarliestDelivery);
        Assert.Equal(new DateOnly(2026, 10, 12), e.LatestDelivery); // hafta sonu atlanır
    }

    [Fact]
    public void CumaAksami_PazartesiBaslar_HaftaSonuSayilmaz()
    {
        var e = DeliveryEstimator.Estimate(Utc(2026, 10, 9, 13));

        Assert.Equal(new DateOnly(2026, 10, 12), e.ProductionStart);
        Assert.Equal(new DateOnly(2026, 10, 13), e.EarliestDelivery);
        Assert.Equal(new DateOnly(2026, 10, 16), e.LatestDelivery);
    }

    [Fact]
    public void CumhuriyetBayrami_IsGunuSayilmaz()
    {
        // 28 Ekim 2026 Çarşamba, 29 Ekim Perşembe resmi tatil
        var e = DeliveryEstimator.Estimate(Utc(2026, 10, 28, 7));

        Assert.Equal(new DateOnly(2026, 10, 28), e.ProductionStart);
        Assert.Equal(new DateOnly(2026, 10, 30), e.EarliestDelivery);
        Assert.Equal(new DateOnly(2026, 11, 4), e.LatestDelivery);
    }

    [Fact]
    public void KurbanBayrami_TumBayramGunleriAtlanir()
    {
        // 26 Mayıs 2026 Salı; 27–30 Mayıs Kurban Bayramı, 31 Pazar → ilk iş günü 1 Haziran
        var e = DeliveryEstimator.Estimate(Utc(2026, 5, 26, 7));

        Assert.Equal(new DateOnly(2026, 5, 26), e.ProductionStart);
        Assert.Equal(new DateOnly(2026, 6, 1), e.EarliestDelivery);
    }

    [Fact]
    public void EkTatilListesi_Uygulanir()
    {
        var e = DeliveryEstimator.Estimate(Utc(2026, 10, 5, 7), extraHolidays: [new DateOnly(2026, 10, 6)]);
        Assert.Equal(new DateOnly(2026, 10, 7), e.EarliestDelivery);
    }

    [Fact]
    public void HaftaSonuVerilenSiparis_PazartesiBaslar()
    {
        var e = DeliveryEstimator.Estimate(Utc(2026, 10, 10, 7)); // Cumartesi
        Assert.Equal(new DateOnly(2026, 10, 12), e.ProductionStart);
    }

    // 2026-10-04 pazar, 2026-10-07 çarşamba, 2026-10-09 cuma (TR saati = UTC+3).
    [Theory]
    [InlineData(2026, 10, 4, 18, 2026, 10, 7, 6)]   // pazar 21:00 → pazartesi sabahından 2 iş günü → çarşamba 09:00
    [InlineData(2026, 10, 7, 7, 2026, 10, 9, 7)]    // çarşamba 10:00 → cuma 10:00
    [InlineData(2026, 10, 9, 12, 2026, 10, 13, 12)] // cuma 15:00 → salı 15:00 (hafta sonu atlanır)
    public void IsGunuEkleme_HaftaSonuTalebiPazartesiSabahindanSayilir(int y, int m, int d, int h, int ey, int em, int ed, int eh)
    {
        Assert.Equal(Utc(ey, em, ed, eh), DeliveryEstimator.AddBusinessDays(Utc(y, m, d, h), 2));
    }
}
