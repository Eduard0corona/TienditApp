using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
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
}

public class GetPublicQuoteQueryHandler : IRequestHandler<GetPublicQuoteQuery, PublicQuoteDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;

    public GetPublicQuoteQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _clock = clock;
    }

    public async Task<PublicQuoteDto?> Handle(GetPublicQuoteQuery request, CancellationToken cancellationToken)
    {
        if (!Order.IsTrackingToken(request.Token))
            return null;

        // The link names one quote of the whole platform before any business is in context: only its business is read here;
        // the quote itself is read inside that business.
        var found = await (
                from quote in _context.Quotes.IgnoreQueryFilters().AsNoTracking()
                join tenant in _context.Tenants.AsNoTracking() on quote.TenantId equals tenant.Id
                where quote.TrackingToken == request.Token && tenant.Status == TenantStatus.Active
                select new { quote.Id, quote.TenantId, quote.TrackingToken })
            .FirstOrDefaultAsync(cancellationToken);
        // The column compares without case, as the database does; the link is the exact one.
        if (found is null || !string.Equals(found.TrackingToken, request.Token, StringComparison.Ordinal))
            return null;

        _tenantService.SetTenant(found.TenantId);
        var view = await _context.Quotes
            .AsNoTracking()
            .Include(q => q.Lines)
            .FirstOrDefaultAsync(q => q.Id == found.Id && q.TenantId == found.TenantId, cancellationToken);
        var business = await _context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == found.TenantId)
            .Select(t => new { t.Name, t.Slug, t.ContactPhone, t.Settings!.TimeZone })
            .FirstOrDefaultAsync(cancellationToken);
        if (view is null || business is null)
            return null;

        var today = QuoteCalendar.Today(business.TimeZone, _clock.UtcNow);
        if (!view.LinkOpensOn(today) || view.SentAt is not { } sentAt || view.ValidUntil is not { } validUntil)
            return null;

        var applies = view.VisitCreditSaleOrderId is { } sale
            && !(await QuoteVisitCredits.CancelledAsync(_context, [sale], cancellationToken)).Contains(sale);
        var status = view.StatusOn(today);
        string? newer = null;
        if (status == QuoteStatus.Superseded)
        {
            // The latest version sent since, while its link opens.
            var latest = await _context.Quotes
                .AsNoTracking()
                .Where(q => q.TenantId == view.TenantId && q.Number == view.Number && q.Revision > view.Revision
                    && q.TrackingToken != null && q.Status != QuoteStatus.Superseded)
                .OrderByDescending(q => q.Revision)
                .Select(q => new { q.TrackingToken, q.ValidUntil })
                .FirstOrDefaultAsync(cancellationToken);
            if (latest?.ValidUntil is { } until && today <= until.AddDays(QuoteRules.LinkDaysAfterValidity))
                newer = latest.TrackingToken;
        }

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
            VisitCredit = view.VisitCreditAmount is { } amount ? new QuoteVisitCreditDto { Amount = amount, Applies = applies } : null,
            ToPay = view.ToPay(applies),
            Note = view.Note,
            NewerToken = newer
        };
    }
}
