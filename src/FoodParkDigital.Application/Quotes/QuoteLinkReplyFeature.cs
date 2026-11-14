using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.OnlinePayments;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Quotes;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Quotes;

/// <summary>
/// The customer accepts or rejects the quote from its link (Fase 29G, ADR 0061), without an account: the version the link
/// names, while it is sent and valid on the business's day and the business lets its customers answer there, with what they
/// want to tell it. Once: the answer it already has changes nothing, another one is refused. A link unknown, changed, expired
/// or of a business that is not active is not found (null), as when it is opened.
/// </summary>
[AllowAnonymousRequest("La respuesta del cliente a la cotización con el enlace imposible de adivinar que el negocio le mandó: acepta o rechaza esa versión, sin más datos que su mensaje.")]
public record ReplyToQuoteFromLinkCommand(string Token, bool Accepted, string? Response) : IRequest<Result<PublicQuoteDto>?>;

public class ReplyToQuoteFromLinkCommandValidator : AbstractValidator<ReplyToQuoteFromLinkCommand>
{
    public ReplyToQuoteFromLinkCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Response).Must(r => r is null || r.Trim().Length <= QuoteRules.MaxResponseLength)
            .WithMessage(QuoteRules.CustomerResponseTooLongMessage);
    }
}

public class ReplyToQuoteFromLinkCommandHandler : IRequestHandler<ReplyToQuoteFromLinkCommand, Result<PublicQuoteDto>?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;
    private readonly IOnlinePaymentGateway? _gateway;

    public ReplyToQuoteFromLinkCommandHandler(
        IApplicationDbContext context, ICurrentTenantService tenantService, IDateTimeProvider clock, IOnlinePaymentGateway? gateway = null)
    {
        _context = context;
        _tenantService = tenantService;
        _clock = clock;
        _gateway = gateway;
    }

    public async Task<Result<PublicQuoteDto>?> Handle(ReplyToQuoteFromLinkCommand request, CancellationToken cancellationToken)
    {
        var found = await PublicQuotes.FindAsync(_context, request.Token, cancellationToken);
        if (found is null)
            return null;

        _tenantService.SetTenant(found.TenantId);
        var now = _clock.UtcNow;
        var business = await PublicQuotes.BusinessAsync(_context, found.TenantId, cancellationToken);
        var quote = await _context.Quotes
            .FirstOrDefaultAsync(q => q.Id == found.QuoteId && q.TenantId == found.TenantId, cancellationToken);
        if (business is null || quote is null)
            return null;
        var today = QuoteCalendar.Today(business.TimeZone, now);
        // Past the days its link opens, it is not found, as when it is opened.
        if (!quote.LinkOpensOn(today))
            return null;

        bool changed;
        try
        {
            // The business turned the answers off: a version still waiting is not answered here (a replaced or expired one says
            // what it is, and the answer it already has is still the answer).
            if (!business.OnlineReplies && quote.StatusOn(today) == QuoteStatus.Sent)
                return Result.Failure<PublicQuoteDto>(QuoteRules.OnlineRepliesOffMessage);
            changed = quote.ReplyFromLink(request.Accepted, today, request.Response, now);
        }
        catch (DomainException ex)
        {
            return Result.Failure<PublicQuoteDto>(ex.Message);
        }

        if (changed)
        {
            // What the customer wrote stays on the quote; the activity keeps that they answered here, never who they are.
            _context.AuditLogs.Add(QuoteAudit.Create(quote, request.Accepted ? QuoteAudit.Accepted : QuoteAudit.Rejected,
                QuoteAudit.CustomerFromLink, new { Channel = QuoteReplyChannel.Link.ToString() }));
            if (request.Accepted)
                await QuoteCorrections.DiscardPendingAsync(_context, quote, now, QuoteAudit.CustomerFromLink, cancellationToken);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Saved first by a double tap, another tab or the business: if it is this same answer, it is the answer; anything
                // else (another answer, a correction) wins, and nothing of this one stays.
                var saved = await _context.Quotes
                    .AsNoTracking()
                    .Where(q => q.Id == found.QuoteId && q.TenantId == found.TenantId)
                    .Select(q => q.Status)
                    .FirstOrDefaultAsync(cancellationToken);
                if (saved != (request.Accepted ? QuoteStatus.Accepted : QuoteStatus.Rejected))
                    return Result.Failure<PublicQuoteDto>(QuoteRules.ChangedMeanwhileMessage);
            }
        }

        var view = await PublicQuotes.DescribeAsync(_context, found, now, cancellationToken, _gateway);
        return view is null ? null : Result.Success(view);
    }
}
