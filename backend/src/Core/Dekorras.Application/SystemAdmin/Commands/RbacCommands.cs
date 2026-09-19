using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.SystemAdmin;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.SystemAdmin.Commands;

public sealed record CreateRoleCommand(string Name) : IRequest<Guid>;

public sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
}

public sealed class CreateRoleCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreateRoleCommand, Guid>
{
    public async Task<Guid> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Role>();
        if (repository.Query().Any(r => r.Name == request.Name))
            throw new InvalidOperationException($"'{request.Name}' adlı bir rol zaten var.");

        var role = new Role(request.Name);
        await repository.AddAsync(role, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return role.Id;
    }
}

/// <summary>
/// "SysAdmin" adlı rol özel olarak korunur - hem burada (yeniden adlandırma) hem
/// `DeleteRoleCommand`de (silme). `AppUserClaimsPrincipalFactory` bu rolü TAM OLARAK bu string
/// değeriyle (`r.Name == "SysAdmin"`) eşleştirip taşıyıcısına TÜM izinlerden bağımsız erişim veren
/// bir "sysadmin" claim'i ekler - rol yeniden adlandırılırsa veya silinirse bu bypass mekanizması
/// SESSİZCE devre dışı kalır (son SysAdmin ise, panelin RBAC'ını yönetebilecek KİMSE kalmaz).
/// </summary>
public sealed record RenameRoleCommand(Guid RoleId, string NewName, string ActingIdentityUserId) : IRequest<Unit>;

public sealed class RenameRoleCommandValidator : AbstractValidator<RenameRoleCommand>
{
    public RenameRoleCommandValidator() => RuleFor(x => x.NewName).NotEmpty().MaximumLength(100);
}

