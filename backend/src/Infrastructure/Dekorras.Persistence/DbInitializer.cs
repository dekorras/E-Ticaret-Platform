using Dekorras.Domain.Customers;
using Dekorras.Domain.Localization;
using Dekorras.Domain.SystemAdmin;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.Persistence;

/// <summary>Geliştirme ortamı için başlangıç verisi: e-ihracat dil/para birimi listesi (bkz. §7),
/// Bireysel/Kurumsal müşteri grupları (bkz. §6) ve her iki modüle de erişimi olan bir başlangıç
/// yönetici hesabı (bkz. §10.1, §11 RBAC). Üretim ortamında migration'lar dışında çalıştırılmamalıdır.</summary>
public static class DbInitializer
{
    public const string SeedAdminEmail = "admin@dekorras.com";
    public const string SeedAdminPassword = "Dekorras38*";

    public static async Task SeedAsync(ApplicationDbContext dbContext, UserManager<IdentityUser> userManager, CancellationToken cancellationToken = default)
    {
        if (!await dbContext.Languages.AnyAsync(cancellationToken))
        {
            dbContext.Languages.AddRange(
                new Language("tr", "Türkçe", isRightToLeft: false, displayOrder: 1),
                new Language("en", "English", isRightToLeft: false, displayOrder: 2),
                new Language("de", "Deutsch", isRightToLeft: false, displayOrder: 3),
                new Language("fr", "Français", isRightToLeft: false, displayOrder: 4),
                new Language("nl", "Nederlands", isRightToLeft: false, displayOrder: 5),
                new Language("es", "Español", isRightToLeft: false, displayOrder: 6),
                new Language("ar", "العربية", isRightToLeft: true, displayOrder: 7));
        }

        if (!await dbContext.Currencies.AnyAsync(cancellationToken))
        {
            dbContext.Currencies.AddRange(
                new Currency("TRY", "₺", isBaseCurrency: true),
                new Currency("USD", "$", isBaseCurrency: false),
                new Currency("EUR", "€", isBaseCurrency: false),
                new Currency("GBP", "£", isBaseCurrency: false));
        }

        if (!await dbContext.CustomerGroups.AnyAsync(cancellationToken))
        {
            dbContext.CustomerGroups.AddRange(
                new CustomerGroup("Bireysel"),
                new CustomerGroup("Kurumsal"));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await SeedAdminUserAsync(dbContext, userManager, cancellationToken);
    }

    /// <summary>Her parçası AYRI AYRI idempotent - "AdminProfile zaten var mı" tek bir toplu
    /// guard'a DAYANMAZ (öyle olsaydı, SysAdmin rolü sonradan eklendiğinde zaten var olan gerçek
    /// admin hesaplarına HİÇBİR ZAMAN uygulanamazdı - bkz. plan "Admin Panel Yeniden Yapılandırma"
    /// Faz 1). Bu yüzden her adım kendi varlığını kontrol edip eksikse tamamlar.</summary>
    private static async Task SeedAdminUserAsync(ApplicationDbContext dbContext, UserManager<IdentityUser> userManager, CancellationToken cancellationToken)
    {
        var identityUser = await userManager.FindByEmailAsync(SeedAdminEmail);
        if (identityUser is null)
        {
            identityUser = new IdentityUser { UserName = SeedAdminEmail, Email = SeedAdminEmail, EmailConfirmed = true };
            var createResult = await userManager.CreateAsync(identityUser, SeedAdminPassword);
            if (!createResult.Succeeded)
                throw new InvalidOperationException($"Başlangıç yönetici kullanıcısı oluşturulamadı: {string.Join(", ", createResult.Errors.Select(e => e.Description))}");
        }

        var eCommercePermission = await dbContext.Permissions.FirstOrDefaultAsync(p => p.Key == "ECommerce.Access", cancellationToken);
        if (eCommercePermission is null)
        {
            eCommercePermission = new Permission("ECommerce.Access", AppModule.ECommerce, "E-Ticaret modülüne erişim");
            dbContext.Permissions.Add(eCommercePermission);
        }

        var accountingPermission = await dbContext.Permissions.FirstOrDefaultAsync(p => p.Key == "Accounting.Access", cancellationToken);
        if (accountingPermission is null)
        {
            accountingPermission = new Permission("Accounting.Access", AppModule.Accounting, "Muhasebe modülüne erişim");
            dbContext.Permissions.Add(accountingPermission);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Role.Permissions - VAR OLAN bir role (FirstOrDefaultAsync ile çekilmiş) Grant() çağırmadan
        // önce koleksiyon yüklenmeli, aksi halde "zaten verilmiş mi" kontrolü sessizce boş görüp
        // HER YENİDEN BAŞLATMADA yinelenen bir RolePermission satırı eklemeye çalışır (bkz. yukarıdaki
        // AdminProfile.Roles notuyla AYNI desen).
        async Task<Role> GetOrCreateRoleAsync(string name)
        {
            var role = await dbContext.AppRoles.FirstOrDefaultAsync(r => r.Name == name, cancellationToken);
            if (role is null)
            {
                role = new Role(name);
                dbContext.AppRoles.Add(role);
            }
            else
            {
                await dbContext.Entry(role).Collection(r => r.Permissions).LoadAsync(cancellationToken);
            }
            return role;
        }

        var adminRole = await GetOrCreateRoleAsync("Yönetici");
        adminRole.Grant(eCommercePermission.Id);
        adminRole.Grant(accountingPermission.Id);

        // SysAdmin: verilmiş izinlerden BAĞIMSIZ olarak her yere erişir (AppUserClaimsPrincipalFactory'de
        // ayrı bir "sysadmin" claim'i ile) - yine de mevcut iki izni de sahip olsun diye grant edilir
        // (tutarlılık için, asıl bypass claim üzerinden çalışır).
        var sysAdminRole = await GetOrCreateRoleAsync("SysAdmin");
        sysAdminRole.Grant(eCommercePermission.Id);
        sysAdminRole.Grant(accountingPermission.Id);

        await dbContext.SaveChangesAsync(cancellationToken);

        var adminProfile = await dbContext.AdminProfiles.FirstOrDefaultAsync(p => p.IdentityUserId == identityUser.Id, cancellationToken);
        if (adminProfile is null)
        {
            adminProfile = new AdminProfile(identityUser.Id, "Sistem Yöneticisi");
            dbContext.AdminProfiles.Add(adminProfile);
        }

        // AdminProfile.Roles - YENİ oluşturulan bir profil için sorun değil (henüz hiç Role'ü yok),
        // ama VAR OLAN (FirstOrDefaultAsync ile çekilmiş) bir profile ikinci bir rol eklerken önce
        // LoadCollectionAsync GEREKİR (bkz. backend/README.md "Önemli mimari not") - aksi halde
        // koleksiyon sessizce eksik görünüp AssignRole'ün yinelenen-kontrol mantığı yanlış çalışır.
        if (dbContext.Entry(adminProfile).State != EntityState.Added)
            await dbContext.Entry(adminProfile).Collection(p => p.Roles).LoadAsync(cancellationToken);

        adminProfile.AssignRole(adminRole.Id);
        adminProfile.AssignRole(sysAdminRole.Id);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
