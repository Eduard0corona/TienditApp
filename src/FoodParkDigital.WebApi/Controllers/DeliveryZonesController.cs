using FoodParkDigital.Application.Appointments.HomeVisits;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Delivery;
using FoodParkDigital.WebApi.Errors;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// The delivery zones of the business (Configuración › Zonas de entrega): every member reads them, owners and admins
/// manage them. A change made over an older revision answers 409 <c>concurrency_conflict</c>.
/// </summary>
[ApiController]
[Route("api/v1/delivery-zones")]
[Authorize]
public class DeliveryZonesController : ControllerBase
{
    private readonly IMediator _mediator;

    public DeliveryZonesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>The zones of the business, by name, with their postal codes.</summary>
    [HttpGet]
    public async Task<ActionResult<List<DeliveryZoneDto>>> GetZones(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetDeliveryZonesQuery(), cancellationToken));

    [HttpPost]
    public async Task<ActionResult<DeliveryZoneDto>> CreateZone([FromBody] SaveDeliveryZoneRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CreateDeliveryZoneCommand(
            request.Name,
            request.Fee,
            request.MinimumOrder,
            request.FreeDeliveryFrom,
            request.PostalCodes,
            request.IsActive), cancellationToken);
        return Answer(result);
    }

    /// <summary>Saves a zone with the revision the person saw.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DeliveryZoneDto>> UpdateZone(
        Guid id,
        [FromBody] SaveDeliveryZoneRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateDeliveryZoneCommand(
            id,
            request.Revision,
            request.Name,
            request.Fee,
            request.MinimumOrder,
            request.FreeDeliveryFrom,
            request.PostalCodes,
            request.IsActive), cancellationToken);
        return Answer(result);
    }

    /// <summary>Deletes a zone with the revision the person saw.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteZone(Guid id, [FromQuery] int revision, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteDeliveryZoneCommand(id, revision), cancellationToken);
        if (result.IsFailure)
            return Refused(result.Error);

        return NoContent();
    }

    /// <summary>The travel time between visits at the customers' addresses (Fase 22E), with the Agenda module.</summary>
    [HttpGet("home-visits")]
    public async Task<ActionResult<HomeVisitSettingsDto>> GetHomeVisitSettings(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetHomeVisitSettingsQuery(), cancellationToken));

    /// <summary>Sets the travel time between visits (owners and admins).</summary>
    [HttpPut("home-visits")]
    public async Task<ActionResult<HomeVisitSettingsDto>> UpdateHomeVisitSettings(
        [FromBody] UpdateHomeVisitSettingsCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    private ActionResult<DeliveryZoneDto> Answer(Result<DeliveryZoneDto> result)
        => result.IsFailure ? Refused(result.Error) : Ok(result.Value);

    private ObjectResult Refused(string? error)
        => error == DeliveryZoneErrors.Stale
            ? this.ApiError(StatusCodes.Status409Conflict, error, ApiErrorCodes.ConcurrencyConflict)
            : this.ApiError(StatusCodes.Status400BadRequest, error);
}

/// <summary>A zone as the form saves it.</summary>
/// <param name="Revision">The revision the person saw; ignored when adding a zone.</param>
/// <param name="PostalCodes">In any order, repeated or with spaces: each is kept once.</param>
public record SaveDeliveryZoneRequest(
    string Name,
    decimal Fee,
    decimal MinimumOrder,
    decimal? FreeDeliveryFrom,
    List<string> PostalCodes,
    bool IsActive = true,
    int Revision = 0);
