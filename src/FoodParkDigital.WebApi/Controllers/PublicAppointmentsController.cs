using FoodParkDigital.Application.OnlineBooking;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// Following and cancelling an appointment booked online with the link of its receipt (Fase 24D, ADR 0025), without an
/// account. The link travels in the body, never in the path or the query: request telemetry keeps URLs. Rate limited per IP
/// like the public menu.
/// </summary>
[ApiController]
[Route("api/v1/public/appointments/tracking")]
[AllowAnonymous]
public class PublicAppointmentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PublicAppointmentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>What the appointment is and where it stands; a link unknown or expired is not found, the same for both.</summary>
    [HttpPost]
    [EnableRateLimiting("public-menu")]
    public async Task<ActionResult<CustomerAppointmentDto>> Track([FromBody] PublicAppointmentRequest request, CancellationToken cancellationToken)
    {
        var appointment = await _mediator.Send(new GetPublicAppointmentQuery(request.Token), cancellationToken);
        return appointment is null ? this.ApiError(StatusCodes.Status404NotFound, CustomerAppointments.NotFoundMessage) : Ok(appointment);
    }

    /// <summary>Cancels the appointment of the link before it starts.</summary>
    [HttpPost("cancel")]
    [EnableRateLimiting("public-order")]
    public async Task<ActionResult<CustomerAppointmentDto>> Cancel([FromBody] PublicAppointmentRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CancelPublicAppointmentCommand(request.Token), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status404NotFound, result.Error);
        return Ok(result.Value);
    }
}

public class PublicAppointmentRequest
{
    /// <summary>The link of the receipt (the fragment of /cita#token).</summary>
    public string Token { get; init; } = null!;
}
