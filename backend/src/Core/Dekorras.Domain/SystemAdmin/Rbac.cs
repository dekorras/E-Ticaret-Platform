using Dekorras.Domain.Common;

namespace Dekorras.Domain.SystemAdmin;

public enum AppModule { ECommerce, Accounting } // Modül Seçim Ekranı - §10.1

/// <summary>Admin panel kullanıcısının profili; kimlik doğrulama ASP.NET Core Identity üzerinden yapılır
/// (Persistence katmanında IdentityUserId ile eşleşir), bu profil ise iş kurallarını (rol, son seçilen modül) taşır.</summary>
public class AdminProfile : AuditableEntity
{
    public string IdentityUserId { get; private set; } = default!;
    public string FullName { get; private set; } = default!;
    public AppModule? LastSelectedModule { get; private set; }

    private readonly List<AdminProfileRole> _roles = [];
    public IReadOnlyCollection<AdminProfileRole> Roles => _roles.AsReadOnly();

    private AdminProfile() { }

    public AdminProfile(string identityUserId, string fullName)
    {
        IdentityUserId = identityUserId;
        FullName = fullName;
    }

    public void AssignRole(Guid roleId)
    {
        if (_roles.Any(r => r.RoleId == roleId)) return;
        _roles.Add(new AdminProfileRole(Id, roleId));
    }

    public void RevokeRole(Guid roleId) => _roles.RemoveAll(r => r.RoleId == roleId);

    public void RememberLastModule(AppModule module) => LastSelectedModule = module;
}

public class AdminProfileRole : BaseEntity
{
    public Guid AdminProfileId { get; private set; }
    public Guid RoleId { get; private set; }

    private AdminProfileRole() { }

    public AdminProfileRole(Guid adminProfileId, Guid roleId)
    {
        AdminProfileId = adminProfileId;
        RoleId = roleId;
    }
}

public class Role : AuditableEntity
{
    public string Name { get; private set; } = default!; // Yönetici, Muhasebeci, Editör/Müşteri Hizmetleri...

    private readonly List<RolePermission> _permissions = [];
    public IReadOnlyCollection<RolePermission> Permissions => _permissions.AsReadOnly();

    private Role() { }

    public Role(string name) => Name = name;

    public void Rename(string name) => Name = name;

    public void Grant(Guid permissionId)
    {
        if (_permissions.Any(p => p.PermissionId == permissionId)) return;
        _permissions.Add(new RolePermission(Id, permissionId));
    }

    public void Revoke(Guid permissionId) => _permissions.RemoveAll(p => p.PermissionId == permissionId);
}

public class RolePermission : BaseEntity
{
    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }

    private RolePermission() { }

    public RolePermission(Guid roleId, Guid permissionId)
    {
        RoleId = roleId;
        PermissionId = permissionId;
    }
}

public class Permission : AuditableEntity
{
    public string Key { get; private set; } = default!; // ör. "Accounting.Access", "ECommerce.Orders.Manage"
    public AppModule Module { get; private set; }
    public string Description { get; private set; } = default!;

    private Permission() { }

    public Permission(string key, AppModule module, string description)
    {
        Key = key;
        Module = module;
        Description = description;
    }
}

public class Setting : AuditableEntity
{
    public string Key { get; private set; } = default!;
    public string Value { get; private set; } = default!;

    private Setting() { }

    public Setting(string key, string value) { Key = key; Value = value; }

    public void SetValue(string value) => Value = value;
}

public class AuditLog : BaseEntity
{
    public string? ActorIdentityUserId { get; private set; }
    public string Action { get; private set; } = default!; // ör. "IntegrationProvider.Activated"
    public string? EntityType { get; private set; }
    public string? EntityId { get; private set; }
    public string? DetailsJson { get; private set; }
    public DateTime OccurredAtUtc { get; private set; } = DateTime.UtcNow;

    private AuditLog() { }

    public AuditLog(string? actorIdentityUserId, string action, string? entityType, string? entityId, string? detailsJson)
    {
        ActorIdentityUserId = actorIdentityUserId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        DetailsJson = detailsJson;
    }
}
