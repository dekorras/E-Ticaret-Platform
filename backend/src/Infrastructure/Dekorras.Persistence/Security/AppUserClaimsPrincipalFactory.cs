using System.Security.Claims;
using Dekorras.Domain.SystemAdmin;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dekorras.Persistence.Security;

public static class AppClaimTypes
{
    public const string Permission = "permission";
    public const string SysAdmin = "sysadmin";
}

/// <summary>
/// Kullanıcı oturum açtığında (veya SignInManager.RefreshSignInAsync ile), AdminProfile'ına
/// bağlı Role'lerin verdiği TÜM Permission.Key değerlerini "permission" claim'leri olarak
/// oturum çerezine gömer. Bu sayede admin panelindeki [Authorize(Policy = "...")] kontrolleri
/// her istekte veritabanına gitmeden, doğrudan çerezdeki claim'lerden çalışır (bkz. §10.1, §11).
/// </summary>
public sealed class AppUserClaimsPrincipalFactory(
    UserManager<IdentityUser> userManager,
    IOptions<IdentityOptions> optionsAccessor,
    ApplicationDbContext dbContext)
    : UserClaimsPrincipalFactory<IdentityUser>(userManager, optionsAccessor)
{
    public override async Task<ClaimsPrincipal> CreateAsync(IdentityUser user)
    {
        var principal = await base.CreateAsync(user);
        var identity = (ClaimsIdentity)principal.Identity!;

        var adminRoles = dbContext.AdminProfiles
            .Where(p => p.IdentityUserId == user.Id)
            .SelectMany(p => p.Roles)
            .Join(dbContext.AppRoles, ar => ar.RoleId, r => r.Id, (ar, r) => r);

        // "SysAdmin" adlı role sahip kullanıcı, verilmiş izinlerden BAĞIMSIZ olarak her yere
        // erişebilir (bkz. plan "Admin Panel Yeniden Yapılandırma" Faz 1) - politikalar bu claim'i
        // RequireAssertion ile ayrıca kontrol eder. NOT: her iki sorgu da AYRI, tek parça
        // IQueryable zincirleri olarak SQL'e çevrilir - Role'ü önce materyalize edip SONRA
        // r.Permissions'a dokunmak (bu proje genelinde tekrar eden bir EF Core hatası -
        // bkz. backend/README.md "Önemli mimari not") navigasyonu SESSİZCE boş döndürürdü.
        var isSysAdmin = await adminRoles.AnyAsync(r => r.Name == "SysAdmin");
        if (isSysAdmin)
            identity.AddClaim(new Claim(AppClaimTypes.SysAdmin, "true"));

        var permissionKeys = await adminRoles
            .SelectMany(r => r.Permissions)
            .Join(dbContext.Permissions, rp => rp.PermissionId, perm => perm.Id, (rp, perm) => perm.Key)
            .Distinct()
            .ToListAsync();

        foreach (var key in permissionKeys)
            identity.AddClaim(new Claim(AppClaimTypes.Permission, key));

        return principal;
    }
}
