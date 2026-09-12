namespace Dekorras.Application.Common;

/// <summary>Storefront header'ının (üst bilgi çubuğu + logo/arama/hesap satırı + mega menü) "container"
/// davranışını yönetim panelinden yönetilebilir kılar - bkz. plan "header alanının ortalı geniş
/// olabilecek şekilde ayarlanabilmesi". `Setting` (bkz. `ContactSettingKeys`) ile AYNI genel
/// anahtar-değer deposu yeniden kullanılıyor, yeni bir tablo/entity GEREKMEDİ.</summary>
public static class LayoutSettingKeys
{
    /// <summary>Değer: "fluid" (kenardan kenara tam genişlik - mevcut/varsayılan davranış) veya
    /// "boxed" (ortalanmış, <see cref="HeaderContainerMaxWidth"/> ile sınırlı genişlik).</summary>
    public const string HeaderContainerMode = "Layout.HeaderContainerMode";

    /// <summary>Yalnızca <see cref="HeaderContainerMode"/> "boxed" iken kullanılır - CSS `max-width`
    /// değeri olarak DOĞRUDAN yazılır (ör. "1600px"). Boş/ayarlanmamışsa 1600px varsayılır.</summary>
    public const string HeaderContainerMaxWidth = "Layout.HeaderContainerMaxWidth";

    public static readonly IReadOnlyList<string> All = [HeaderContainerMode, HeaderContainerMaxWidth];

    public const string ModeFluid = "fluid";
    public const string ModeBoxed = "boxed";
    public const string DefaultMaxWidth = "1320px";

    /// <summary>Bootstrap 5'in KENDİ `.container` sınıfının breakpoint başına ürettiği resmi
    /// maksimum genişlikler (bkz. Bootstrap `_variables.scss` - `$container-max-widths`). Admin
    /// panelinde serbest metin/sayı girişi yerine BU listeden seçim yaptırılır - kullanıcının
    /// isteği: "px yerine bootstrap yapılandırmasını liste şeklinde göster".</summary>
    public sealed record BootstrapContainerBreakpoint(string Key, string Label, int MaxWidthPx);

    public static readonly IReadOnlyList<BootstrapContainerBreakpoint> BootstrapContainerBreakpoints =
    [
        new("sm", "Small (sm) - ekran ≥576px iken 540px genişlik", 540),
        new("md", "Medium (md) - ekran ≥768px iken 720px genişlik", 720),
        new("lg", "Large (lg) - ekran ≥992px iken 960px genişlik", 960),
        new("xl", "Extra Large (xl) - ekran ≥1200px iken 1140px genişlik", 1140),
        new("xxl", "Extra Extra Large (xxl) - ekran ≥1400px iken 1320px genişlik (Bootstrap'ın en geniş standart container'ı)", 1320),
    ];
}
