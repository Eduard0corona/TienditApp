using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// Following an online order with the link of its receipt (Fase 24C, ADR 0023), without an account. The link travels in
/// the body, never in the path or the query: request telemetry keeps URLs. Rate limited per IP like the public menu.
/// </summary>
[ApiController]
[Route("api/v1/public/orders/tracking")]
[AllowAnonymous]
public class PublicOrderTrackingController : ControllerBase
{
    private readonly IMediator _mediator;

    public PublicOrderTrackingController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Where the order is and what it carries; a link unknown or expired is not found, the same for both.</summary>
    [HttpPost]
    [EnableRateLimiting("public-menu")]
    public async Task<ActionResult<PublicOrderTrackingDto>> Track([FromBody] PublicOrderTrackingRequest request, CancellationToken cancellationToken)
    {
        var order = await _mediator.Send(new GetPublicOrderTrackingQuery(request.Token), cancellationToken);
        return order is null ? this.ApiError(StatusCodes.Status404NotFound, OrderTracking.NotFoundMessage) : Ok(order);
    }

    /// <summary>
    /// Cancels the order of the link while the business has not accepted it and nothing was paid (Fase 29R); answers the order
    /// as the link shows it. Rate limited per IP like placing an order.
    /// </summary>
    [HttpPost("cancel")]
    [EnableRateLimiting("public-order")]
    public async Task<ActionResult<PublicOrderTrackingDto>> Cancel([FromBody] PublicOrderTrackingRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CancelPublicOrderCommand(request.Token), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status404NotFound, result.Error);
        return Ok(result.Value);
    }
}

public class PublicOrderTrackingRequest
{
    /// <summary>The link of the receipt (the fragment of /seguimiento#token).</summary>
    public string Token { get; init; } = null!;
}
