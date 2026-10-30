using FoodParkDigital.Application.Invoicing;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// The CFDI a business sent by WhatsApp (Fase 28M, ADR 0058), with the link of its message and without an account: what it
/// is, and its XML and PDF while it stands. The link travels in the body, never in the path or the query: request telemetry
/// keeps URLs. Rate limited per IP like the public menu.
/// </summary>
[ApiController]
[Route("api/v1/public/cfdi")]
[AllowAnonymous]
public class PublicCfdiController : ControllerBase
{
    private readonly IMediator _mediator;

    public PublicCfdiController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>The CFDI of the link; a link unknown or expired is not found, the same for both.</summary>
    [HttpPost]
    [EnableRateLimiting("public-menu")]
    public async Task<ActionResult<PublicCfdiDto>> Get([FromBody] PublicCfdiRequest request, CancellationToken cancellationToken)
    {
        var cfdi = await _mediator.Send(new GetPublicCfdiQuery(request.Token), cancellationToken);
        return cfdi is null ? this.ApiError(StatusCodes.Status404NotFound, PublicCfdiLinks.NotFoundMessage) : Ok(cfdi);
    }

    /// <summary>Its XML or its PDF, downloaded; one that no longer stands is not.</summary>
    [HttpPost("files")]
    [EnableRateLimiting("public-menu")]
    public async Task<IActionResult> Download([FromBody] PublicCfdiFileRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPublicCfdiFileQuery(request.Token, request.Kind), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status404NotFound, result.Error);

        // Downloaded, never rendered as a page of this origin nor kept by shared caches.
        Response.Headers.ContentSecurityPolicy = "sandbox";
        Response.Headers.CacheControl = "private, no-store";
        return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
    }
}

public class PublicCfdiRequest
{
    /// <summary>The link of the message (the fragment of /factura#token).</summary>
    public string Token { get; init; } = null!;
}

public class PublicCfdiFileRequest
{
    public string Token { get; init; } = null!;
    public OrderInvoiceFileKind Kind { get; init; }
}
