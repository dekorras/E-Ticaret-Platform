using Dekorras.Application.Ordering.Commands;
using Dekorras.Domain.Ordering;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dekorras.Api.Controllers;

[ApiController]
[Route("api/v1/orders")]
[Authorize]
public sealed class OrdersController(ISender sender) : ControllerBase
{
    /// <summary>14 durumlu sipariş state machine'i - kaynağı (Web/Mobil/pazaryeri) fark etmeksizin
    /// aynı kısıtlarla çalışır (bkz. plan §6, kabul kriterleri §14).</summary>
    [HttpPost("{orderId:guid}/status")]
    public async Task<IActionResult> TransitionStatus(Guid orderId, [FromBody] TransitionOrderStatusRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new TransitionOrderStatusCommand(orderId, request.NewStatus, request.Note), cancellationToken);
        return NoContent();
    }
}

public sealed record TransitionOrderStatusRequest(OrderStatus NewStatus, string? Note);
