using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Orders.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF,KITCHEN")]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrdersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedList<OrderDto>>> GetAll(
        [FromQuery] OrderStatus? status,
        [FromQuery] DateTimeOffset? fromDate,
        [FromQuery] DateTimeOffset? toDate,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new GetOrdersByBusinessQuery
        {
            StatusFilter = status,
            FromDate = fromDate,
            ToDate = toDate,
            PageNumber = pageNumber,
            PageSize = pageSize
        });
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> GetById(Guid id)
    {
        var result = await _mediator.Send(new GetOrderByIdQuery(id));
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
    public async Task<ActionResult<OrderDto>> Create([FromBody] CreateOrderCommand command)
    {
        var result = await _mediator.Send(command);
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateOrderStatusRequest request)
    {
        var result = await _mediator.Send(new UpdateOrderStatusCommand
        {
            OrderId = id,
            NewStatus = request.NewStatus
        });
        if (result.IsFailure)
        {
            if (result.Error!.Contains("otro proceso"))
                return Conflict(new { error = result.Error });
            return BadRequest(new { error = result.Error });
        }
        return NoContent();
    }

    [HttpPut("{id:guid}/cancel")]
    [Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelOrderRequest? request)
    {
        var result = await _mediator.Send(new CancelOrderCommand
        {
            OrderId = id,
            Reason = request?.Reason
        });
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return NoContent();
    }
}

public record UpdateOrderStatusRequest
{
    public OrderStatus NewStatus { get; init; }
}

public record CancelOrderRequest
{
    public string? Reason { get; init; }
}
