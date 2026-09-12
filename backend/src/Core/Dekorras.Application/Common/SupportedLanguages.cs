namespace Dekorras.Application.Common;

/// <summary>Hedef diller TR/EN/DE/FR/NL/ES/AR, Arapça RTL (bkz. plan §7). `ProductTranslation`/
/// `CategoryTranslation` zaten herhangi bir `LanguageCode` string'ini kabul ediyor - bu liste yalnızca
/// Admin çeviri ekranlarında (ör. ProductEdit) hangi dillerin seçenek olarak gösterileceğini belirler,
/// veritabanı seviyesinde bir kısıt DEĞİLDİR. Yeni bir dil eklemek yalnızca buraya bir satır eklemek
/// anlamına gelir.</summary>
public static class SupportedLanguages
{
    public static readonly IReadOnlyList<(string Code, string DisplayName, bool IsRtl)> All =
    [
        ("tr", "Türkçe", false),
        ("en", "English", false),
        ("de", "Deutsch", false),
        ("fr", "Français", false),
        ("nl", "Nederlands", false),
        ("es", "Español", false),
        ("ar", "العربية", true), // Arapça - tek RTL dil
    ];
}
