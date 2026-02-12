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
[Authorize(Roles = "KITCHEN,TENANT_ADMIN")]
public class KitchenController : ControllerBase
{
    private readonly IMediator _mediator;

    public KitchenController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("orders")]
    public async Task<ActionResult<List<KitchenOrderDto>>> GetKitchenOrders()
    {
        var result = await _mediator.Send(new GetKitchenOrdersQuery());
        return Ok(result);
    }

    [HttpPut("orders/{id:guid}/start")]
    public async Task<IActionResult> StartPreparation(Guid id)
    {
        var result = await _mediator.Send(new UpdateOrderStatusCommand
        {
            OrderId = id,
            NewStatus = OrderStatus.InPreparation
        });
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return NoContent();
    }

    [HttpPut("orders/{id:guid}/ready")]
    public async Task<IActionResult> MarkReady(Guid id)
    {
        var result = await _mediator.Send(new UpdateOrderStatusCommand
        {
            OrderId = id,
            NewStatus = OrderStatus.Ready
        });
        if (result.IsFailure)
            return BadRequest(new { error = result.Error });
        return NoContent();
    }
}
