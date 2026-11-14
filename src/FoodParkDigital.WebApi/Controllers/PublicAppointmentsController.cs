using FoodParkDigital.Application.OnlineBooking;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// Following and cancelling an appointment booked online with the link of its receipt (Fase 24D, ADR 0025), without an
/// account, and sending the photos of what happens with a visit requested online (Fase 29H, ADR 0062). The link travels in
/// the body, never in the path or the query: request telemetry keeps URLs. Rate limited per IP like the public menu.
/// </summary>
[ApiController]
[Route("api/v1/public/appointments/tracking")]
[AllowAnonymous]
public class PublicAppointmentsController : ControllerBase
{
    /// <summary>The rate limit of the photos of a visit, per IP (Fase 29H).</summary>
    public const string PhotosRateLimitPolicy = "public-upload";
    // A photo up to 10 MB with the link and the form around it.
    private const long PhotoBodyLimit = VisitProblemPhotos.MaxFileBytes + 64 * 1024;

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
    public async Task<ActionResult<CustomerAppointmentDto>> Cancel([FromBody] CancelPublicAppointmentRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CancelPublicAppointmentCommand(request.Token, request.ExpectDepositBack), cancellationToken);
        if (result.IsFailure)
        {
            // A link that is not found; or the deposit no longer does what the page said (Fase 31O), and nothing changed.
            var status = result.Error == CustomerAppointments.NotFoundMessage ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
            return this.ApiError(status, result.Error);
        }
        return Ok(result.Value);
    }

    /// <summary>
    /// A photo of what happens for the visit of the link (Fase 29H): one per request, up to five per visit, while the visit
    /// waits or is agendada and before its time. Answers the appointment with how many photos it has.
    /// </summary>
    [HttpPost("photos")]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting(PhotosRateLimitPolicy)]
    [RequestSizeLimit(PhotoBodyLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = PhotoBodyLimit)]
    public async Task<ActionResult<CustomerAppointmentDto>> AddPhoto([FromForm] PublicAppointmentPhotoRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null || request.File.Length == 0)
            return this.ApiError(StatusCodes.Status400BadRequest, "Elige la foto que quieres mandar.");

        await using var stream = request.File.OpenReadStream();
        var result = await _mediator.Send(new AddVisitProblemPhotoCommand
        {
            Token = request.Token,
            FileStream = stream,
            FileSize = request.File.Length
        }, cancellationToken);
        if (result is null)
            return this.ApiError(StatusCodes.Status404NotFound, CustomerAppointments.NotFoundMessage);
        if (result.IsFailure)
        {
            // Too many photos being read right now: the customer sends it again in a moment.
            var status = result.Error == VisitProblemPhotos.BusyMessage
                ? StatusCodes.Status429TooManyRequests
                : StatusCodes.Status400BadRequest;
            return this.ApiError(status, result.Error);
        }
        return Ok(result.Value);
    }
}

public class PublicAppointmentRequest
{
    /// <summary>The link of the receipt (the fragment of /cita#token).</summary>
    public string Token { get; init; } = null!;
}

public class CancelPublicAppointmentRequest
{
    /// <summary>The link of the receipt (the fragment of /cita#token).</summary>
    public string Token { get; init; } = null!;
    /// <summary>Whether the page said cancelling gives the deposit back (Fase 31O); when that changed, nothing is cancelled.</summary>
    public bool? ExpectDepositBack { get; init; }
}

/// <summary>A photo of what happens for the visit of a link (Fase 29H), as a form: the link and the file.</summary>
public class PublicAppointmentPhotoRequest
{
    /// <summary>The link of the receipt (the fragment of /cita#token).</summary>
    public string Token { get; init; } = null!;
    /// <summary>The photo: JPG, PNG or WebP, up to 10 MB.</summary>
    public IFormFile File { get; init; } = null!;
}
