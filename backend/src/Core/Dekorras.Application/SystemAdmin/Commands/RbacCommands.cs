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
