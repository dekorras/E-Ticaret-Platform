namespace Dekorras.IntegrationTests;

/// <summary>
/// Yerel SQLEXPRESS test veritabanlarına bağlanmak için kullanılan `sa` şifresi - önceden HER
/// regresyon test dosyasına doğrudan gömülüydü (45+ dosya), GitHub'a genel/public bir repo olarak
/// aktarılmadan önce (bkz. backend/README.md) tek bir yere toplanıp `DEKORRAS_SQL_PASSWORD` ortam
/// değişkeninden okunacak şekilde değiştirildi. Bu makinede (geliştirici ortamı) bu değişken zaten
/// kalıcı olarak ayarlı; depoyu başka bir makinede çalıştıran biri kendi SQLEXPRESS `sa`
/// şifresini bu değişkene atamalıdır.
/// </summary>
internal static class TestSqlPassword
{
    public static readonly string Value =
        Environment.GetEnvironmentVariable("DEKORRAS_SQL_PASSWORD")
        ?? throw new InvalidOperationException(
            "DEKORRAS_SQL_PASSWORD ortam değişkeni ayarlanmamış - yerel SQLEXPRESS 'sa' şifrenizi " +
            "bu değişkene atayın (ör. [Environment]::SetEnvironmentVariable('DEKORRAS_SQL_PASSWORD', '...', 'User')).");
}
