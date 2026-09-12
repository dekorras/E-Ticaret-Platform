using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Integrations;

namespace Dekorras.Application.Integrations.Dtos;

public sealed record IntegrationProviderDto(
    Guid? Id,
    string ProviderKey,
    ProviderCategory Category,
    string DisplayName,
    ProviderStatus Status,
    DateTime? LastHealthCheckAtUtc,
    string? LastHealthCheckMessage,
    IReadOnlyCollection<ConfigFieldDefinition> ConfigFields,
    bool IsDefaultForCategory = false);
