using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Orders.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class KitchenController : ControllerBase
{
    private readonly IMediator _mediator;

    public KitchenController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("orders")]
    public async Task<ActionResult<List<KitchenOrderDto>>> GetKitchenOrders(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetKitchenOrdersQuery(), cancellationToken);
        return Ok(result);
    }

    /// <summary>Rounds of open tabs still in the kitchen or waiting to be taken to the table.</summary>
    [HttpGet("tickets")]
    public async Task<ActionResult<List<KitchenTicketDto>>> GetTickets(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetKitchenTicketsQuery(), cancellationToken));

    [HttpPut("tickets/{id:guid}/start")]
    public Task<IActionResult> StartTicket(Guid id, CancellationToken cancellationToken) => MoveTicket(id, KitchenTicketStatus.Preparing, cancellationToken);

    [HttpPut("tickets/{id:guid}/ready")]
    public Task<IActionResult> MarkTicketReady(Guid id, CancellationToken cancellationToken) => MoveTicket(id, KitchenTicketStatus.Ready, cancellationToken);

    [HttpPut("tickets/{id:guid}/served")]
    public Task<IActionResult> MarkTicketServed(Guid id, CancellationToken cancellationToken) => MoveTicket(id, KitchenTicketStatus.Served, cancellationToken);

    private async Task<IActionResult> MoveTicket(Guid id, KitchenTicketStatus status, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateKitchenTicketStatusCommand(id, status), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status409Conflict, result.Error);
        return NoContent();
    }

    [HttpPut("orders/{id:guid}/start")]
    public async Task<IActionResult> StartPreparation(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new MoveKitchenOrderCommand(id, OrderStatus.InPreparation), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status409Conflict, result.Error);
        return NoContent();
    }

    [HttpPut("orders/{id:guid}/ready")]
    public async Task<IActionResult> MarkReady(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new MoveKitchenOrderCommand(id, OrderStatus.Ready), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status409Conflict, result.Error);
        return NoContent();
    }
}
