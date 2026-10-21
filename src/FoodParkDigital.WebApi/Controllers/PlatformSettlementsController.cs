using System.Text.Json.Serialization;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.DeliveryPlatforms;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.WebApi.Errors;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// The settlements of the delivery platforms' deposits (Reportes › Liquidaciones): whoever reads the reports sees them and
/// what each platform still owes; owners and admins open, save, close and void them.
/// </summary>
[ApiController]
[Route("api/v1/platform-settlements")]
[Authorize]
public class PlatformSettlementsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlatformSettlementsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// The settlements, the newest period first, of one platform or status if asked, or with a text in their reference or
    /// notes.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PaginatedList<PlatformSettlementDto>>> GetSettlements(
        [FromQuery] OrderChannel? channel,
        [FromQuery] PlatformSettlementStatus? status,
        [FromQuery] string? search,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = GetPlatformSettlementsQuery.DefaultPageSize,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(
            new GetPlatformSettlementsQuery { Channel = channel, Status = status, Search = search, PageNumber = pageNumber, PageSize = pageSize },
            cancellationToken));

    /// <summary>What each platform still owes for its orders not settled yet.</summary>
    [HttpGet("receivable")]
    public async Task<ActionResult<List<PlatformReceivableDto>>> GetReceivables(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetPlatformReceivablesQuery(), cancellationToken));

    /// <summary>
    /// The orders of a platform pending for a new settlement of a period (ending today at the latest), the oldest first:
    /// those registered in it and, in the room left of one settlement, the most recent of before; each says whether it
    /// falls in the period.
    /// </summary>
    /// <param name="channel">Rappi, UberEats or DidiFood, by name or number.</param>
    [HttpGet("pending-orders")]
    public async Task<ActionResult<PendingPlatformOrdersDto>> GetPendingOrders(
        [FromQuery] OrderChannel? channel,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetPendingPlatformOrdersQuery { Channel = channel, From = from, To = to }, cancellationToken));

    /// <summary>
    /// A platform's statement against its orders, by folio, for the open settlement or a new one (Fase 22H): which orders
    /// the settlement takes, what each brings by the statement and by TienditApp, and what no order explains. It saves
    /// nothing; the settlement is saved as always.
    /// </summary>
    [HttpPost("statement-matches")]
    // 5,000 lines with their longest folio and concept, escaped, fit in 8 MB; the server's 30 MB would be a million lines.
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<ActionResult<PlatformStatementMatchDto>> MatchStatement(
        [FromBody] MatchPlatformStatementRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Lines.Any(line => line is null))
            return this.ApiError(StatusCodes.Status400BadRequest, MatchPlatformStatementQueryValidator.MissingLineMessage, ApiErrorCodes.ValidationFailed);

        return Ok(await _mediator.Send(new MatchPlatformStatementQuery
        {
            Channel = request.Channel,
            SettlementId = request.SettlementId,
            PeriodFrom = request.PeriodFrom,
            PeriodTo = request.PeriodTo,
            Lines = request.Lines
                .Select(line => new PlatformStatementLineInput { Row = line.Row, Folio = line.Folio, Amount = line.Amount, Concept = line.Concept })
                .ToList()
        }, cancellationToken));
    }

    /// <summary>A settlement with its orders and adjustments and, while open, the pending orders it could add.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PlatformSettlementDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetPlatformSettlementQuery(id), cancellationToken));

    /// <summary>
    /// Opens a settlement of a platform's deposit with the orders it pays and the adjustments that explain the rest. One
    /// open per platform: another one opened at the same time answers 409 <c>concurrency_conflict</c>, and an order that
    /// another settlement took, 409 <c>conflict</c>.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<PlatformSettlementDetailDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PlatformSettlementDetailDto>> Create(
        [FromBody] CreatePlatformSettlementRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Adjustments.Any(adjustment => adjustment is null))
            return this.ApiError(StatusCodes.Status400BadRequest, PlatformSettlementErrors.EmptyAdjustment, ApiErrorCodes.ValidationFailed);

        var result = await _mediator.Send(new CreatePlatformSettlementCommand(
            request.Channel,
            request.PeriodFrom,
            request.PeriodTo,
            request.DepositedOn,
            request.DepositedAmount,
            request.Reference,
            request.Notes,
            request.OrderIds,
            Adjustments(request.Adjustments)), cancellationToken);
        if (result.IsFailure)
            return Refused(result.Error);

        return CreatedAtAction(nameof(GetById), new { id = result.Value.Settlement.Id }, result.Value);
    }

    /// <summary>
    /// Replaces an open settlement whole (deposit, orders and adjustments) with the revision the person saw; someone
    /// else's save in between answers 409 <c>concurrency_conflict</c>, and an order that another settlement took, 409
    /// <c>conflict</c>.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PlatformSettlementDetailDto>> Update(
        Guid id,
        [FromBody] UpdatePlatformSettlementRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Adjustments.Any(adjustment => adjustment is null))
            return this.ApiError(StatusCodes.Status400BadRequest, PlatformSettlementErrors.EmptyAdjustment, ApiErrorCodes.ValidationFailed);

        var result = await _mediator.Send(new UpdatePlatformSettlementCommand(
            id,
            request.Revision,
            request.PeriodFrom,
            request.PeriodTo,
            request.DepositedOn,
            request.DepositedAmount,
            request.Reference,
            request.Notes,
            request.OrderIds,
            Adjustments(request.Adjustments)), cancellationToken);
        return result.IsFailure ? Refused(result.Error) : Ok(result.Value);
    }

    /// <summary>
    /// Closes a settlement once its difference is 0. If what an order brings changed since the last save, the new figures
    /// are saved and it answers 409 <c>conflict</c> for the person to review them; someone else's save in between, 409
    /// <c>concurrency_conflict</c>.
    /// </summary>
    [HttpPost("{id:guid}/close")]
    public async Task<ActionResult<PlatformSettlementDetailDto>> Close(
        Guid id,
        [FromBody] ClosePlatformSettlementRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ClosePlatformSettlementCommand(id, request.Revision), cancellationToken);
        return result.IsFailure ? Refused(result.Error) : Ok(result.Value);
    }

    /// <summary>Voids an open or closed settlement with a reason: it stays as history and its orders are pending again.</summary>
    [HttpPost("{id:guid}/void")]
    public async Task<ActionResult<PlatformSettlementDetailDto>> Void(
        Guid id,
        [FromBody] VoidPlatformSettlementRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new VoidPlatformSettlementCommand(id, request.Revision, request.Reason), cancellationToken);
        return result.IsFailure ? Refused(result.Error) : Ok(result.Value);
    }

    private ObjectResult Refused(string? error)
    {
        // Someone else's save in between: like any concurrent save, the client reloads.
        if (PlatformSettlementErrors.IsStale(error))
            return this.ApiError(StatusCodes.Status409Conflict, error, ApiErrorCodes.ConcurrencyConflict);
        // The data changed under the request: the reason says what to review.
        if (PlatformSettlementErrors.IsConflict(error))
            return this.ApiError(StatusCodes.Status409Conflict, error, ApiErrorCodes.Conflict);
        return this.ApiError(StatusCodes.Status400BadRequest, error);
    }

    private static List<PlatformSettlementAdjustmentInput> Adjustments(IReadOnlyList<PlatformSettlementAdjustmentRequest> adjustments)
        => adjustments
            .Select(adjustment => new PlatformSettlementAdjustmentInput(adjustment.Kind, adjustment.Description, adjustment.Amount))
            .ToList();
}

