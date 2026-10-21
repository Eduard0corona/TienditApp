using FoodParkDigital.Application.Inventory.Reconciliation;
using FoodParkDigital.WebApi.Errors;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// Whether each stock adds up to its history (Fase 27C, ADR 0039): what does not, why, and recording the gap in the
/// history without moving the stock.
/// </summary>
[ApiController]
[Route("api/v1/inventory/reconciliation")]
[Authorize]
public class InventoryReconciliationController : ControllerBase
{
    private readonly IMediator _mediator;

    public InventoryReconciliationController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>The stocks of the business, or of <paramref name="inventoryLocationId"/>, that do not add up to their history.</summary>
    [HttpGet]
    public async Task<ActionResult<InventoryReconciliationDto>> GetReconciliation(
        [FromQuery] Guid? inventoryLocationId,
        [FromQuery] string? search,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new GetInventoryReconciliationQuery
        {
            InventoryLocationId = inventoryLocationId,
            Search = search,
            PageNumber = pageNumber,
            PageSize = pageSize
        }, cancellationToken));

    /// <summary>Where the history of one stock stops adding up to it.</summary>
    [HttpGet("{supplyItemId:guid}/{inventoryLocationId:guid}")]
    public async Task<ActionResult<InventoryReconciliationDetailDto>> GetReconciliationDetail(
        Guid supplyItemId, Guid inventoryLocationId, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetInventoryReconciliationDetailQuery(supplyItemId, inventoryLocationId), cancellationToken));

    /// <summary>
    /// Records the reviewed differences of a location in its history. An item that already adds up is left as it is, so
    /// sending it again records nothing twice.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<InventoryReconciliationResultDto>> Reconcile(
        [FromBody] ReconcileInventoryLedgerCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return result.Error!.StartsWith(ReconcileInventoryLedgerCommandHandler.ChangedMessagePrefix, StringComparison.Ordinal)
                // What does not add up changed after it was reviewed: the client reads the review again.
                ? this.ApiError(StatusCodes.Status409Conflict, result.Error, ApiErrorCodes.Conflict)
                : this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }
}
