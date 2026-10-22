using System.Text.Json.Serialization;
using FoodParkDigital.Application.DeliveryPlatforms;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.WebApi.Errors;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// The withholdings of the delivery platforms (Reportes › Liquidaciones › Retenciones, Fase 22J): whoever reads the reports
/// sees, month by month, what TienditApp calculated against what each CFDI de retenciones says; owners and admins upload
/// and remove the CFDI.
/// </summary>
[ApiController]
[Route("api/v1/platform-withholdings")]
[Authorize]
public class PlatformWithholdingsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlatformWithholdingsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>A platform's withholdings in a year, January to December, and its CFDI de retenciones of the year.</summary>
    /// <param name="channel">Rappi, UberEats or DidiFood, by name or number.</param>
    [HttpGet]
    public async Task<ActionResult<PlatformWithholdingsDto>> Get(
        [FromQuery] OrderChannel channel,
        [FromQuery] int year,
        CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetPlatformWithholdingsQuery { Channel = channel, Year = year }, cancellationToken));

    /// <summary>
    /// Registers the XML of a CFDI de retenciones 2.0 the platform issued to the business: what it says is kept, never the
    /// file. The same CFDI in force in the business answers 409 <c>conflict</c> saying where it is.
    /// </summary>
    [HttpPost]
    // 512 KB of XML, escaped in JSON, fit in 2 MB.
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<ActionResult<PlatformWithholdingCertificateDto>> Register(
        [FromBody] RegisterPlatformWithholdingCertificateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RegisterPlatformWithholdingCertificateCommand(request.Channel, request.Xml), cancellationToken);
        return result.IsFailure ? Refused(result.Error) : Ok(result.Value);
    }

    /// <summary>
    /// Takes a CFDI out of the reconciliation with a reason; it stays as history. Someone else's removal in between answers
    /// 409 <c>concurrency_conflict</c>.
    /// </summary>
    [HttpPost("{id:guid}/remove")]
    public async Task<ActionResult<PlatformWithholdingCertificateDto>> Remove(
        Guid id,
        [FromBody] RemovePlatformWithholdingCertificateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RemovePlatformWithholdingCertificateCommand(id, request.Reason), cancellationToken);
        return result.IsFailure ? Refused(result.Error) : Ok(result.Value);
    }

    private ObjectResult Refused(string? error)
    {
        if (PlatformWithholdingErrors.IsStale(error))
            return this.ApiError(StatusCodes.Status409Conflict, error, ApiErrorCodes.ConcurrencyConflict);
        if (PlatformWithholdingErrors.IsConflict(error))
            return this.ApiError(StatusCodes.Status409Conflict, error, ApiErrorCodes.Conflict);
        return this.ApiError(StatusCodes.Status400BadRequest, error);
    }
}

/// <summary>The platform that issued the CFDI, as the person says, and the text of its XML.</summary>
public record RegisterPlatformWithholdingCertificateRequest(
    [property: JsonRequired] OrderChannel Channel,
    [property: JsonRequired] string Xml);

/// <summary>Why the CFDI is removed.</summary>
public record RemovePlatformWithholdingCertificateRequest([property: JsonRequired] string Reason);
