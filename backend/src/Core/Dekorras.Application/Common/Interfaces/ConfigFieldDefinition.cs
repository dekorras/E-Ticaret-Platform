namespace Dekorras.Application.Common.Interfaces;

public enum ConfigFieldType { Text, Password, Select, Number, Boolean }

/// <summary>
/// Bir connector'ın (ör. iyzico, Trendyol) admin panelinden yapılandırılması için ihtiyaç duyduğu
/// tek bir alanı tanımlar. Admin panelindeki "Entegrasyonlar" ekranı bu tanıma bakarak
/// otomatik/metadata-güdümlü bir form render eder - yeni bir connector eklendiğinde
/// admin UI'da elle yeni bir ekran yazmaya gerek kalmaz (bkz. plan §4.1).
/// </summary>
public sealed record ConfigFieldDefinition(
    string Key,
    string Label,
    ConfigFieldType Type,
    bool IsRequired,
    IReadOnlyCollection<string>? SelectOptions = null);
