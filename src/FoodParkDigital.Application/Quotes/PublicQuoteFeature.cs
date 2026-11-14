using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.OnlinePayments;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Quotes;

/// <summary>
/// A quote opened with its link (Fase 29C, ADR 0053 point 16), without an account: that version only, until 30 days after its
/// validity. Of the customer, only their name: never their phone, their email nor the visit's address. A link unknown,
/// changed, expired or of a business that is not active answers the same.
/// </summary>
[AllowAnonymousRequest("Una cotización con el enlace imposible de adivinar que el negocio le mandó al cliente, sin más datos del cliente que su nombre.")]
public record GetPublicQuoteQuery(string Token) : IRequest<PublicQuoteDto?>;

public class GetPublicQuoteQueryValidator : AbstractValidator<GetPublicQuoteQuery>
{
    public GetPublicQuoteQueryValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(100);
    }
}

/// <summary>A line as the customer reads it.</summary>
public class PublicQuoteLineDto
{
    public string Description { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
}

public class PublicQuoteDto
{
    public const string NotFoundMessage = "Esta cotización no existe o su enlace ya venció. Pídele al negocio que te la mande otra vez.";

    public string BusinessName { get; set; } = null!;
    /// <summary>Its online menu (/m/{slug}).</summary>
    public string BusinessSlug { get; set; } = null!;
    /// <summary>The business's phone, to answer it.</summary>
    public string? BusinessContactPhone { get; set; }
    public string Number { get; set; } = null!;
    public int Revision { get; set; }
    /// <summary>
    /// Sent (valid), Accepted, Rejected, Expired or Superseded: a link exists only once the quote was sent.
    /// </summary>
    public QuoteStatus Status { get; set; }
    public string CustomerName { get; set; } = null!;
    public DateTimeOffset SentAt { get; set; }
    public DateOnly ValidUntil { get; set; }
    public bool PricesIncludeTax { get; set; }
    public List<PublicQuoteLineDto> Lines { get; set; } = [];
    public decimal SubTotal { get; set; }
    public decimal Tax { get; set; }
    public List<QuoteTaxDto> Taxes { get; set; } = [];
    public decimal Total { get; set; }
    public QuoteVisitCreditDto? VisitCredit { get; set; }
    public decimal ToPay { get; set; }
    public string? Note { get; set; }
    /// <summary>The link of the version that replaced this one, while it opens; null otherwise.</summary>
    public string? NewerToken { get; set; }
    /// <summary>
    /// The customer can accept or reject it here (Fase 29G, ADR 0061): sent, still valid and the business lets them.
    /// </summary>
    public bool CanReply { get; set; }
    /// <summary>When it was accepted or rejected; null while it waits or once it expired or was replaced unanswered.</summary>
    public DateTimeOffset? RepliedAt { get; set; }
    /// <summary>
    /// The business's day it was answered on, as its validity: the customer's device may be in another time zone, and the page
    /// says the same day the business sees.
    /// </summary>
    public DateOnly? RepliedOn { get; set; }
    /// <summary>The customer answered here, from its link; otherwise the business recorded the answer.</summary>
    public bool RepliedFromLink { get; set; }
    /// <summary>What the customer wrote when they answered here; never what the business recorded.</summary>
    public string? Response { get; set; }
    /// <summary>
    /// The deposit to pay to accept it here (Fase 31N, ADR 0094); null when it asks none. Accepting it is paying it: «accept»
    /// alone is refused.
    /// </summary>
    public decimal? DepositAmount { get; set; }
    /// <summary>
    /// The deposit can be paid now: it can be answered here and the business can collect it. Asking one it cannot collect now,
    /// the customer can still reject it, or write to the business.
    /// </summary>
    public bool CanPayDeposit { get; set; }
    /// <summary>The customer paid the deposit, and so accepted it here.</summary>
    public bool DepositPaid { get; set; }
}

public class GetPublicQuoteQueryHandler : IRequestHandler<GetPublicQuoteQuery, PublicQuoteDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;
    private readonly IOnlinePaymentGateway? _gateway;

    /// <param name="gateway">Mercado Pago, to say whether its deposit can be paid (Fase 31N).</param>
    public GetPublicQuoteQueryHandler(
        IApplicationDbContext context, ICurrentTenantService tenantService, IDateTimeProvider clock, IOnlinePaymentGateway? gateway = null)
    {
        _context = context;
        _tenantService = tenantService;
        _clock = clock;
        _gateway = gateway;
    }

    public async Task<PublicQuoteDto?> Handle(GetPublicQuoteQuery request, CancellationToken cancellationToken)
    {
        var found = await PublicQuotes.FindAsync(_context, request.Token, cancellationToken);
        if (found is null)
            return null;

        _tenantService.SetTenant(found.TenantId);
        return await PublicQuotes.DescribeAsync(_context, found, _clock.UtcNow, cancellationToken, _gateway);
    }
}

/// <summary>A quote named by its link, before any business is in context, and how its customer reads it.</summary>
internal static class PublicQuotes
{
    public sealed record Found(Guid QuoteId, Guid TenantId);

