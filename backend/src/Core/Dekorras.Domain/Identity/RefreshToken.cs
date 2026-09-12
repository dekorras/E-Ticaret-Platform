using Dekorras.Domain.Common;

namespace Dekorras.Domain.Identity;

/// <summary>
/// Dekorras.Api'nin JWT erişim token'ı yenileme akışı için saklanan opak yenileme jetonu.
/// Ham jeton İSTEMCİYE döner, veritabanında yalnızca SHA-256 özeti (TokenHash) tutulur - bir
/// veritabanı sızıntısı doğrudan geçerli bir jeton anlamına gelmesin diye. Her yenilemede eski
/// jeton iptal edilip yenisi verilir (rotasyon) - çalınmış bir jetonun tekrar kullanılmaya
/// çalışılması durumunda zincirin kırıldığı fark edilebilir.
/// </summary>
public class RefreshToken : AuditableEntity
{
    public string IdentityUserId { get; private set; } = default!;
    public string TokenHash { get; private set; } = default!;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }

    public bool IsActive => RevokedAtUtc is null && ExpiresAtUtc > DateTime.UtcNow;

    private RefreshToken() { }

    public RefreshToken(string identityUserId, string tokenHash, DateTime expiresAtUtc)
    {
        IdentityUserId = identityUserId;
        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
    }

    public void Revoke() => RevokedAtUtc = DateTime.UtcNow;
}