/// <summary>
/// A new settlement: the platform, the period it pays, the deposit, the orders chosen and the adjustments. Everything but
/// the reference and the notes is required, so nothing is read as 0 that nobody wrote.
/// </summary>
public record CreatePlatformSettlementRequest(
    [property: JsonRequired] OrderChannel Channel,
    [property: JsonRequired] DateOnly PeriodFrom,
    [property: JsonRequired] DateOnly PeriodTo,
    [property: JsonRequired] DateOnly DepositedOn,
    [property: JsonRequired] decimal DepositedAmount,
    string? Reference,
    string? Notes,
    [property: JsonRequired] IReadOnlyList<Guid> OrderIds,
    [property: JsonRequired] IReadOnlyList<PlatformSettlementAdjustmentRequest> Adjustments);

/// <summary>An open settlement as it should be, whole, with the revision the person saw.</summary>
public record UpdatePlatformSettlementRequest(
    [property: JsonRequired] int Revision,
    [property: JsonRequired] DateOnly PeriodFrom,
    [property: JsonRequired] DateOnly PeriodTo,
    [property: JsonRequired] DateOnly DepositedOn,
    [property: JsonRequired] decimal DepositedAmount,
    string? Reference,
    string? Notes,
    [property: JsonRequired] IReadOnlyList<Guid> OrderIds,
    [property: JsonRequired] IReadOnlyList<PlatformSettlementAdjustmentRequest> Adjustments);

/// <summary>An adjustment: its reason, an optional description («Otro» needs one) and its signed amount.</summary>
public record PlatformSettlementAdjustmentRequest(
    [property: JsonRequired] PlatformSettlementAdjustmentKind Kind,
    string? Description,
    [property: JsonRequired] decimal Amount);

/// <summary>
/// A platform's statement to compare with its orders: the settlement it is for (none for a new one), its period and the
/// lines of the person's file.
/// </summary>
public record MatchPlatformStatementRequest(
    [property: JsonRequired] OrderChannel Channel,
    Guid? SettlementId,
    [property: JsonRequired] DateOnly PeriodFrom,
    [property: JsonRequired] DateOnly PeriodTo,
    [property: JsonRequired] IReadOnlyList<PlatformStatementLineRequest> Lines);

/// <summary>A line of the statement: the line of the file it came in, its folio (none for a fee), its amount and concept.</summary>
public record PlatformStatementLineRequest(
    [property: JsonRequired] int Row,
    string? Folio,
    [property: JsonRequired] decimal Amount,
    string? Concept);

/// <summary>The revision of the settlement the person saw.</summary>
public record ClosePlatformSettlementRequest([property: JsonRequired] int Revision);

/// <summary>The revision of the settlement the person saw and why it is voided.</summary>
public record VoidPlatformSettlementRequest(
    [property: JsonRequired] int Revision,
    [property: JsonRequired] string Reason);
