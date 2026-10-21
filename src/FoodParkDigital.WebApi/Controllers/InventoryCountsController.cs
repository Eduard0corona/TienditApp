using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Inventory.Counts;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.WebApi.Errors;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>Physical counts of a location (Fase 27A, ADR 0037): start, count, add items, post or cancel.</summary>
[ApiController]
[Route("api/v1/inventory/counts")]
[Authorize]
public class InventoryCountsController : ControllerBase
{
    private readonly IMediator _mediator;

    public InventoryCountsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedList<InventoryCountListItemDto>>> GetCounts(
        [FromQuery] InventoryCountStatus? status,
        [FromQuery] Guid? inventoryLocationId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new GetInventoryCountsQuery
        {
            Status = status,
            InventoryLocationId = inventoryLocationId,
            PageNumber = pageNumber,
            PageSize = pageSize
        }, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InventoryCountDto>> GetCount(Guid id, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetInventoryCountQuery(id), cancellationToken));

    /// <summary>The items of the count; <paramref name="search"/> takes a name, code, SKU or barcode.</summary>
    [HttpGet("{id:guid}/lines")]
    public async Task<ActionResult<PaginatedList<InventoryCountLineDto>>> GetLines(
        Guid id,
        [FromQuery] string? search,
        [FromQuery] bool? pending,
        [FromQuery] bool? withDifference,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new GetInventoryCountLinesQuery
        {
            CountId = id,
            Search = search,
            Pending = pending,
            WithDifference = withDifference,
            PageNumber = pageNumber,
            PageSize = pageSize
        }, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<InventoryCountDto>> StartCount([FromBody] StartInventoryCountCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return CreatedAtAction(nameof(GetCount), new { id = result.Value.Id }, result.Value);
    }

    /// <summary>What was counted of these items, each with what the system had of it right now.</summary>
    [HttpPut("{id:guid}/lines")]
    public async Task<ActionResult<RecordedInventoryCountDto>> RecordLines(
        Guid id, [FromBody] RecordInventoryCountRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RecordInventoryCountCommand { CountId = id, Lines = request.Lines }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }

    [HttpPost("{id:guid}/items")]
    public async Task<ActionResult<InventoryCountDto>> AddItems(
        Guid id, [FromBody] AddInventoryCountItemsRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new AddInventoryCountItemsCommand { CountId = id, SupplyItemIds = request.SupplyItemIds }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }

    [HttpPost("{id:guid}/post")]
    public async Task<ActionResult<InventoryCountDto>> PostCount(
        Guid id, [FromBody] PostInventoryCountRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new PostInventoryCountCommand { CountId = id, ExpectedRevision = request.ExpectedRevision }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<InventoryCountDto>> CancelCount(
        Guid id, [FromBody] CancelInventoryCountRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CancelInventoryCountCommand { CountId = id, Reason = request.Reason }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }
}

public class RecordInventoryCountRequest
{
    public List<InventoryCountEntry> Lines { get; init; } = [];
}

public class AddInventoryCountItemsRequest
{
    public List<Guid> SupplyItemIds { get; init; } = [];
}

public class PostInventoryCountRequest
{
    /// <summary>The revision of the count that was reviewed (<see cref="InventoryCountDto.Revision"/>).</summary>
    public int ExpectedRevision { get; init; }
}

public class CancelInventoryCountRequest
{
    public string? Reason { get; init; }
}
