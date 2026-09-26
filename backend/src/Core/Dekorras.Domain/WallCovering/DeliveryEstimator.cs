namespace Dekorras.Domain.WallCovering;

public sealed record DeliveryEstimate(DateOnly ProductionStart, DateOnly EarliestDelivery, DateOnly LatestDelivery);

public sealed record DeliveryRules(int ProductionMinDays = 1, int ProductionMaxDays = 2, int ShippingMinDays = 1, int ShippingMaxDays = 3, int CutoffHour = 14);

/// <summary>Türkiye resmi tatilleri. Sabit tarihliler her yıl; dini bayramlar hicri takvime bağlı
/// olduğundan yıl yıl tablo halinde tutulur.
/// VARSAYIM: dini bayram tarihleri Diyanet'in yayımladığı takvime göre 2025–2028 için girildi;
/// arife günleri (yarım gün) iş günü sayılır. Tablo dışındaki yıllar veya ek kapanış günleri için
/// admin ayarındaki ek tatil listesi (<see cref="DeliveryEstimator"/>'a extraHolidays) kullanılır.</summary>
public static class TurkishHolidays
{
    private static readonly (int Month, int Day)[] Fixed =
    [
        (1, 1),   // Yılbaşı
        (4, 23),  // Ulusal Egemenlik ve Çocuk Bayramı
        (5, 1),   // Emek ve Dayanışma Günü
        (5, 19),  // Atatürk'ü Anma, Gençlik ve Spor Bayramı
        (7, 15),  // Demokrasi ve Milli Birlik Günü
        (8, 30),  // Zafer Bayramı
        (10, 29), // Cumhuriyet Bayramı
    ];

    // (başlangıç, gün sayısı): Ramazan Bayramı 3 gün, Kurban Bayramı 4 gün
    private static readonly (DateOnly Start, int Days)[] Religious =
    [
        (new DateOnly(2025, 3, 30), 3), (new DateOnly(2025, 6, 6), 4),
        (new DateOnly(2026, 3, 20), 3), (new DateOnly(2026, 5, 27), 4),
        (new DateOnly(2027, 3, 9), 3), (new DateOnly(2027, 5, 16), 4),
        (new DateOnly(2028, 2, 26), 3), (new DateOnly(2028, 5, 5), 4),
    ];

    public static bool IsHoliday(DateOnly date) =>
        Fixed.Any(f => f.Month == date.Month && f.Day == date.Day)
        || Religious.Any(r => date >= r.Start && date < r.Start.AddDays(r.Days));
}

/// <summary>Tahmini teslim tarihi aralığı (spec 1.7 - IDeliveryEstimator'ın saf hesap kısmı).
/// Hafta sonu ve resmi tatiller iş günü sayılmaz; kesim saatinden (14:00) sonra verilen sipariş
/// üretime ertesi iş günü başlar. Üretim ilk iş günü "1. gün" sayılır, kargo üretimin bittiği
/// günün ertesinden itibaren sayılır.</summary>
public static class DeliveryEstimator
{
    private static readonly TimeZoneInfo TurkeyTimeZone = ResolveTurkeyTimeZone();

    public static DeliveryEstimate Estimate(DateTime orderedAtUtc, DeliveryRules? rules = null, IReadOnlyCollection<DateOnly>? extraHolidays = null)
    {
        rules ??= new DeliveryRules();
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(orderedAtUtc, DateTimeKind.Utc), TurkeyTimeZone);
        var orderDay = DateOnly.FromDateTime(local);

        bool IsBusinessDay(DateOnly d) =>
            d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday
            && !TurkishHolidays.IsHoliday(d)
            && (extraHolidays is null || !extraHolidays.Contains(d));

        DateOnly NextBusinessDay(DateOnly d)
        {
            do d = d.AddDays(1); while (!IsBusinessDay(d));
            return d;
        }

        DateOnly AddBusinessDays(DateOnly d, int days)
        {
            for (var i = 0; i < days; i++) d = NextBusinessDay(d);
            return d;
        }

        var start = IsBusinessDay(orderDay) && local.Hour < rules.CutoffHour ? orderDay : NextBusinessDay(orderDay);

        var earliestShip = AddBusinessDays(start, rules.ProductionMinDays - 1);
        var latestShip = AddBusinessDays(start, rules.ProductionMaxDays - 1);

        return new DeliveryEstimate(start, AddBusinessDays(earliestShip, rules.ShippingMinDays), AddBusinessDays(latestShip, rules.ShippingMaxDays));
    }

    /// <summary>İş günü ekler (hafta sonu + resmi tatil + ek tatiller atlanır) - ör. tasarım talebi SLA'sı.
    /// Saat bilgisi korunur; başlangıç iş günü değilse sayım bir sonraki iş gününden başlar.</summary>
    public static DateTime AddBusinessDays(DateTime startUtc, int days, IReadOnlyCollection<DateOnly>? extraHolidays = null)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), TurkeyTimeZone);
        var day = DateOnly.FromDateTime(local);
        bool IsBusinessDay(DateOnly d) =>
            d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && !TurkishHolidays.IsHoliday(d) && (extraHolidays is null || !extraHolidays.Contains(d));

        var timeOfDay = local.TimeOfDay;
        if (!IsBusinessDay(day)) timeOfDay = TimeSpan.FromHours(9); // hafta sonu gelen talep: iş günü sabahından say
        for (var i = 0; i < days; i++)
        {
            do day = day.AddDays(1); while (!IsBusinessDay(day));
        }
        return TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.FromTimeSpan(timeOfDay)), TurkeyTimeZone);
    }

    private static TimeZoneInfo ResolveTurkeyTimeZone()
    {
        foreach (var id in new[] { "Europe/Istanbul", "Turkey Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        // Türkiye 2016'dan beri kalıcı UTC+3.
        return TimeZoneInfo.CreateCustomTimeZone("TR", TimeSpan.FromHours(3), "Türkiye", "Türkiye");
    }
}
