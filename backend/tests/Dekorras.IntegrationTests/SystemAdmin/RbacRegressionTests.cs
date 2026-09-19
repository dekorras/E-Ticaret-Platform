using Dekorras.Application.SystemAdmin.Commands;
using Dekorras.Application.SystemAdmin.Queries;
using Dekorras.Domain.SystemAdmin;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.SystemAdmin;

/// <summary>Gerçek SQL Server'a karşı çalışır - `Domain.SystemAdmin.Role`/`Permission`/`AdminProfile`
/// Faz 0/1'den beri vardı ve oturum açarken `AppUserClaimsPrincipalFactory` tarafından OKUNUYORDU
/// (RBAC izin kontrolü zaten çalışıyordu) ama roller/izinler/yönetici atamaları hiçbir yerden
/// YAZILAMIYORDU - yalnızca `DbInitializer`'ın tek seferlik SQL seed'i vardı. Bu turda eklenen
/// Admin UI ile artık ikinci bir yönetici kullanıcısı/rolü GERÇEKTEN oluşturulabiliyor.</summary>
public sealed class RbacRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasRbacTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureDeletedAsync();
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task RolOlusturulurIzinVerilirVeGeriAlinir_AyniAdIkinciKezOlusturulamaz()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var permission = new Permission("Accounting.Access", AppModule.Accounting, "Muhasebe modülüne erişim");
        dbContext.Permissions.Add(permission);
        await dbContext.SaveChangesAsync();

        var roleId = await new CreateRoleCommandHandler(unitOfWork).Handle(new CreateRoleCommand("Muhasebeci"), CancellationToken.None);

        var rolesBeforeGrant = await new GetRolesQueryHandler(unitOfWork).Handle(new GetRolesQuery(), CancellationToken.None);
        Assert.Empty(rolesBeforeGrant.Single(r => r.Id == roleId).PermissionIds);

        await new GrantPermissionCommandHandler(unitOfWork).Handle(new GrantPermissionCommand(roleId, permission.Id, "acting-user-1"), CancellationToken.None);

        var rolesAfterGrant = await new GetRolesQueryHandler(unitOfWork).Handle(new GetRolesQuery(), CancellationToken.None);
        Assert.Contains(permission.Id, rolesAfterGrant.Single(r => r.Id == roleId).PermissionIds);

        // İkinci kez aynı izni vermek yinelenen bir satır OLUŞTURMAMALI.
        await new GrantPermissionCommandHandler(unitOfWork).Handle(new GrantPermissionCommand(roleId, permission.Id, "acting-user-1"), CancellationToken.None);
        var rolesAfterSecondGrant = await new GetRolesQueryHandler(unitOfWork).Handle(new GetRolesQuery(), CancellationToken.None);
        Assert.Single(rolesAfterSecondGrant.Single(r => r.Id == roleId).PermissionIds);

        await new RevokePermissionCommandHandler(unitOfWork).Handle(new RevokePermissionCommand(roleId, permission.Id, "acting-user-1"), CancellationToken.None);
        var rolesAfterRevoke = await new GetRolesQueryHandler(unitOfWork).Handle(new GetRolesQuery(), CancellationToken.None);
        Assert.Empty(rolesAfterRevoke.Single(r => r.Id == roleId).PermissionIds);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new CreateRoleCommandHandler(unitOfWork)
            .Handle(new CreateRoleCommand("Muhasebeci"), CancellationToken.None));

        var auditLog = await new GetAuditLogQueryHandler(unitOfWork).Handle(new GetAuditLogQuery(), CancellationToken.None);
        Assert.Contains(auditLog, a => a.Action == "Role.PermissionGranted");
        Assert.Contains(auditLog, a => a.Action == "Role.PermissionRevoked");
    }

    [Fact]
    public async Task YoneticiProfiliOlusturulurRolAtanirVeGeriAlinir()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var roleId = await new CreateRoleCommandHandler(unitOfWork).Handle(new CreateRoleCommand("Editör"), CancellationToken.None);

        var profileId = await new CreateAdminProfileCommandHandler(unitOfWork).Handle(
            new CreateAdminProfileCommand("identity-rbac-1", "Test Yöneticisi", "acting-user-1"), CancellationToken.None);

        var profilesBeforeAssign = await new GetAdminProfilesQueryHandler(unitOfWork).Handle(new GetAdminProfilesQuery(), CancellationToken.None);
        Assert.Empty(profilesBeforeAssign.Single(p => p.Id == profileId).RoleIds);

        await new AssignRoleToAdminCommandHandler(unitOfWork).Handle(new AssignRoleToAdminCommand(profileId, roleId, "acting-user-1"), CancellationToken.None);
        var profilesAfterAssign = await new GetAdminProfilesQueryHandler(unitOfWork).Handle(new GetAdminProfilesQuery(), CancellationToken.None);
        Assert.Contains(roleId, profilesAfterAssign.Single(p => p.Id == profileId).RoleIds);

        // Aynı kullanıcı için İKİNCİ bir profil oluşturulamamalı.
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CreateAdminProfileCommandHandler(unitOfWork)
            .Handle(new CreateAdminProfileCommand("identity-rbac-1", "Başka Ad", "acting-user-1"), CancellationToken.None));

        // Denetim kaydı işlemi YAPAN aktörü taşımalı ("identity-rbac-1" - yeni oluşturulan
        // profilin KENDİSİ - DEĞİL) - bu turda bulunup düzeltilen gerçek bir hatanın regresyonu.
        var auditLogForCreate = await new GetAuditLogQueryHandler(unitOfWork).Handle(new GetAuditLogQuery(), CancellationToken.None);
        var createEntry = auditLogForCreate.Single(a => a.Action == "AdminProfile.Created" && a.EntityId == profileId.ToString());
        Assert.Equal("acting-user-1", createEntry.ActorIdentityUserId);

        await new RevokeRoleFromAdminCommandHandler(unitOfWork).Handle(new RevokeRoleFromAdminCommand(profileId, roleId, "acting-user-1"), CancellationToken.None);
        var profilesAfterRevoke = await new GetAdminProfilesQueryHandler(unitOfWork).Handle(new GetAdminProfilesQuery(), CancellationToken.None);
        Assert.Empty(profilesAfterRevoke.Single(p => p.Id == profileId).RoleIds);

        var auditLog = await new GetAuditLogQueryHandler(unitOfWork).Handle(new GetAuditLogQuery(), CancellationToken.None);
        Assert.Contains(auditLog, a => a.Action == "AdminProfile.Created");
        Assert.Contains(auditLog, a => a.Action == "AdminProfile.RoleAssigned");
        Assert.Contains(auditLog, a => a.Action == "AdminProfile.RoleRevoked");
    }

    /// <summary>Kullanıcının ekran görüntüsüyle bulduğu gerçek eksiklik - Roller sayfasında rol
    /// silme/yeniden adlandırma HİÇ yoktu. Bu test hem YENİ komutların doğru çalıştığını hem de
    /// GERÇEK bir veri bütünlüğü riskini kanıtlar: `AdminProfileRole.RoleId`nin `Role`e veritabanı
    /// düzeyinde bir FK KISITLAMASI YOK (bkz. backend/README.md) - yani veritabanının KENDİSİ, hâlâ
    /// bir yöneticiye atanmış bir rolün silinmesini ENGELLEMEZ; bu kontrolün uygulama katmanında
    /// (DeleteRoleCommandHandler) doğru çalıştığı burada doğrulanır.</summary>
    [Fact]
    public async Task RolYenidenAdlandirilirVeSilinir_SysAdminKorunurVeAtanmisRolSilinemez()
    {
        await using var dbContext = CreateDbContext();
        var unitOfWork = new UnitOfWork(dbContext);

        var sysAdminRoleId = await new CreateRoleCommandHandler(unitOfWork).Handle(new CreateRoleCommand("SysAdmin"), CancellationToken.None);
        var unassignedRoleId = await new CreateRoleCommandHandler(unitOfWork).Handle(new CreateRoleCommand("Geçici Rol"), CancellationToken.None);
        var assignedRoleId = await new CreateRoleCommandHandler(unitOfWork).Handle(new CreateRoleCommand("Muhasebeci"), CancellationToken.None);

        // --- SysAdmin korumaları ---
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RenameRoleCommandHandler(unitOfWork)
            .Handle(new RenameRoleCommand(sysAdminRoleId, "SuperAdmin", "acting-user-1"), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DeleteRoleCommandHandler(unitOfWork)
            .Handle(new DeleteRoleCommand(sysAdminRoleId, "acting-user-1"), CancellationToken.None));

        // --- Yeniden adlandırma: başarılı + çakışan isim reddedilir ---
        await new RenameRoleCommandHandler(unitOfWork).Handle(new RenameRoleCommand(unassignedRoleId, "Kalıcı Rol", "acting-user-1"), CancellationToken.None);
        var rolesAfterRename = await new GetRolesQueryHandler(unitOfWork).Handle(new GetRolesQuery(), CancellationToken.None);
        Assert.Equal("Kalıcı Rol", rolesAfterRename.Single(r => r.Id == unassignedRoleId).Name);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new RenameRoleCommandHandler(unitOfWork)
            .Handle(new RenameRoleCommand(unassignedRoleId, "Muhasebeci", "acting-user-1"), CancellationToken.None));

        // --- Atanmış bir rol silinemez ---
        var profileId = await new CreateAdminProfileCommandHandler(unitOfWork).Handle(
            new CreateAdminProfileCommand("identity-rbac-delete-1", "Test Yöneticisi 2", "acting-user-1"), CancellationToken.None);
        await new AssignRoleToAdminCommandHandler(unitOfWork).Handle(new AssignRoleToAdminCommand(profileId, assignedRoleId, "acting-user-1"), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new DeleteRoleCommandHandler(unitOfWork)
            .Handle(new DeleteRoleCommand(assignedRoleId, "acting-user-1"), CancellationToken.None));

        // Rol kaldırıldıktan SONRA artık silinebilmeli.
        await new RevokeRoleFromAdminCommandHandler(unitOfWork).Handle(new RevokeRoleFromAdminCommand(profileId, assignedRoleId, "acting-user-1"), CancellationToken.None);
        await new DeleteRoleCommandHandler(unitOfWork).Handle(new DeleteRoleCommand(assignedRoleId, "acting-user-1"), CancellationToken.None);

        // --- Atanmamış bir rol doğrudan silinebilir ---
        await new DeleteRoleCommandHandler(unitOfWork).Handle(new DeleteRoleCommand(unassignedRoleId, "acting-user-1"), CancellationToken.None);

        var rolesAfterDeletes = await new GetRolesQueryHandler(unitOfWork).Handle(new GetRolesQuery(), CancellationToken.None);
        Assert.DoesNotContain(rolesAfterDeletes, r => r.Id == assignedRoleId);
        Assert.DoesNotContain(rolesAfterDeletes, r => r.Id == unassignedRoleId);
        Assert.Contains(rolesAfterDeletes, r => r.Id == sysAdminRoleId); // hiç dokunulmadı

        var auditLog = await new GetAuditLogQueryHandler(unitOfWork).Handle(new GetAuditLogQuery(), CancellationToken.None);
        Assert.Contains(auditLog, a => a.Action == "Role.Renamed");
        Assert.Contains(auditLog, a => a.Action == "Role.Deleted");
    }
}
