using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Purchasing.Commands;
using FoodParkDigital.Application.Purchasing.DTOs;
using FoodParkDigital.Application.Purchasing.Queries;
using FoodParkDigital.Domain.Purchasing.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class PurchaseOrdersController : ControllerBase
{
    private readonly IMediator _mediator;

    public PurchaseOrdersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedList<PurchaseOrderDto>>> GetAll(
        [FromQuery] Guid? supplierId,
        [FromQuery] PurchaseOrderStatus? status,
        [FromQuery] DateTimeOffset? fromDate,
        [FromQuery] DateTimeOffset? toDate,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new GetPurchaseOrdersQuery
        {
            SupplierId = supplierId,
            Status = status,
            FromDate = fromDate,
            ToDate = toDate,
            PageNumber = pageNumber,
            PageSize = pageSize
        });
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PurchaseOrderDto>> GetById(Guid id)
    {
        var result = await _mediator.Send(new GetPurchaseOrderByIdQuery(id));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<PurchaseOrderDto>> Create([FromBody] CreatePurchaseOrderCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    [HttpPost("{id:guid}/receive")]
    public async Task<ActionResult<PurchaseOrderDto>> Receive(Guid id, [FromBody] ReceivePurchaseOrderRequest request)
    {
        var result = await _mediator.Send(new ReceivePurchaseOrderCommand
        {
            PurchaseOrderId = id,
            ReceivedAt = request.ReceivedAt,
            Lines = request.Lines.Select(x => new ReceivePurchaseOrderLineRequest
            {
                PurchaseOrderLineId = x.PurchaseOrderLineId,
                QuantityReceived = x.QuantityReceived,
                UnitCost = x.UnitCost
            }).ToList()
        });

        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);

        return Ok(result.Value);
    }

    [HttpPost("{id:guid}/send")]
    public async Task<ActionResult<PurchaseOrderDto>> Send(Guid id)
    {
        var result = await _mediator.Send(new SendPurchaseOrderCommand(id));
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<PurchaseOrderDto>> Cancel(Guid id)
    {
        var result = await _mediator.Send(new CancelPurchaseOrderCommand(id));
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }
}

public class ReceivePurchaseOrderRequest
{
    public DateTimeOffset? ReceivedAt { get; init; }
    public List<ReceivePurchaseOrderLineItemRequest> Lines { get; init; } = [];
}

public class ReceivePurchaseOrderLineItemRequest
{
    public Guid PurchaseOrderLineId { get; init; }
    public decimal QuantityReceived { get; init; }
    /// <summary>The supplier's actual price when it differs from the ordered one.</summary>
    public decimal? UnitCost { get; init; }
}
