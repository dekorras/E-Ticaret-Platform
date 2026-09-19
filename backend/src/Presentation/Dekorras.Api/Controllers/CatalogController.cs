using Dekorras.Application.Catalog.Commands;
using Dekorras.Application.Catalog.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dekorras.Api.Controllers;

[ApiController]
[Route("api/v1/catalog")]
public sealed class CatalogController(ISender sender) : ControllerBase
{
    [HttpGet("products")]
    [AllowAnonymous]
    [ProducesResponseType<Dekorras.Application.Catalog.Storefront.PagedResult<ProductListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<Dekorras.Application.Catalog.Storefront.PagedResult<ProductListItemDto>>> GetProducts(
        [FromQuery] string languageCode = "tr", [FromQuery] Guid? categoryId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(new GetProductsQuery(languageCode, categoryId, page, pageSize), cancellationToken));

    [HttpPost("products")]
    [Authorize]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<ActionResult<Guid>> CreateProduct([FromBody] CreateProductCommand command, CancellationToken cancellationToken)
    {
        var id = await sender.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetProducts), new { }, id);
    }

    [HttpPost("categories")]
    [Authorize]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<ActionResult<Guid>> CreateCategory([FromBody] CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        var id = await sender.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetProducts), new { }, id);
    }
}
