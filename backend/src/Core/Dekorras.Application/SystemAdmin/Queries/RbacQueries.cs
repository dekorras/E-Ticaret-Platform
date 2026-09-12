using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.SystemAdmin;
using MediatR;

namespace Dekorras.Application.SystemAdmin.Queries;

public sealed record PermissionDto(Guid Id, string Key, AppModule Module, string Description);

public sealed record GetPermissionsQuery : IRequest<IReadOnlyCollection<PermissionDto>>;

public sealed class GetPermissionsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetPermissionsQuery, IReadOnlyCollection<PermissionDto>>
{
    public Task<IReadOnlyCollection<PermissionDto>> Handle(GetPermissionsQuery request, CancellationToken cancellationToken)
    {
        var permissions = unitOfWork.Repository<Permission>().Query()
            .Select(p => new PermissionDto(p.Id, p.Key, p.Module, p.Description))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<PermissionDto>>(permissions);
    }
}

public sealed record RoleDto(Guid Id, string Name, IReadOnlyCollection<Guid> PermissionIds);

public sealed record GetRolesQuery : IRequest<IReadOnlyCollection<RoleDto>>;

public sealed class GetRolesQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetRolesQuery, IReadOnlyCollection<RoleDto>>
{
    public Task<IReadOnlyCollection<RoleDto>> Handle(GetRolesQuery request, CancellationToken cancellationToken)
    {
        var roles = unitOfWork.Repository<Role>().Query()
            .Select(r => new RoleDto(r.Id, r.Name, r.Permissions.Select(p => p.PermissionId).ToList()))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<RoleDto>>(roles);
    }
}

public sealed record AdminProfileDto(Guid Id, string IdentityUserId, string FullName, IReadOnlyCollection<Guid> RoleIds);

public sealed record GetAdminProfilesQuery : IRequest<IReadOnlyCollection<AdminProfileDto>>;

public sealed class GetAdminProfilesQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetAdminProfilesQuery, IReadOnlyCollection<AdminProfileDto>>
{
    public Task<IReadOnlyCollection<AdminProfileDto>> Handle(GetAdminProfilesQuery request, CancellationToken cancellationToken)
    {
        var profiles = unitOfWork.Repository<AdminProfile>().Query()
            .Select(p => new AdminProfileDto(p.Id, p.IdentityUserId, p.FullName, p.Roles.Select(r => r.RoleId).ToList()))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<AdminProfileDto>>(profiles);
    }
}

public sealed record AuditLogEntryDto(Guid Id, string? ActorIdentityUserId, string Action, string? EntityType, string? EntityId, DateTime OccurredAtUtc);

public sealed record GetAuditLogQuery(int Take = 200) : IRequest<IReadOnlyCollection<AuditLogEntryDto>>;

public sealed class GetAuditLogQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetAuditLogQuery, IReadOnlyCollection<AuditLogEntryDto>>
{
    public Task<IReadOnlyCollection<AuditLogEntryDto>> Handle(GetAuditLogQuery request, CancellationToken cancellationToken)
    {
        var entries = unitOfWork.Repository<AuditLog>().Query()
            .OrderByDescending(a => a.OccurredAtUtc)
            .Take(request.Take)
            .Select(a => new AuditLogEntryDto(a.Id, a.ActorIdentityUserId, a.Action, a.EntityType, a.EntityId, a.OccurredAtUtc))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<AuditLogEntryDto>>(entries);
    }
}