    /// <summary>
    /// The quote of the whole platform the link names, of an active business; null for a link unknown, malformed or that
    /// differs only in case. Only its business is read here: the quote itself is read inside that business.
    /// </summary>
    public static async Task<Found?> FindAsync(IApplicationDbContext context, string token, CancellationToken cancellationToken)
    {
        if (!Order.IsTrackingToken(token))
            return null;

        var found = await (
                from quote in context.Quotes.IgnoreQueryFilters().AsNoTracking()
                join tenant in context.Tenants.AsNoTracking() on quote.TenantId equals tenant.Id
                where quote.TrackingToken == token && tenant.Status == TenantStatus.Active
                select new { quote.Id, quote.TenantId, quote.TrackingToken })
            .FirstOrDefaultAsync(cancellationToken);
        // The column compares without case, as the database does; the link is the exact one.
        return found is null || !string.Equals(found.TrackingToken, token, StringComparison.Ordinal)
            ? null
            : new Found(found.Id, found.TenantId);
    }

    /// <summary>The business's time zone and whether it lets its customers answer from the link.</summary>
    public static Task<BusinessView?> BusinessAsync(IApplicationDbContext context, Guid tenantId, CancellationToken cancellationToken)
        => context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            // A business without its settings row (legal, if rare) reads the defaults: its time zone and the answers on.
            .Select(t => new BusinessView(t.Name, t.Slug, t.ContactPhone, t.Settings!.TimeZone, (bool?)t.Settings.QuoteOnlineReplies ?? true))
            .FirstOrDefaultAsync(cancellationToken);

    public sealed record BusinessView(string Name, string Slug, string? ContactPhone, string? TimeZone, bool OnlineReplies);

    /// <summary>
    /// The quote as its customer reads it: that version only, until 30 days after its validity (null after it). Of the customer,
    /// only their name. Without <paramref name="gateway"/> its deposit cannot be paid.
    /// </summary>
    public static async Task<PublicQuoteDto?> DescribeAsync(
        IApplicationDbContext context, Found found, DateTimeOffset now, CancellationToken cancellationToken,
        IOnlinePaymentGateway? gateway = null)
    {
        var view = await context.Quotes
            .AsNoTracking()
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == found.QuoteId && q.TenantId == found.TenantId, cancellationToken);
        var business = await BusinessAsync(context, found.TenantId, cancellationToken);
        if (view is null || business is null)
            return null;

        var today = QuoteCalendar.Today(business.TimeZone, now);
        if (!view.LinkOpensOn(today) || view.SentAt is not { } sentAt || view.ValidUntil is not { } validUntil)
            return null;

        var stillPaid = await QuoteVisitCredits.StillPaidAsync(context, view, cancellationToken);
        var status = view.StatusOn(today);
        string? newer = null;
        if (status == QuoteStatus.Superseded)
        {
            // The latest version sent since, while its link opens.
            var latest = await context.Quotes
                .AsNoTracking()
                .Where(q => q.TenantId == view.TenantId && q.Number == view.Number && q.Revision > view.Revision
                    && q.TrackingToken != null && q.Status != QuoteStatus.Superseded)
                .OrderByDescending(q => q.Revision)
                .Select(q => new { q.TrackingToken, q.ValidUntil })
                .FirstOrDefaultAsync(cancellationToken);
            if (latest?.ValidUntil is { } until && today <= until.AddDays(QuoteRules.LinkDaysAfterValidity))
                newer = latest.TrackingToken;
        }

        var replied = status is QuoteStatus.Accepted or QuoteStatus.Rejected;
        var fromLink = replied && view.ReplyChannel == QuoteReplyChannel.Link;
        var canReply = status == QuoteStatus.Sent && business.OnlineReplies;
        var canPayDeposit = canReply && view.DepositAmount is not null
            && await QuoteDeposits.OffersAsync(context, gateway, view.TenantId, now, cancellationToken);
        return new PublicQuoteDto
        {
            BusinessName = business.Name,
            BusinessSlug = business.Slug,
            BusinessContactPhone = business.ContactPhone,
            Number = view.Number,
            Revision = view.Revision,
            Status = status,
            CustomerName = view.CustomerName,
            SentAt = sentAt,
            ValidUntil = validUntil,
            PricesIncludeTax = view.PricesIncludeTax,
            Lines = view.Lines.OrderBy(line => line.Position).Select(line => new PublicQuoteLineDto
            {
                Description = line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Total = line.Total
            }).ToList(),
            SubTotal = view.SubTotal,
            Tax = view.Tax,
            Taxes = QuoteViews.Taxes(view),
            Total = view.Total,
            VisitCredit = QuoteVisitCredits.Describe(view, stillPaid),
            ToPay = view.ToPay(stillPaid),
            Note = view.Note,
            NewerToken = newer,
            CanReply = canReply,
            RepliedAt = replied ? view.ClosedAt : null,
            RepliedOn = replied && view.ClosedAt is { } closedAt ? QuoteCalendar.Today(business.TimeZone, closedAt) : null,
            RepliedFromLink = fromLink,
            Response = fromLink ? view.CustomerResponse : null,
            DepositAmount = view.DepositAmount,
            CanPayDeposit = canPayDeposit,
            DepositPaid = await QuoteDeposits.PaidAsync(context, view, cancellationToken)
        };
    }
}
