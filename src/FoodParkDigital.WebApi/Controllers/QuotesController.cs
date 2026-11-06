using System.Text.Json.Serialization;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Quotes;
using FoodParkDigital.Application.Quotes.ReplyNotices;
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

    /// <summary>
    /// Whether the person gets an email each time a customer answers a quote of this business from its link, and where it
    /// arrives (Fase 29O). Only for its team: the platform's admin acting in it gets 404.
    /// </summary>
    [HttpGet("reply-emails")]
    public async Task<ActionResult<QuoteReplyEmailsDto>> GetReplyEmails(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetQuoteReplyEmailsQuery(), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status404NotFound, result.Error);
        return Ok(result.Value);
    }

    /// <summary>Turns those emails on or off for the person, in this business only (Fase 29O).</summary>
    [HttpPut("reply-emails")]
    public async Task<ActionResult<QuoteReplyEmailsDto>> UpdateReplyEmails(
        [FromBody] UpdateQuoteReplyEmailsCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status404NotFound, result.Error);
        return Ok(result.Value);
    }

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

    /// <summary>
    /// The accepted version becomes its service order (Fase 29E), once, at the accepted prices; what was paid for the visit
    /// becomes a payment of the order.
    /// </summary>
    [HttpPost("{id:guid}/order")]
    public async Task<ActionResult<QuoteDto>> ConvertToOrder(
        Guid id, [FromBody] ConvertQuoteToOrderRequest? request, CancellationToken cancellationToken)
        => Answer(await _mediator.Send(new ConvertQuoteToOrderCommand(id, request?.PromisedAt), cancellationToken));

    /// <summary>
    /// Agenda the job of the order the quote became (Fase 29F): an appointment of the team at the place of the visit, charged
    /// in that order. The quote comes back with the appointments of its job.
    /// </summary>
    [HttpPost("{id:guid}/work-appointments")]
    public async Task<ActionResult<QuoteDto>> ScheduleWork(
        Guid id, [FromBody] ScheduleQuoteWorkRequest request, CancellationToken cancellationToken)
        => Answer(await _mediator.Send(new ScheduleQuoteWorkCommand(
            id, request.StartAt, request.EndAt, request.StaffUserId, request.Notes), cancellationToken));

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

public class ConvertQuoteToOrderRequest
{
    /// <summary>When the business promises the job, as in any service order; optional.</summary>
    public DateTimeOffset? PromisedAt { get; init; }
}

public class ScheduleQuoteWorkRequest
{
    public DateTimeOffset StartAt { get; init; }
    public DateTimeOffset EndAt { get; init; }
    /// <summary>Who does it (an active member of the team); optional.</summary>
    public Guid? StaffUserId { get; init; }
    public string? Notes { get; init; }
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

    /// <summary>
    /// The customer accepts or rejects the quote (Fase 29G, ADR 0061) and gets it back as it stands. A link unknown or
    /// expired is not found; a quote the business changed at the same time is a conflict; any other refusal says why.
    /// </summary>
    [HttpPost("reply")]
    [EnableRateLimiting("public-order")]
    public async Task<ActionResult<PublicQuoteDto>> Reply([FromBody] PublicQuoteReplyRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ReplyToQuoteFromLinkCommand(request.Token, request.Accepted, request.Response), cancellationToken);
        if (result is null)
            return this.ApiError(StatusCodes.Status404NotFound, PublicQuoteDto.NotFoundMessage);
        if (result.IsFailure)
            return this.ApiError(
                result.Error == QuoteRules.ChangedMeanwhileMessage ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest,
                result.Error);
        return Ok(result.Value);
    }
}

public class PublicQuoteRequest
{
    /// <summary>The link of the quote (the fragment of /cotizacion#token).</summary>
    public string Token { get; init; } = null!;
}

public class PublicQuoteReplyRequest
{
    /// <summary>The link of the quote (the fragment of /cotizacion#token).</summary>
    public string Token { get; init; } = null!;
    /// <summary>True to accept it, false to reject it. Required: a body without it is refused, never read as a rejection.</summary>
    [JsonRequired]
    public bool Accepted { get; init; }
    /// <summary>What the customer wants to tell the business, up to 500 characters.</summary>
    public string? Response { get; init; }
}
