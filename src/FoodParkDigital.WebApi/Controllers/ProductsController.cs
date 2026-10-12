using FoodParkDigital.Application.Catalog.Commands;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Catalog.Queries;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class ProductsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProductsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedList<ProductDto>>> GetAll(
        [FromQuery] Guid? categoryId,
        [FromQuery] bool? isAvailable,
        [FromQuery] ProductCatalogItemType? itemType = null,
        [FromQuery] bool onlyAvailableNow = false,
        [FromQuery] string? search = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] OrderChannel? priceChannel = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetProductsQuery
        {
            CategoryId = categoryId,
            IsAvailable = isAvailable,
            OnlyAvailableNow = onlyAvailableNow,
            ItemType = itemType,
            Search = search,
            PageNumber = pageNumber,
            PageSize = pageSize,
            PriceChannel = priceChannel
        }, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetProductByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create(
        [FromBody] CreateProductCommand command,
        [FromQuery] ProductCatalogItemType? itemType,
        CancellationToken cancellationToken)
    {
        if (!IsItemTypeConsistent(itemType, command.Unit))
        {
            return this.ApiError(
                StatusCodes.Status400BadRequest,
                "itemType no coincide con la unidad enviada. Para servicios use Unit=Hour|Session|Service; para productos use otra unidad.");
        }

        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProductDto>> Update(
        Guid id,
        [FromBody] UpdateProductCommand command,
        [FromQuery] ProductCatalogItemType? itemType,
        CancellationToken cancellationToken)
    {
        if (id != command.Id)
            return this.ApiError(StatusCodes.Status400BadRequest, "El id de ruta no coincide con el cuerpo.");

        if (!IsItemTypeConsistent(itemType, command.Unit))
        {
            return this.ApiError(
                StatusCodes.Status400BadRequest,
                "itemType no coincide con la unidad enviada. Para servicios use Unit=Hour|Session|Service; para productos use otra unidad.");
        }

        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteProductCommand(id), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return NoContent();
    }

    [HttpPatch("{id:guid}/availability")]
    public async Task<IActionResult> ToggleAvailability(Guid id, [FromBody] ToggleAvailabilityRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ToggleProductAvailabilityCommand
        {
            Id = id,
            IsAvailable = request.IsAvailable
        }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return NoContent();
    }

    /// <summary>Replaces the variants and extras of the product.</summary>
    [HttpPut("{id:guid}/option-groups")]
    public async Task<ActionResult<ProductDto>> SetOptionGroups(Guid id, [FromBody] SetProductOptionGroupsRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SetProductOptionGroupsCommand { ProductId = id, Groups = request.Groups }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpPost("{id:guid}/image")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ProductImageUrlsDto>> UploadImage(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UploadProductImageCommand
        {
            ProductId = id,
            FileStream = file.OpenReadStream(),
            FileName = file.FileName,
            ContentType = file.ContentType,
            FileSize = file.Length
        }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    private static bool IsServiceUnit(UnitType? unit) =>
        unit is UnitType.Hour or UnitType.Session or UnitType.Service;

    private static bool IsItemTypeConsistent(ProductCatalogItemType? itemType, UnitType? unit)
    {
        if (!itemType.HasValue)
            return true;

        var isService = IsServiceUnit(unit);
        return itemType.Value == ProductCatalogItemType.Service ? isService : !isService;
    }
}

public record ToggleAvailabilityRequest(bool IsAvailable);

public class SetProductOptionGroupsRequest
{
    public List<ProductOptionGroupInput> Groups { get; init; } = [];
}
