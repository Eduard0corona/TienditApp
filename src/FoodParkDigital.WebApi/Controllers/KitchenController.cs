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
// Any employee role: TenantResolutionMiddleware requires ManageServiceBoard and the Kitchen capability,
// the same rule that shows the kitchen board in the menu.
[Authorize(Roles = "SUPER_ADMIN,TENANT_ADMIN,STAFF,KITCHEN")]
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

    /// <summary>Rounds of open tabs still in the kitchen or waiting to be taken to the table.</summary>
    [HttpGet("tickets")]
    public async Task<ActionResult<List<KitchenTicketDto>>> GetTickets()
        => Ok(await _mediator.Send(new GetKitchenTicketsQuery()));

    [HttpPut("tickets/{id:guid}/start")]
    public Task<IActionResult> StartTicket(Guid id) => MoveTicket(id, KitchenTicketStatus.Preparing);

    [HttpPut("tickets/{id:guid}/ready")]
    public Task<IActionResult> MarkTicketReady(Guid id) => MoveTicket(id, KitchenTicketStatus.Ready);

    [HttpPut("tickets/{id:guid}/served")]
    public Task<IActionResult> MarkTicketServed(Guid id) => MoveTicket(id, KitchenTicketStatus.Served);

    private async Task<IActionResult> MoveTicket(Guid id, KitchenTicketStatus status)
    {
        var result = await _mediator.Send(new UpdateKitchenTicketStatusCommand(id, status));
        if (result.IsFailure)
            return Conflict(new { error = result.Error });
        return NoContent();
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
            return Conflict(new { error = result.Error });
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
            return Conflict(new { error = result.Error });
        return NoContent();
    }
}
