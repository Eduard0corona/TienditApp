using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Inventory.Commands;
using FoodParkDigital.Application.Inventory.DTOs;
using FoodParkDigital.Application.Inventory.Queries;
using FoodParkDigital.Domain.Inventory.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class InventoryController : ControllerBase
{
    private readonly IMediator _mediator;

    public InventoryController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("supplies")]
    public async Task<ActionResult<PaginatedList<SupplyItemDto>>> GetSupplies(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] bool? lowStockOnly,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new GetSupplyItemsQuery
        {
            Search = search,
            IsActive = isActive,
            LowStockOnly = lowStockOnly,
            PageNumber = pageNumber,
            PageSize = pageSize
        });

        return Ok(result);
    }

    [HttpGet("supplies/{id:guid}")]
    public async Task<ActionResult<SupplyItemDto>> GetSupplyById(Guid id)
    {
        var result = await _mediator.Send(new GetSupplyItemByIdQuery(id));
        return Ok(result);
    }

    [HttpPost("supplies")]
    public async Task<ActionResult<SupplyItemDto>> CreateSupply([FromBody] CreateSupplyItemCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return CreatedAtAction(nameof(GetSupplyById), new { id = result.Value.Id }, result.Value);
    }

    [HttpPut("supplies/{id:guid}")]
    public async Task<ActionResult<SupplyItemDto>> UpdateSupply(Guid id, [FromBody] UpdateSupplyItemCommand command)
    {
        if (id != command.Id)
            return this.ApiError(StatusCodes.Status400BadRequest, "El id de ruta no coincide con el cuerpo.");

        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }

    [HttpPatch("supplies/{id:guid}/active")]
    public async Task<IActionResult> SetSupplyActive(Guid id, [FromBody] SetEntityActiveRequest request)
    {
        var result = await _mediator.Send(new SetSupplyItemActiveCommand
        {
            Id = id,
            IsActive = request.IsActive
        });

        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return NoContent();
    }

    [HttpGet("locations")]
    public async Task<ActionResult<List<InventoryLocationDto>>> GetLocations([FromQuery] bool? isActive)
    {
        var result = await _mediator.Send(new GetInventoryLocationsQuery { IsActive = isActive });
        return Ok(result);
    }

    [HttpPost("locations")]
    public async Task<ActionResult<InventoryLocationDto>> CreateLocation([FromBody] CreateInventoryLocationCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }

    [HttpPut("locations/{id:guid}")]
    public async Task<ActionResult<InventoryLocationDto>> UpdateLocation(Guid id, [FromBody] UpdateInventoryLocationCommand command)
    {
        if (id != command.Id)
            return this.ApiError(StatusCodes.Status400BadRequest, "El id de ruta no coincide con el cuerpo.");

        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }

    [HttpPatch("locations/{id:guid}/active")]
    public async Task<IActionResult> SetLocationActive(Guid id, [FromBody] SetEntityActiveRequest request)
    {
        var result = await _mediator.Send(new SetInventoryLocationActiveCommand
        {
            Id = id,
            IsActive = request.IsActive
        });

        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return NoContent();
    }

    [HttpGet("stocks")]
    public async Task<ActionResult<PaginatedList<InventoryStockDto>>> GetStocks(
        [FromQuery] string? search,
        [FromQuery] Guid? inventoryLocationId,
        [FromQuery] bool? lowStockOnly,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new GetInventoryStocksQuery
        {
            Search = search,
            InventoryLocationId = inventoryLocationId,
            LowStockOnly = lowStockOnly,
            PageNumber = pageNumber,
            PageSize = pageSize
        });

        return Ok(result);
    }

    [HttpGet("movements")]
    public async Task<ActionResult<PaginatedList<InventoryMovementDto>>> GetMovements(
        [FromQuery] InventoryMovementType? movementType,
        [FromQuery] InventoryMovementReason? movementReason,
        [FromQuery] DateTimeOffset? fromDate,
        [FromQuery] DateTimeOffset? toDate,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new GetInventoryMovementsQuery
        {
            MovementType = movementType,
            MovementReason = movementReason,
            FromDate = fromDate,
            ToDate = toDate,
            PageNumber = pageNumber,
            PageSize = pageSize
        });

        return Ok(result);
    }

    [HttpGet("kardex")]
    public async Task<ActionResult<PaginatedList<InventoryKardexEntryDto>>> GetKardex(
        [FromQuery] Guid? supplyItemId,
        [FromQuery] Guid? inventoryLocationId,
        [FromQuery] DateTimeOffset? fromDate,
        [FromQuery] DateTimeOffset? toDate,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _mediator.Send(new GetInventoryKardexQuery
        {
            SupplyItemId = supplyItemId,
            InventoryLocationId = inventoryLocationId,
            FromDate = fromDate,
            ToDate = toDate,
            PageNumber = pageNumber,
            PageSize = pageSize
        });

        return Ok(result);
    }

    [HttpGet("alerts")]
    public async Task<ActionResult<InventoryAlertsDto>> GetAlerts()
    {
        var result = await _mediator.Send(new GetInventoryAlertsQuery());
        return Ok(result);
    }

    [HttpGet("recipes/products/{productId:guid}")]
    public async Task<ActionResult<ProductRecipeDto>> GetProductRecipe(Guid productId)
    {
        var result = await _mediator.Send(new GetProductRecipeQuery(productId));
        return Ok(result);
    }

    [HttpPut("recipes/products/{productId:guid}")]
    public async Task<ActionResult<ProductRecipeDto>> UpsertProductRecipe(
        Guid productId,
        [FromBody] UpsertProductRecipeRequest request)
    {
        var result = await _mediator.Send(new UpsertProductRecipeCommand
        {
            ProductId = productId,
            Lines = request.Lines.Select(line => new UpsertProductRecipeLineRequest
            {
                RequirementId = line.RequirementId,
                SupplyItemId = line.SupplyItemId,
                InventoryLocationId = line.InventoryLocationId,
                Quantity = line.Quantity,
                WastePercent = line.WastePercent,
                Notes = line.Notes
            }).ToList()
        });

        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }

    /// <summary>The stock of a product by location, when it controls it.</summary>
    [HttpGet("products/{productId:guid}/stock")]
    public async Task<ActionResult<ProductStockDto>> GetProductStock(Guid productId)
        => Ok(await _mediator.Send(new GetProductStockQuery(productId)));

    /// <summary>The product starts controlling its stock, optionally with an opening balance.</summary>
    [HttpPost("products/{productId:guid}/stock")]
    public async Task<ActionResult<ProductStockDto>> TrackProductStock(Guid productId, [FromBody] TrackProductStockRequest request)
    {
        var result = await _mediator.Send(new TrackProductStockCommand
        {
            ProductId = productId,
            SupplyItemId = request.SupplyItemId,
            InitialQuantity = request.InitialQuantity,
            InventoryLocationId = request.InventoryLocationId,
            UnitCost = request.UnitCost,
            ReorderPoint = request.ReorderPoint
        });
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }

    /// <summary>The product stops controlling its stock; its item stays, with its history, as a supply.</summary>
    [HttpDelete("products/{productId:guid}/stock")]
    public async Task<ActionResult<ProductStockDto>> StopTrackingProductStock(Guid productId)
    {
        var result = await _mediator.Send(new StopTrackingProductStockCommand(productId));
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }

    [HttpGet("movements/{id:guid}")]
    public async Task<ActionResult<InventoryMovementDto>> GetMovementById(Guid id)
    {
        var result = await _mediator.Send(new GetInventoryMovementByIdQuery(id));
        return Ok(result);
    }

    [HttpPost("movements")]
    public async Task<ActionResult<InventoryMovementDto>> CreateMovement([FromBody] CreateInventoryMovementCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return CreatedAtAction(nameof(GetMovementById), new { id = result.Value.Id }, result.Value);
    }
}

public record SetEntityActiveRequest(bool IsActive);

public class TrackProductStockRequest
{
    /// <summary>An item the business already keeps for the product, instead of a new one.</summary>
    public Guid? SupplyItemId { get; init; }
    public decimal InitialQuantity { get; init; }
    public Guid? InventoryLocationId { get; init; }
    public decimal? UnitCost { get; init; }
    public decimal ReorderPoint { get; init; }
}

public class UpsertProductRecipeRequest
{
    public List<UpsertProductRecipeLineRequestDto> Lines { get; init; } = [];
}

public class UpsertProductRecipeLineRequestDto
{
    public Guid? RequirementId { get; init; }
    public Guid SupplyItemId { get; init; }
    public Guid? InventoryLocationId { get; init; }
    public decimal Quantity { get; init; }
    public decimal WastePercent { get; init; }
    public string? Notes { get; init; }
}
