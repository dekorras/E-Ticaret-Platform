using Dekorras.Domain.SystemAdmin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dekorras.Persistence.Configurations;

public class AdminProfileConfiguration : IEntityTypeConfiguration<AdminProfile>
{
    public void Configure(EntityTypeBuilder<AdminProfile> builder)
    {
        builder.HasIndex(p => p.IdentityUserId).IsUnique();

        builder.HasMany(p => p.Roles).WithOne().HasForeignKey(r => r.AdminProfileId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(AdminProfile.Roles))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.HasIndex(r => r.Name).IsUnique();

        builder.HasMany(r => r.Permissions).WithOne().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Role.Permissions))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder) => builder.HasIndex(p => p.Key).IsUnique();
}

public class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> builder) => builder.HasIndex(s => s.Key).IsUnique();
}
