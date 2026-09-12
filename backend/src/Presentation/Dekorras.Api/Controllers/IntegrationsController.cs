using Dekorras.Application.Integrations.Commands;
using Dekorras.Application.Integrations.Dtos;
using Dekorras.Application.Integrations.Queries;
using Dekorras.Domain.Integrations;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dekorras.Api.Controllers;

/// <summary>
/// Admin panelin "Entegrasyonlar" ekranının backend'i - Provider Registry mimarisinin uç noktaları
/// (bkz. plan §4.1). Ödeme/Kargo/Pazaryeri/e-Fatura sekmeleri hepsi bu tek controller üzerinden çalışır.
/// </summary>
[ApiController]
[Route("api/v1/integrations")]
[Authorize]
public sealed class IntegrationsController(ISender sender) : ControllerBase
{
    [HttpGet("{category}")]
    [ProducesResponseType<IReadOnlyCollection<IntegrationProviderDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<IntegrationProviderDto>>> GetByCategory(ProviderCategory category, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetIntegrationProvidersQuery(category), cancellationToken));

    [HttpGet("{category}/active")]
    [AllowAnonymous] // checkout ve storefront'un "hangi sağlayıcılar aktif" sorusuna anonim erişebilmesi gerekir
    [ProducesResponseType<IReadOnlyCollection<ActiveProviderDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<ActiveProviderDto>>> GetActive(ProviderCategory category, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetActiveProvidersQuery(category), cancellationToken));

    [HttpPost("configure")]
    [ProducesResponseType<ConfigureIntegrationProviderResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ConfigureIntegrationProviderResult>> Configure(
        [FromBody] ConfigureIntegrationProviderRequest request, CancellationToken cancellationToken)
    {
        var actingUserId = User.Identity?.Name;
        var command = new ConfigureIntegrationProviderCommand(request.ProviderKey, request.Category, request.ConfigValues, actingUserId);
        return Ok(await sender.Send(command, cancellationToken));
    }
}

public sealed record ConfigureIntegrationProviderRequest(string ProviderKey, ProviderCategory Category, Dictionary<string, string> ConfigValues);
