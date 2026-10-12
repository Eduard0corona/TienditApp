using System.Text.Json.Serialization;
using FoodParkDigital.Application.DeliveryPlatforms;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.WebApi.Errors;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// The delivery platforms of the business (Configuración › Negocio › Plataformas de reparto). The rates of the contracts
/// are for owners and admins; whoever takes orders or reads the reports gets only which platforms are on.
/// </summary>
[ApiController]
[Route("api/v1/delivery-platforms")]
[Authorize]
public class DeliveryPlatformsController : ControllerBase
{
    private readonly IMediator _mediator;

    public DeliveryPlatformsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>The three platforms (Rappi, Uber Eats, DiDi Food) with their terms and an example of what each deposits.</summary>
    [HttpGet]
    public async Task<ActionResult<DeliveryPlatformsDto>> GetPlatforms(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetDeliveryPlatformsQuery(), cancellationToken));

    /// <summary>The platforms the business set up and whether each one takes orders, without their rates.</summary>
    [HttpGet("channels")]
    public async Task<ActionResult<List<DeliveryPlatformChannelDto>>> GetChannels(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetDeliveryPlatformChannelsQuery(), cancellationToken));

    /// <summary>
    /// Turns a platform on or off and saves its terms, with the revision the person saw (0 the first time); answers the
    /// three platforms. Someone else's save in between answers 409 <c>concurrency_conflict</c>.
    /// </summary>
    /// <param name="channel">Rappi, UberEats or DidiFood, by name or number.</param>
    [HttpPut("{channel}")]
    public async Task<ActionResult<DeliveryPlatformsDto>> SavePlatform(
        OrderChannel channel,
        [FromBody] SaveDeliveryPlatformRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SaveDeliveryPlatformCommand(
            channel,
            request.Revision,
            request.IsActive,
            request.CommissionPercent,
            request.CommissionBase,
            request.CommissionVatPercent,
            request.IsrWithholdingPercent,
            request.VatWithholdingPercent), cancellationToken);
        if (result.IsFailure)
        {
            // Someone saved the platform since it was loaded: like any concurrent save, the client reloads.
            return result.Error == DeliveryPlatformErrors.Stale
                ? this.ApiError(StatusCodes.Status409Conflict, result.Error, ApiErrorCodes.ConcurrencyConflict)
                : this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        }

        return Ok(result.Value);
    }
}

/// <summary>
/// A platform as it should be, with the revision the person saw (0 for one never saved). Every field is required: a
/// missing one is refused (400) instead of being read as 0, so no commission is saved that nobody wrote.
/// </summary>
public record SaveDeliveryPlatformRequest(
    [property: JsonRequired] int Revision,
    [property: JsonRequired] bool IsActive,
    [property: JsonRequired] decimal CommissionPercent,
    [property: JsonRequired] PlatformCommissionBase CommissionBase,
    [property: JsonRequired] decimal CommissionVatPercent,
    [property: JsonRequired] decimal IsrWithholdingPercent,
    [property: JsonRequired] decimal VatWithholdingPercent);
