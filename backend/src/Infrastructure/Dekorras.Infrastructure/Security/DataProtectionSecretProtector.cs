using Dekorras.Application.Common.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace Dekorras.Infrastructure.Security;

/// <summary>Sağlayıcı anahtarları (API key/secret) her zaman Data Protection API ile şifreli
/// saklanır (bkz. plan §4.1, §11) - asla düz metin olarak veritabanına yazılmaz.</summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private const string Purpose = "Dekorras.IntegrationProvider.ConfigValues.v1";
    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector(Purpose);

    public string Protect(string plainText) => _protector.Protect(plainText);
    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}