public sealed class RenameRoleCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<RenameRoleCommand, Unit>
{
    public async Task<Unit> Handle(RenameRoleCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Role>();
        var role = await repository.GetByIdAsync(request.RoleId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.RoleId}' numaralı rol bulunamadı.");

        if (role.Name == "SysAdmin")
            throw new InvalidOperationException("'SysAdmin' rolü yeniden adlandırılamaz - panelin RBAC'ını yönetme yetkisi bu isme bağlıdır.");

        if (repository.Query().Any(r => r.Id != request.RoleId && r.Name == request.NewName))
            throw new InvalidOperationException($"'{request.NewName}' adlı bir rol zaten var.");

        role.Rename(request.NewName);

        await unitOfWork.Repository<AuditLog>().AddAsync(new AuditLog(
            request.ActingIdentityUserId, "Role.Renamed", nameof(Role), role.Id.ToString(), request.NewName), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>
/// "SysAdmin" silinemez (bkz. RenameRoleCommand'daki AYNI gerekçe). AYRICA: `AdminProfileRole.RoleId`
/// bilinçli olarak (ya da bir gözden kaçma sonucu) `Role`e GERÇEK bir veritabanı FK KISITLAMASI
/// TAŞIMIYOR (bkz. backend/README.md - `sys.foreign_keys` ile doğrulandı, yalnızca AdminProfileId'ye
/// FK var). Yani veritabanının kendisi, hâlâ bir yöneticiye atanmış bir rolün silinmesini
/// ENGELLEMEZ - bu kontrol BURADA, uygulama katmanında yapılmalı, aksi halde silinen role hâlâ
/// atanmış yönetici hesapları "hayalet" bir RoleId ile kalır (ne hata verir ne de düzgün çalışır).
/// </summary>
public sealed record DeleteRoleCommand(Guid RoleId, string ActingIdentityUserId) : IRequest<Unit>;

public sealed class DeleteRoleCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<DeleteRoleCommand, Unit>
{
    public async Task<Unit> Handle(DeleteRoleCommand request, CancellationToken cancellationToken)
    {
        var roleRepository = unitOfWork.Repository<Role>();
        var role = await roleRepository.GetByIdAsync(request.RoleId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.RoleId}' numaralı rol bulunamadı.");

        if (role.Name == "SysAdmin")
            throw new InvalidOperationException("'SysAdmin' rolü silinemez - bu rol, yönetim panelinin kendisini yönetebilme yetkisini taşır.");

        var isAssignedToAnyAdmin = unitOfWork.Repository<AdminProfileRole>().Query().Any(ar => ar.RoleId == request.RoleId);
        if (isAssignedToAnyAdmin)
            throw new InvalidOperationException("Bu rol hâlâ bir veya daha fazla yöneticiye atanmış - silmeden önce 'Yöneticiler' sayfasından bu rolü tüm kullanıcılardan kaldırın.");

        await unitOfWork.Repository<AuditLog>().AddAsync(new AuditLog(
            request.ActingIdentityUserId, "Role.Deleted", nameof(Role), role.Id.ToString(), role.Name), cancellationToken);

        roleRepository.Remove(role);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

public sealed record GrantPermissionCommand(Guid RoleId, Guid PermissionId, string ActingIdentityUserId) : IRequest<Unit>;

public sealed class GrantPermissionCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<GrantPermissionCommand, Unit>
{
    public async Task<Unit> Handle(GrantPermissionCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Role>();
        var role = await repository.GetByIdAsync(request.RoleId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.RoleId}' numaralı rol bulunamadı.");

        await repository.LoadCollectionAsync(role, r => r.Permissions, cancellationToken);
        role.Grant(request.PermissionId);

        await unitOfWork.Repository<AuditLog>().AddAsync(new AuditLog(
            request.ActingIdentityUserId, "Role.PermissionGranted", nameof(Role), role.Id.ToString(), request.PermissionId.ToString()), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record RevokePermissionCommand(Guid RoleId, Guid PermissionId, string ActingIdentityUserId) : IRequest<Unit>;

public sealed class RevokePermissionCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<RevokePermissionCommand, Unit>
{
    public async Task<Unit> Handle(RevokePermissionCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Role>();
        var role = await repository.GetByIdAsync(request.RoleId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.RoleId}' numaralı rol bulunamadı.");

        await repository.LoadCollectionAsync(role, r => r.Permissions, cancellationToken);
        role.Revoke(request.PermissionId);

        await unitOfWork.Repository<AuditLog>().AddAsync(new AuditLog(
            request.ActingIdentityUserId, "Role.PermissionRevoked", nameof(Role), role.Id.ToString(), request.PermissionId.ToString()), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>ASP.NET Identity kullanıcısı (IdentityUser) Presentation katmanında UserManager ile
/// oluşturulduktan SONRA çağrılır - Application katmanı bilinçli olarak IdentityUser'a bağımlı
/// değildir, yalnızca opak bir `identityUserId` string'i taşır (AccountController/Program.cs'teki
/// aynı katman ayrım deseni).</summary>
public sealed record CreateAdminProfileCommand(string IdentityUserId, string FullName, string? ActingIdentityUserId = null) : IRequest<Guid>;

public sealed class CreateAdminProfileCommandValidator : AbstractValidator<CreateAdminProfileCommand>
{
    public CreateAdminProfileCommandValidator()
    {
        RuleFor(x => x.IdentityUserId).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
    }
}

public sealed class CreateAdminProfileCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreateAdminProfileCommand, Guid>
{
    public async Task<Guid> Handle(CreateAdminProfileCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<AdminProfile>();
        if (repository.Query().Any(p => p.IdentityUserId == request.IdentityUserId))
            throw new InvalidOperationException("Bu kullanıcı için zaten bir yönetici profili var.");

        var profile = new AdminProfile(request.IdentityUserId, request.FullName);
        await repository.AddAsync(profile, cancellationToken);

        // Denetim kaydının "aktörü" işlemi YAPAN yönetici olmalı, yeni oluşturulan profilin KENDİSİ
        // değil - bu ikisi kolayca karıştırılabiliyor çünkü ikisi de bu komutun parametresi.
        await unitOfWork.Repository<AuditLog>().AddAsync(new AuditLog(
            request.ActingIdentityUserId, "AdminProfile.Created", nameof(AdminProfile), profile.Id.ToString(), request.IdentityUserId), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return profile.Id;
    }
}

public sealed record AssignRoleToAdminCommand(Guid AdminProfileId, Guid RoleId, string ActingIdentityUserId) : IRequest<Unit>;

public sealed class AssignRoleToAdminCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<AssignRoleToAdminCommand, Unit>
{
    public async Task<Unit> Handle(AssignRoleToAdminCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<AdminProfile>();
        var profile = await repository.GetByIdAsync(request.AdminProfileId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.AdminProfileId}' numaralı yönetici profili bulunamadı.");

        await repository.LoadCollectionAsync(profile, p => p.Roles, cancellationToken);
        profile.AssignRole(request.RoleId);

        await unitOfWork.Repository<AuditLog>().AddAsync(new AuditLog(
            request.ActingIdentityUserId, "AdminProfile.RoleAssigned", nameof(AdminProfile), profile.Id.ToString(), request.RoleId.ToString()), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record RevokeRoleFromAdminCommand(Guid AdminProfileId, Guid RoleId, string ActingIdentityUserId) : IRequest<Unit>;

public sealed class RevokeRoleFromAdminCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<RevokeRoleFromAdminCommand, Unit>
{
    public async Task<Unit> Handle(RevokeRoleFromAdminCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<AdminProfile>();
        var profile = await repository.GetByIdAsync(request.AdminProfileId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.AdminProfileId}' numaralı yönetici profili bulunamadı.");

        await repository.LoadCollectionAsync(profile, p => p.Roles, cancellationToken);
        profile.RevokeRole(request.RoleId);

        await unitOfWork.Repository<AuditLog>().AddAsync(new AuditLog(
            request.ActingIdentityUserId, "AdminProfile.RoleRevoked", nameof(AdminProfile), profile.Id.ToString(), request.RoleId.ToString()), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
