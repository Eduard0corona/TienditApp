using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Quotes;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.WebApi.Errors;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// The quotes of the business (Fase 29C, ADR 0053): drafts the server totals, sent with their validity and link, corrected
/// with new versions, and the customer's answer. Each request checks the permission and the module of the business.
/// </summary>
[ApiController]
[Route("api/v1/quotes")]
[Authorize]
public class QuotesController : ControllerBase
{
    private readonly IMediator _mediator;

    public QuotesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedList<QuoteSummaryDto>>> GetAll(
        [FromQuery] QuoteListFilter filter = QuoteListFilter.Open,
        [FromQuery] string? search = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new ListQuotesQuery(filter, search, pageNumber, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<QuoteDto>> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetQuoteQuery(id), cancellationToken));

    /// <summary>How long the quotes are valid and how the business writes its prices.</summary>
    [HttpGet("settings")]
    public async Task<ActionResult<QuoteSettingsDto>> GetSettings(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetQuoteSettingsQuery(), cancellationToken));

    /// <summary>How many days a quote is valid once sent (owners and admins).</summary>
    [HttpPut("settings")]
    public async Task<ActionResult<QuoteSettingsDto>> UpdateSettings([FromBody] UpdateQuoteSettingsCommand command, CancellationToken cancellationToken)
        => Answer(await _mediator.Send(command, cancellationToken));

    /// <summary>The WhatsApp template the quotes go out with, or TienditApp's text (null): owners and admins.</summary>
    [HttpPut("settings/whatsapp-template")]
    public async Task<ActionResult<QuoteSettingsDto>> SetWhatsAppTemplate(
        [FromBody] SetQuoteWhatsAppTemplateCommand command, CancellationToken cancellationToken)
        => Answer(await _mediator.Send(command, cancellationToken));

    /// <summary>A new draft for a customer, or for the visit of an appointment (which opens its quote when it has one).</summary>
    [HttpPost]
    public async Task<ActionResult<QuoteDto>> Create([FromBody] CreateQuoteCommand command, CancellationToken cancellationToken)
        => Answer(await _mediator.Send(command, cancellationToken));

    /// <summary>Everything a draft says; its totals come back from the server.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<QuoteDto>> UpdateDraft(Guid id, [FromBody] UpdateQuoteDraftRequest request, CancellationToken cancellationToken)
        => Answer(await _mediator.Send(new UpdateQuoteDraftCommand(
            id, request.CustomerId, request.CustomerName, request.CustomerPhone, request.CustomerEmail, request.ValidityDays,
            request.Note, request.Lines), cancellationToken));

    [HttpPost("{id:guid}/send")]
    public async Task<ActionResult<QuoteDto>> Send(Guid id, CancellationToken cancellationToken)
        => Answer(await _mediator.Send(new SendQuoteCommand(id), cancellationToken));

    [HttpPost("{id:guid}/revise")]
    public async Task<ActionResult<QuoteDto>> Revise(Guid id, CancellationToken cancellationToken)
        => Answer(await _mediator.Send(new ReviseQuoteCommand(id), cancellationToken));

    [HttpPost("{id:guid}/discard")]
    public async Task<ActionResult<QuoteDto>> Discard(Guid id, CancellationToken cancellationToken)
        => Answer(await _mediator.Send(new DiscardQuoteCommand(id), cancellationToken));

    /// <summary>The customer accepted or rejected it, and how they said so.</summary>
    [HttpPost("{id:guid}/reply")]
    public async Task<ActionResult<QuoteDto>> Reply(Guid id, [FromBody] RecordQuoteReplyRequest request, CancellationToken cancellationToken)
        => Answer(await _mediator.Send(new RecordQuoteReplyCommand(id, request.Accepted, request.Channel, request.Response), cancellationToken));

    /// <summary>Sends a valid sent version by email from TienditApp (Fase 29D); it goes out from the outbox.</summary>
    [HttpPost("{id:guid}/email")]
    public async Task<ActionResult<QuoteEmailDto>> SendByEmail(Guid id, [FromBody] SendQuoteByEmailRequest request, CancellationToken cancellationToken)
        => Answer(await _mediator.Send(new SendQuoteByEmailCommand(id, request.Email), cancellationToken));

    /// <summary>Sends a valid sent version by WhatsApp from the business's number (Fase 29D); it goes out from the queue.</summary>
    [HttpPost("{id:guid}/whatsapp")]
    public async Task<ActionResult<QuoteWhatsAppDto>> SendByWhatsApp(
        Guid id, [FromBody] SendQuoteByWhatsAppRequest request, CancellationToken cancellationToken)
        => Answer(await _mediator.Send(new SendQuoteByWhatsAppCommand(id, request.Phone), cancellationToken));

    private ActionResult Answer<T>(Result<T> result)
        => result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);
}

public class UpdateQuoteDraftRequest
{
    public Guid? CustomerId { get; init; }
    public string CustomerName { get; init; } = null!;
    public string CustomerPhone { get; init; } = null!;
    public string? CustomerEmail { get; init; }
    public int ValidityDays { get; init; }
    public string? Note { get; init; }
    public List<QuoteLineRequest> Lines { get; init; } = [];
}

public class SendQuoteByEmailRequest
{
    public string Email { get; init; } = null!;
}

public class SendQuoteByWhatsAppRequest
{
    public string Phone { get; init; } = null!;
}

public class RecordQuoteReplyRequest
{
    public bool Accepted { get; init; }
    public QuoteReplyChannel Channel { get; init; }
    public string? Response { get; init; }
}

/// <summary>
/// A quote opened with the link the business sent (Fase 29C, ADR 0053), without an account. The link travels in the body,
/// never in the path or the query: request telemetry keeps URLs. Rate limited per IP like the public menu.
/// </summary>
[ApiController]
[Route("api/v1/public/quotes")]
[AllowAnonymous]
public class PublicQuotesController : ControllerBase
{
    private readonly IMediator _mediator;

    public PublicQuotesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>The quote; a link unknown or expired is not found, the same for both.</summary>
    [HttpPost("view")]
    [EnableRateLimiting("public-menu")]
    public async Task<ActionResult<PublicQuoteDto>> View([FromBody] PublicQuoteRequest request, CancellationToken cancellationToken)
    {
        var quote = await _mediator.Send(new GetPublicQuoteQuery(request.Token), cancellationToken);
        return quote is null ? this.ApiError(StatusCodes.Status404NotFound, PublicQuoteDto.NotFoundMessage) : Ok(quote);
    }
}

public class PublicQuoteRequest
{
    /// <summary>The link of the quote (the fragment of /cotizacion#token).</summary>
    public string Token { get; init; } = null!;
}
