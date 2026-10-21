using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Inventory.Transfers;
using FoodParkDigital.WebApi.Errors;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>Transfers between the locations of the business (Fase 27B, ADR 0038): what left one and entered the other at once.</summary>
[ApiController]
[Route("api/v1/inventory/transfers")]
[Authorize]
public class InventoryTransfersController : ControllerBase
{
    private readonly IMediator _mediator;

    public InventoryTransfersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>The newest first; <paramref name="inventoryLocationId"/> takes those that left it or entered it.</summary>
    [HttpGet]
    public async Task<ActionResult<PaginatedList<InventoryTransferListItemDto>>> GetTransfers(
        [FromQuery] Guid? inventoryLocationId,
        [FromQuery] string? search,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new GetInventoryTransfersQuery
        {
            InventoryLocationId = inventoryLocationId,
            Search = search,
            PageNumber = pageNumber,
            PageSize = pageSize
        }, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InventoryTransferDto>> GetTransfer(Guid id, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetInventoryTransferQuery(id), cancellationToken));

    /// <summary>The same <c>idempotencyKey</c> again answers the transfer already made, without moving anything.</summary>
    [HttpPost]
    public async Task<ActionResult<InventoryTransferDto>> CreateTransfer(
        [FromBody] CreateInventoryTransferCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return result.Error == CreateInventoryTransferCommandHandler.SoldMeanwhileMessage
                // A sale took the stock while transferring: nothing moved, and the client reads the stock again.
                ? this.ApiError(StatusCodes.Status409Conflict, result.Error, ApiErrorCodes.Conflict)
                : this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return CreatedAtAction(nameof(GetTransfer), new { id = result.Value.Id }, result.Value);
    }
}
