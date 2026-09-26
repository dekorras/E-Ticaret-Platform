using System.Globalization;
using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.SystemAdmin;
using Dekorras.Domain.WallCovering;

namespace Dekorras.Application.WallCovering;

/// <summary>Duvar kağıdı modülünün admin'den yönetilen ayarları. `ContactSettingKeys`/`LayoutSettingKeys`
/// ile AYNI genel `Setting` anahtar-değer deposu kullanılır, yeni tablo gerekmez.</summary>
public static class WallCoveringSettingKeys
{
    public const string ChargeBleed = "WallCovering.ChargeBleed";
    public const string FreeShippingThresholdTry = "WallCovering.FreeShippingThresholdTry";
    public const string GlueFreeThresholdTry = "WallCovering.GlueFreeThresholdTry";
    public const string RoomPreviewFreeQuota = "WallCovering.RoomPreviewFreeQuota";
    public const string DefaultSceneId = "WallCovering.DefaultSceneId";
    public const string DefaultWidthCm = "WallCovering.DefaultWidthCm";
    public const string DefaultHeightCm = "WallCovering.DefaultHeightCm";
    public const string MinPrintDpi = "WallCovering.MinPrintDpi";
    public const string ProofAutoApproveHours = "WallCovering.ProofAutoApproveHours";
    public const string ExtraHolidays = "WallCovering.ExtraHolidays";
    public const string CategorySlugs = "WallCovering.CategorySlugs";
    public const string DesignRequestSlaBusinessDays = "WallCovering.DesignRequestSlaBusinessDays";
    public const string AdminNotificationEmail = "WallCovering.AdminNotificationEmail";

    /// <summary>"Duvarında Gör" bağlantısının gösterileceği ziyaretçi yüzdesi (0–100). Boşsa appsettings
    /// `FeatureManagement:WallPreviewLink` değeri, o da yoksa %100 geçerlidir.</summary>
    public const string WallPreviewLinkPercent = "WallCovering.WallPreviewLinkPercent";

    /// <summary>E-postalardaki mutlak bağlantılar için sitenin kök adresi (ör. "https://www.dekorras.com").</summary>
    public const string PublicBaseUrl = "WallCovering.PublicBaseUrl";

    public static readonly IReadOnlyList<string> All =
    [
        ChargeBleed, FreeShippingThresholdTry, GlueFreeThresholdTry, RoomPreviewFreeQuota, DefaultSceneId, DefaultWidthCm,
        DefaultHeightCm, MinPrintDpi, ProofAutoApproveHours, ExtraHolidays, CategorySlugs, DesignRequestSlaBusinessDays,
        AdminNotificationEmail, WallPreviewLinkPercent, PublicBaseUrl
    ];
}

public sealed record WallCoveringSettings(
    bool ChargeBleed,
    decimal? FreeShippingThresholdTry,
    decimal? GlueFreeThresholdTry,
    int RoomPreviewFreeQuota,
    Guid? DefaultSceneId,
    decimal DefaultWidthCm,
    decimal DefaultHeightCm,
    int MinPrintDpi,
    int ProofAutoApproveHours,
    IReadOnlyCollection<DateOnly> ExtraHolidays,
    IReadOnlyList<string> CategorySlugs,
    int DesignRequestSlaBusinessDays,
    string? AdminNotificationEmail,
    string? PublicBaseUrl = null)
{
    // VARSAYIM: ücretsiz kargo eşiği varsayılan olarak KAPALI (null) - açılırsa mevcut tüm checkout'u
    // etkiler, bu yüzden admin bilinçli olarak girmeli. Tutkal eşiği spec'teki 1.000 ₺.
    public static readonly WallCoveringSettings Default = new(
        ChargeBleed: true,
        FreeShippingThresholdTry: null,
        GlueFreeThresholdTry: 1000m,
        RoomPreviewFreeQuota: 10,
        DefaultSceneId: null,
        DefaultWidthCm: 300m,
        DefaultHeightCm: 250m,
        MinPrintDpi: 72,
        ProofAutoApproveHours: 24,
        ExtraHolidays: [],
        CategorySlugs: ["duvar-kagitlari-129", "posterler-141"],
        DesignRequestSlaBusinessDays: 2,
        AdminNotificationEmail: null);

    public static WallCoveringSettings Load(IUnitOfWork unitOfWork)
    {
        var values = unitOfWork.Repository<Setting>().Query()
            .Where(s => s.Key.StartsWith("WallCovering."))
            .ToDictionary(s => s.Key, s => s.Value);

        string? Get(string key) => values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
        decimal? Dec(string key) => decimal.TryParse(Get(key)?.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
        int? Int(string key) => int.TryParse(Get(key), out var i) ? i : null;
        var def = Default;

        var holidays = (Get(WallCoveringSettingKeys.ExtraHolidays) ?? "")
            .Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : (DateOnly?)null)
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .ToList();

        var slugs = Get(WallCoveringSettingKeys.CategorySlugs)?
            .Split([',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return new WallCoveringSettings(
            ChargeBleed: Get(WallCoveringSettingKeys.ChargeBleed) is { } cb ? cb is "true" or "1" : def.ChargeBleed,
            // Ayar anahtarı hiç yoksa varsayılan; "0" veya boş ise kapalı.
            FreeShippingThresholdTry: values.ContainsKey(WallCoveringSettingKeys.FreeShippingThresholdTry)
                ? Dec(WallCoveringSettingKeys.FreeShippingThresholdTry) is > 0 and var fs ? fs : null
                : def.FreeShippingThresholdTry,
            GlueFreeThresholdTry: values.ContainsKey(WallCoveringSettingKeys.GlueFreeThresholdTry)
                ? Dec(WallCoveringSettingKeys.GlueFreeThresholdTry) is > 0 and var gf ? gf : null
                : def.GlueFreeThresholdTry,
            RoomPreviewFreeQuota: Int(WallCoveringSettingKeys.RoomPreviewFreeQuota) ?? def.RoomPreviewFreeQuota,
            DefaultSceneId: Guid.TryParse(Get(WallCoveringSettingKeys.DefaultSceneId), out var sceneId) ? sceneId : null,
            DefaultWidthCm: Dec(WallCoveringSettingKeys.DefaultWidthCm) is >= WallDimensions.MinSideCm and var w ? w : def.DefaultWidthCm,
            DefaultHeightCm: Dec(WallCoveringSettingKeys.DefaultHeightCm) is >= WallDimensions.MinSideCm and var h ? h : def.DefaultHeightCm,
            MinPrintDpi: Int(WallCoveringSettingKeys.MinPrintDpi) ?? def.MinPrintDpi,
            ProofAutoApproveHours: Int(WallCoveringSettingKeys.ProofAutoApproveHours) ?? def.ProofAutoApproveHours,
            ExtraHolidays: holidays,
            CategorySlugs: slugs is { Count: > 0 } ? slugs : def.CategorySlugs,
            DesignRequestSlaBusinessDays: Int(WallCoveringSettingKeys.DesignRequestSlaBusinessDays) ?? def.DesignRequestSlaBusinessDays,
            AdminNotificationEmail: Get(WallCoveringSettingKeys.AdminNotificationEmail),
            PublicBaseUrl: Get(WallCoveringSettingKeys.PublicBaseUrl)?.TrimEnd('/'));
    }
}
