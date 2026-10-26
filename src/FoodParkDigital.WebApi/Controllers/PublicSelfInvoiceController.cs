using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.WebApi.Errors;
using FoodParkDigital.WebApi.Http;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// Self-invoicing (Fase 28F, ADR 0050): the customer invoices the ticket on the business's page, without an account, with
/// the folio and the code printed on it. Both travel in the body, never in the path or the query: request telemetry keeps
/// URLs. A folio that is not there and a code that is not its own answer the same 404. The requests that read a ticket are
/// rate limited per IP in a bucket of their own.
/// </summary>
[ApiController]
[Route("api/v1/public/self-invoice/{slug}")]
[AllowAnonymous]
public class PublicSelfInvoiceController : ControllerBase
{
    public const string RateLimitPolicy = "self-invoice";

    private readonly IMediator _mediator;

    public PublicSelfInvoiceController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>The business and whether it invoices tickets online now.</summary>
    [HttpGet]
    [EnableRateLimiting(PublicPages.RateLimitPolicy)]
    public async Task<ActionResult<SelfInvoicePageDto>> GetPage(string slug, CancellationToken cancellationToken)
    {
        var page = slug.Length > PublicPages.MaxSlugLength
            ? null
            : await _mediator.Send(new GetSelfInvoicePageQuery(slug), cancellationToken);
        return page is null ? this.ApiError(StatusCodes.Status404NotFound, "No encontramos ese negocio.") : Ok(page);
    }

    /// <summary>The ticket and whether it can be invoiced online.</summary>
    [HttpPost("ticket")]
    [EnableRateLimiting(RateLimitPolicy)]
    public async Task<ActionResult<SelfInvoiceTicketDto>> LookUp(
        string slug, [FromBody] SelfInvoiceTicketRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new LookUpSelfInvoiceTicketQuery(slug, request.Folio, request.Code), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return result.Value is null ? this.ApiError(StatusCodes.Status404NotFound, SelfInvoiceMessages.NotFound) : Ok(result.Value);
    }

    /// <summary>
    /// Invoices the ticket: stamped (its XML and PDF go to the email), still stamping (they follow), or refused by the PAC
    /// with its reason.
    /// </summary>
    [HttpPost("invoice")]
    [EnableRateLimiting(RateLimitPolicy)]
    public async Task<ActionResult<SelfInvoiceResultDto>> Invoice(
        string slug, [FromBody] SelfInvoiceRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SelfInvoiceCommand(slug, request.Folio, request.Code, request.Receiver, request.Email), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return result.Value is null ? this.ApiError(StatusCodes.Status404NotFound, SelfInvoiceMessages.NotFound) : Ok(result.Value);
    }

    /// <summary>Sends the ticket's invoice again, to whoever knows the RFC it was made for.</summary>
    [HttpPost("resend")]
    [EnableRateLimiting(RateLimitPolicy)]
    public async Task<ActionResult<SelfInvoiceResendDto>> Resend(
        string slug, [FromBody] ResendSelfInvoiceRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ResendSelfInvoiceCommand(slug, request.Folio, request.Code, request.Rfc, request.Email), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return result.Value is null ? this.ApiError(StatusCodes.Status404NotFound, SelfInvoiceMessages.NotFound) : Ok(result.Value);
    }
}

public class SelfInvoiceTicketRequest
{
    /// <summary>The folio printed on the ticket («ORD-000123», or its number).</summary>
    public string Folio { get; init; } = string.Empty;
    /// <summary>The code printed on the ticket («ABCDE-12345»).</summary>
    public string Code { get; init; } = string.Empty;
}

public class SelfInvoiceRequest : SelfInvoiceTicketRequest
{
    public CfdiReceiverInput Receiver { get; init; } = new();
    /// <summary>Where the XML and the PDF go.</summary>
    public string Email { get; init; } = string.Empty;
}

public class ResendSelfInvoiceRequest : SelfInvoiceTicketRequest
{
    /// <summary>The RFC the invoice was made for.</summary>
    public string Rfc { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
}
