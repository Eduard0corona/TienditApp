using System.Text.Json.Serialization;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Couriers;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.WebApi.Errors;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// The cash a courier collects for the orders to the customer's address (Fase 22D, Caja › Repartidores): whoever handles
/// the cash sees what each courier owes and its settlements; settling also charges the orders, in their own drawer.
/// </summary>
[ApiController]
[Route("api/v1/courier-settlements")]
[Authorize]
public class CourierSettlementsController : ControllerBase
{
    private readonly IMediator _mediator;

    public CourierSettlementsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>The settlements, the newest first, of one courier if asked.</summary>
    [HttpGet]
    public async Task<ActionResult<PaginatedList<CourierSettlementDto>>> GetSettlements(
        [FromQuery] DeliveryCourier? courier,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(
            new GetCourierSettlementsQuery { Courier = courier, PageNumber = pageNumber, PageSize = pageSize }, cancellationToken));

    /// <summary>What each courier owes for the orders it collected and carries on its way.</summary>
    [HttpGet("receivable")]
    public async Task<ActionResult<IReadOnlyList<CourierReceivableDto>>> GetReceivables(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetCourierReceivablesQuery(), cancellationToken));

    /// <summary>The orders a courier owes, the oldest first and as many as one settlement takes, and those on their way.</summary>
    [HttpGet("pending-orders")]
    public async Task<ActionResult<PendingCourierOrdersDto>> GetPendingOrders(
        [FromQuery] DeliveryCourier courier = DeliveryCourier.Paquetenvia,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new GetPendingCourierOrdersQuery(courier), cancellationToken));

    /// <summary>A settlement with the orders it paid and its adjustments.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CourierSettlementDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetCourierSettlementQuery(id), cancellationToken));

    /// <summary>
    /// The courier hands over the cash: each order is paid in the drawer of whoever receives it and the adjustments enter it
    /// as movements. What was handed over must add up to the cent. An order that is no longer owed, or owes other than
    /// when it was read (charged, settled or changed meanwhile), answers 409 <c>conflict</c>; a charge saved at the same
    /// time, 409 <c>concurrency_conflict</c>.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<CourierSettlementDetailDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CourierSettlementDetailDto>> Create(
        [FromBody] CreateCourierSettlementRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Adjustments.Any(adjustment => adjustment is null))
            return this.ApiError(StatusCodes.Status400BadRequest, CourierSettlement.AdjustmentKindMessage, ApiErrorCodes.ValidationFailed);

        var result = await _mediator.Send(new CreateCourierSettlementCommand(
            request.Courier,
            request.Method,
            request.HandedAmount,
            request.OrderIds,
            request.OrdersTotal,
            request.Adjustments
                .Select(adjustment => new CourierSettlementAdjustmentInput(adjustment.Kind, adjustment.Description, adjustment.Amount))
                .ToList(),
            request.Reference,
            request.Notes), cancellationToken);
        if (result.IsFailure)
        {
            if (result.Error == CourierSettlementErrors.Stale)
                return this.ApiError(StatusCodes.Status409Conflict, result.Error, ApiErrorCodes.ConcurrencyConflict);
            if (result.Error == CourierSettlementErrors.NotOwed)
                return this.ApiError(StatusCodes.Status409Conflict, result.Error, ApiErrorCodes.Conflict);
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Value.Settlement.Id }, result.Value);
    }
}

/// <summary>
/// What the courier hands over: how, how much, the orders it pays (with what they owed when they were read) and the
/// adjustments. Everything but the reference and the notes is required, so nothing is read as 0 that nobody wrote.
/// </summary>
public record CreateCourierSettlementRequest(
    [property: JsonRequired] DeliveryCourier Courier,
    [property: JsonRequired] OrderPaymentMethod Method,
    [property: JsonRequired] decimal HandedAmount,
    [property: JsonRequired] IReadOnlyList<Guid> OrderIds,
    [property: JsonRequired] decimal OrdersTotal,
    [property: JsonRequired] IReadOnlyList<CourierSettlementAdjustmentRequest> Adjustments,
    string? Reference,
    string? Notes);

/// <summary>An adjustment: its reason, an optional description («Otro» needs one) and its signed amount.</summary>
public record CourierSettlementAdjustmentRequest(
    [property: JsonRequired] CourierSettlementAdjustmentKind Kind,
    string? Description,
    [property: JsonRequired] decimal Amount);
