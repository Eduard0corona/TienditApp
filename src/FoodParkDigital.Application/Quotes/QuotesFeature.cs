using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Catalog;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Quotes;

// The quotes (Fase 29C, ADR 0053 points 14, 15 and 17): what the business proposes after looking at the job, for who takes
// orders in a business with service orders or the agenda. The server works out the taxes and the total of each version.

/// <summary>The quotes of the business, one row per version (the replaced ones only in their quote's history).</summary>
[RequiresPermission(TenantPermission.ManageOrders)]
[RequiresCapability(TenantCapability.Service | TenantCapability.Appointments)]
public record ListQuotesQuery(QuoteListFilter Filter = QuoteListFilter.Open, string? Search = null, int PageNumber = 1, int PageSize = 20)
    : IRequest<PaginatedList<QuoteSummaryDto>>;

/// <summary>A version of a quote with the history of its quote.</summary>
[RequiresPermission(TenantPermission.ManageOrders)]
[RequiresCapability(TenantCapability.Service | TenantCapability.Appointments)]
public record GetQuoteQuery(Guid Id) : IRequest<QuoteDto>;

/// <summary>How long the business's quotes are valid and how its prices are written.</summary>
[RequiresPermission(TenantPermission.ManageOrders)]
[RequiresCapability(TenantCapability.Service | TenantCapability.Appointments)]
public record GetQuoteSettingsQuery : IRequest<QuoteSettingsDto>;

/// <summary>
/// Starts a quote: a draft without lines for a customer, or for the visit of an appointment (its customer, and what was paid
/// for the visit when it is taken off). An appointment has one quote: asking again opens the one it has.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
[RequiresCapability(TenantCapability.Service | TenantCapability.Appointments)]
public record CreateQuoteCommand(
    Guid? AppointmentId, Guid? CustomerId, string? CustomerName, string? CustomerPhone, string? CustomerEmail)
    : IRequest<Result<QuoteDto>>, ITransactionalRequest;

/// <summary>Everything a draft says: who it is for, its validity, its note and its lines, in order.</summary>
[RequiresPermission(TenantPermission.ManageOrders)]
[RequiresCapability(TenantCapability.Service | TenantCapability.Appointments)]
public record UpdateQuoteDraftCommand(
    Guid Id, Guid? CustomerId, string CustomerName, string CustomerPhone, string? CustomerEmail, int ValidityDays, string? Note,
    IReadOnlyList<QuoteLineRequest> Lines) : IRequest<Result<QuoteDto>>;

/// <summary>
/// The customer gets the draft: valid from today for its days, with its link. A correction replaces the version the customer
/// had.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
[RequiresCapability(TenantCapability.Service | TenantCapability.Appointments)]
public record SendQuoteCommand(Guid Id) : IRequest<Result<QuoteDto>>, ITransactionalRequest;

/// <summary>Starts the correction of the version the customer has, or opens the one already started.</summary>
[RequiresPermission(TenantPermission.ManageOrders)]
[RequiresCapability(TenantCapability.Service | TenantCapability.Appointments)]
public record ReviseQuoteCommand(Guid Id) : IRequest<Result<QuoteDto>>, ITransactionalRequest;

/// <summary>Drops a draft before sending it; it stays in the history.</summary>
[RequiresPermission(TenantPermission.ManageOrders)]
[RequiresCapability(TenantCapability.Service | TenantCapability.Appointments)]
public record DiscardQuoteCommand(Guid Id) : IRequest<Result<QuoteDto>>;

/// <summary>What the customer answered to a valid sent quote, and how: they accepted or rejected it.</summary>
[RequiresPermission(TenantPermission.ManageOrders)]
[RequiresCapability(TenantCapability.Service | TenantCapability.Appointments)]
public record RecordQuoteReplyCommand(Guid Id, bool Accepted, QuoteReplyChannel Channel, string? Response) : IRequest<Result<QuoteDto>>;

/// <summary>How many days the business's quotes are valid once sent (owners and admins).</summary>
[RequiresTenantManager]
[RequiresCapability(TenantCapability.Service | TenantCapability.Appointments)]
public record UpdateQuoteSettingsCommand(int ValidityDays) : IRequest<Result<QuoteSettingsDto>>;

/// <summary>
/// The WhatsApp template the business's quotes go out with (Fase 29D, owners and admins): an active one for quotes, or
/// TienditApp's text (null).
/// </summary>
[RequiresTenantManager]
[RequiresCapability(TenantCapability.Service | TenantCapability.Appointments)]
public record SetQuoteWhatsAppTemplateCommand(Guid? TemplateId) : IRequest<Result<QuoteSettingsDto>>;

public class ListQuotesQueryValidator : AbstractValidator<ListQuotesQuery>
{
    public ListQuotesQueryValidator()
    {
        RuleFor(x => x.Filter).IsInEnum();
        RuleFor(x => x.Search).MaximumLength(100);
    }
}

public class CreateQuoteCommandValidator : AbstractValidator<CreateQuoteCommand>
{
    public CreateQuoteCommandValidator()
    {
        RuleFor(x => x.AppointmentId).NotEqual(Guid.Empty);
        RuleFor(x => x.CustomerId).NotEqual(Guid.Empty);
        // Without the visit, the customer: their name and phone.
        When(x => x.AppointmentId is null, () =>
        {
            RuleFor(x => x.CustomerName).NotEmpty().WithMessage(QuoteRules.CustomerNameMessage)
                .MaximumLength(QuoteRules.MaxCustomerNameLength).WithMessage(QuoteRules.CustomerNameTooLongMessage);
            RuleFor(x => x.CustomerPhone).NotEmpty().WithMessage(QuoteRules.CustomerPhoneMessage)
                .MaximumLength(QuoteRules.MaxCustomerPhoneLength).WithMessage(QuoteRules.CustomerPhoneMessage);
            RuleFor(x => x.CustomerEmail).Must(BeAnEmail).WithMessage(InvoiceEmailAddress.InvalidMessage);
        });
    }

    internal static bool BeAnEmail(string? email) => string.IsNullOrWhiteSpace(email) || InvoiceEmailAddress.IsValid(email.Trim());
}

public class UpdateQuoteDraftCommandValidator : AbstractValidator<UpdateQuoteDraftCommand>
{
    public UpdateQuoteDraftCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEqual(Guid.Empty);
        RuleFor(x => x.CustomerName).NotEmpty().WithMessage(QuoteRules.CustomerNameMessage)
            .MaximumLength(QuoteRules.MaxCustomerNameLength).WithMessage(QuoteRules.CustomerNameTooLongMessage);
        RuleFor(x => x.CustomerPhone).NotEmpty().WithMessage(QuoteRules.CustomerPhoneMessage)
            .MaximumLength(QuoteRules.MaxCustomerPhoneLength).WithMessage(QuoteRules.CustomerPhoneMessage);
        RuleFor(x => x.CustomerEmail).Must(CreateQuoteCommandValidator.BeAnEmail).WithMessage(InvoiceEmailAddress.InvalidMessage);
        RuleFor(x => x.ValidityDays).InclusiveBetween(QuoteRules.MinValidityDays, QuoteRules.MaxValidityDays)
            .WithMessage(QuoteRules.ValidityDaysMessage);
        RuleFor(x => x.Note).MaximumLength(QuoteRules.MaxNoteLength).WithMessage(QuoteRules.NoteTooLongMessage);
        RuleFor(x => x.Lines).Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(QuoteRules.DescriptionMessage)
            .Must(lines => lines.Count <= QuoteRules.MaxLines).WithMessage(QuoteRules.TooManyLinesMessage);
        RuleForEach(x => x.Lines).NotNull().WithMessage(QuoteRules.DescriptionMessage);
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEqual(Guid.Empty);
            line.RuleFor(l => l.Description).NotEmpty().When(l => l.ProductId is null).WithMessage(QuoteRules.DescriptionMessage);
            line.RuleFor(l => l.Description)
                .Must(d => d is null || d.Trim().Length <= QuoteRules.MaxDescriptionLength).WithMessage(QuoteRules.DescriptionTooLongMessage);
            line.RuleFor(l => l.Quantity)
                .Must(q => q > 0 && q <= QuoteRules.MaxQuantity && decimal.Round(q, 3) == q).WithMessage(QuoteRules.QuantityMessage);
            line.RuleFor(l => l.UnitPrice)
                .Must(p => p >= 0 && p <= QuoteRules.MaxUnitPrice && decimal.Round(p, 2) == p).WithMessage(QuoteRules.UnitPriceMessage);
        });
    }
}

public class RecordQuoteReplyCommandValidator : AbstractValidator<RecordQuoteReplyCommand>
{
    public RecordQuoteReplyCommandValidator()
    {
        RuleFor(x => x.Channel).IsInEnum().WithMessage(QuoteRules.ReplyChannelMessage);
        RuleFor(x => x.Response).MaximumLength(QuoteRules.MaxResponseLength).WithMessage(QuoteRules.ResponseTooLongMessage);
    }
}

public class UpdateQuoteSettingsCommandValidator : AbstractValidator<UpdateQuoteSettingsCommand>
{
    public UpdateQuoteSettingsCommandValidator()
    {
        RuleFor(x => x.ValidityDays).InclusiveBetween(QuoteRules.MinValidityDays, QuoteRules.MaxValidityDays)
            .WithMessage(QuoteRules.ValidityDaysMessage);
    }
}

public class QuotesHandler :
    IRequestHandler<ListQuotesQuery, PaginatedList<QuoteSummaryDto>>,
    IRequestHandler<GetQuoteQuery, QuoteDto>,
    IRequestHandler<GetQuoteSettingsQuery, QuoteSettingsDto>,
    IRequestHandler<UpdateQuoteDraftCommand, Result<QuoteDto>>,
    IRequestHandler<SendQuoteCommand, Result<QuoteDto>>,
    IRequestHandler<ReviseQuoteCommand, Result<QuoteDto>>,
    IRequestHandler<DiscardQuoteCommand, Result<QuoteDto>>,
    IRequestHandler<RecordQuoteReplyCommand, Result<QuoteDto>>,
    IRequestHandler<UpdateQuoteSettingsCommand, Result<QuoteSettingsDto>>,
    IRequestHandler<SetQuoteWhatsAppTemplateCommand, Result<QuoteSettingsDto>>
{
    public const string TemplateNotFoundMessage = "Elige una plantilla activa para cotizaciones, o el texto de TienditApp.";
    public const string AppointmentNotFoundMessage = "No encontramos esa cita.";
    public const string CustomerNotFoundMessage = "No encontramos ese cliente en tu lista.";
    public const string ProductNotFoundMessage = "Un renglón es de un producto que ya no está en tu catálogo: escríbelo a mano.";
    public const string AlreadyQuotingMessage = "Alguien más empezó la cotización de esta visita al mismo tiempo: vuelve a abrirla.";
    public const string AlreadyRevisingMessage = "Alguien más empezó la corrección de esta cotización al mismo tiempo: ábrela desde la cotización.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public QuotesHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUser,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUser = currentUser;
        _clock = clock;
    }

    private Guid TenantId => _tenantService.TenantId;

    public async Task<PaginatedList<QuoteSummaryDto>> Handle(ListQuotesQuery request, CancellationToken cancellationToken)
    {
        var today = await QuoteCalendar.TodayAsync(_context, TenantId, _clock.UtcNow, cancellationToken);
        var quotes = _context.Quotes.AsNoTracking().Where(q => q.TenantId == TenantId && q.Status != QuoteStatus.Superseded);
        quotes = request.Filter switch
        {
            QuoteListFilter.Open => quotes.Where(q => q.Status == QuoteStatus.Draft || (q.Status == QuoteStatus.Sent && q.ValidUntil >= today)),
            QuoteListFilter.Accepted => quotes.Where(q => q.Status == QuoteStatus.Accepted),
            QuoteListFilter.Rejected => quotes.Where(q => q.Status == QuoteStatus.Rejected),
            QuoteListFilter.Expired => quotes.Where(q => q.Status == QuoteStatus.Sent && q.ValidUntil < today),
            QuoteListFilter.Discarded => quotes.Where(q => q.Status == QuoteStatus.Discarded),
            _ => quotes
        };
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            quotes = quotes.Where(q => q.Number.Contains(search) || q.CustomerName.Contains(search) || q.CustomerPhone.Contains(search));
        }

        var page = await PaginatedList<QuoteSummaryDto>.CreateAsync(
            quotes
                .OrderByDescending(q => q.UpdatedAt ?? q.CreatedAt).ThenByDescending(q => q.Number).ThenByDescending(q => q.Revision)
                .Select(q => new QuoteSummaryDto
                {
                    Id = q.Id,
                    Number = q.Number,
                    Revision = q.Revision,
                    Status = q.Status == QuoteStatus.Sent && q.ValidUntil < today ? QuoteStatus.Expired : q.Status,
                    CustomerName = q.CustomerName,
                    CustomerPhone = q.CustomerPhone,
                    Total = q.Total,
                    ValidUntil = q.ValidUntil,
                    UpdatedAt = q.UpdatedAt ?? q.CreatedAt,
                    FromVisit = q.AppointmentId != null
                }),
            request.PageNumber, request.PageSize, cancellationToken);
        return page;
    }

    public async Task<QuoteDto> Handle(GetQuoteQuery request, CancellationToken cancellationToken)
    {
        var quote = await FindAsync(request.Id, cancellationToken, tracking: false);
        return await DescribeAsync(quote, cancellationToken);
    }

    public async Task<QuoteSettingsDto> Handle(GetQuoteSettingsQuery request, CancellationToken cancellationToken)
        => await SettingsAsync(cancellationToken);

    public async Task<Result<QuoteDto>> Handle(UpdateQuoteDraftCommand request, CancellationToken cancellationToken)
    {
        var quote = await FindAsync(request.Id, cancellationToken);
        if (quote.Status != QuoteStatus.Draft)
            return Result.Failure<QuoteDto>(QuoteRules.NotDraftMessage);
        if (request.CustomerId is { } customerId && customerId != quote.CustomerId
            && !await _context.Customers.AnyAsync(c => c.Id == customerId && c.TenantId == TenantId, cancellationToken))
            return Result.Failure<QuoteDto>(CustomerNotFoundMessage);

        var sales = await TenantSalesSettings.ReadAsync(_context, TenantId, cancellationToken);
        var productIds = request.Lines.Select(line => line.ProductId).OfType<Guid>().Distinct().ToList();
        var products = await _context.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id) && p.TenantId == TenantId)
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        if (products.Count != productIds.Count)
            return Result.Failure<QuoteDto>(ProductNotFoundMessage);

        var now = _clock.UtcNow;
        try
        {
            var lines = request.Lines.Select(line =>
            {
                if (line.ProductId is { } productId)
                {
                    var product = products[productId];
                    var description = string.IsNullOrWhiteSpace(line.Description) ? product.Name : line.Description;
                    // A product not sold in fractions is quoted whole, as it is sold: its order takes its supplies by those units.
                    if (!UnitTypeRules.IsValidQuantity(line.Quantity, product.Unit))
                        throw new DomainException(QuoteRules.WholeQuantityMessage(description));
                    return new QuoteLineInput(productId, description, line.Quantity, line.UnitPrice, product.GetVatRate(sales.DefaultTaxRate));
                }
                // Written by hand: the business's IVA, as its services without a rate of their own.
                return new QuoteLineInput(null, line.Description ?? string.Empty, line.Quantity, line.UnitPrice,
                    VatRate.ForProduct(null, false, sales.DefaultTaxRate));
            }).ToList();
            quote.UpdateDetails(new QuoteCustomer(request.CustomerId, request.CustomerName, request.CustomerPhone, request.CustomerEmail),
                request.ValidityDays, request.Note, now);
            quote.ReplaceLines(lines, now);
            quote.SetVisitCredit(await QuoteVisitCredits.ForAsync(_context, TenantId, quote.AppointmentId, cancellationToken), now);
        }
        catch (DomainException ex)
        {
            return Result.Failure<QuoteDto>(ex.Message);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(await DescribeAsync(quote, cancellationToken));
    }

    public async Task<Result<QuoteDto>> Handle(SendQuoteCommand request, CancellationToken cancellationToken)
    {
        var quote = await FindAsync(request.Id, cancellationToken);
        if (quote.Status != QuoteStatus.Draft)
            return Result.Failure<QuoteDto>(QuoteRules.NotDraftMessage);

        // The version the customer has: the latest earlier one sent (even expired) or rejected.
        var replaced = quote.Revision == 1
            ? null
            : await _context.Quotes
                .Include(q => q.Lines)
                .Where(q => q.TenantId == TenantId && q.Number == quote.Number && q.Revision < quote.Revision
                    && q.Status != QuoteStatus.Discarded)
                .OrderByDescending(q => q.Revision)
                .FirstOrDefaultAsync(cancellationToken);
        if (quote.Revision > 1 && replaced is null)
            return Result.Failure<QuoteDto>(QuoteRules.NotReplaceableMessage);

        var now = _clock.UtcNow;
        var today = await QuoteCalendar.TodayAsync(_context, TenantId, now, cancellationToken);
        try
        {
            // What was paid for the visit, as it stands when the customer gets it.
            quote.SetVisitCredit(await QuoteVisitCredits.ForAsync(_context, TenantId, quote.AppointmentId, cancellationToken), now);
            quote.Send(today, OrderTracking.NewToken(), replaced, now);
        }
        catch (DomainException ex)
        {
            return Result.Failure<QuoteDto>(ex.Message);
        }

        _context.AuditLogs.Add(QuoteAudit.Create(quote, QuoteAudit.Sent, _currentUser.UserName, new { quote.ValidUntil }));
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(await DescribeAsync(quote, cancellationToken));
    }

    public async Task<Result<QuoteDto>> Handle(ReviseQuoteCommand request, CancellationToken cancellationToken)
    {
        var quote = await FindAsync(request.Id, cancellationToken);
        // A correction already started opens: a folio has one draft at a time.
        var draft = await _context.Quotes
            .AsNoTracking()
            .Where(q => q.TenantId == TenantId && q.Number == quote.Number && q.Status == QuoteStatus.Draft)
            .Select(q => (Guid?)q.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (draft is { } draftId && quote.Status is (QuoteStatus.Sent or QuoteStatus.Rejected))
            return Result.Success(await DescribeAsync(await FindAsync(draftId, cancellationToken, tracking: false), cancellationToken));

        // After every version, the discarded drafts included: their numbers are not given again.
        var next = await _context.Quotes
            .Where(q => q.TenantId == TenantId && q.Number == quote.Number)
            .MaxAsync(q => q.Revision, cancellationToken) + 1;
        Quote revision;
        try
        {
            revision = quote.Revise(next, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure<QuoteDto>(ex.Message);
        }

        _context.Quotes.Add(revision);
        _context.AuditLogs.Add(QuoteAudit.Create(revision, QuoteAudit.Revised, _currentUser.UserName));
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Someone started the correction at the same time (a folio has one draft): this one is undone whole.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            if (await _context.Quotes.AsNoTracking().AnyAsync(
                    q => q.TenantId == TenantId && q.Number == quote.Number && q.Status == QuoteStatus.Draft, cancellationToken))
                return Result.Failure<QuoteDto>(AlreadyRevisingMessage);
            throw;
        }

        return Result.Success(await DescribeAsync(revision, cancellationToken));
    }

    public async Task<Result<QuoteDto>> Handle(DiscardQuoteCommand request, CancellationToken cancellationToken)
    {
        var quote = await FindAsync(request.Id, cancellationToken);
        try
        {
            quote.Discard(_clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure<QuoteDto>(ex.Message);
        }

        _context.AuditLogs.Add(QuoteAudit.Create(quote, QuoteAudit.Discarded, _currentUser.UserName));
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(await DescribeAsync(quote, cancellationToken));
    }

    public async Task<Result<QuoteDto>> Handle(RecordQuoteReplyCommand request, CancellationToken cancellationToken)
    {
        var quote = await FindAsync(request.Id, cancellationToken);
        var now = _clock.UtcNow;
        var today = await QuoteCalendar.TodayAsync(_context, TenantId, now, cancellationToken);
        try
        {
            if (request.Accepted)
                quote.Accept(today, request.Channel, request.Response, now);
            else
                quote.Reject(today, request.Channel, request.Response, now);
        }
        catch (DomainException ex)
        {
            return Result.Failure<QuoteDto>(ex.Message);
        }

        // What the customer said stays on the quote; the activity keeps that they answered and how.
        _context.AuditLogs.Add(QuoteAudit.Create(quote, request.Accepted ? QuoteAudit.Accepted : QuoteAudit.Rejected,
            _currentUser.UserName, new { Channel = request.Channel.ToString() }));
        // Accepted, a correction started on it can never be sent: it is discarded, and stays in the history.
        if (request.Accepted)
        {
            var pendingCorrections = await _context.Quotes
                .Include(q => q.Lines)
                .Where(q => q.TenantId == TenantId && q.Number == quote.Number && q.Status == QuoteStatus.Draft)
                .ToListAsync(cancellationToken);
            foreach (var correction in pendingCorrections)
            {
                correction.Discard(now);
                _context.AuditLogs.Add(QuoteAudit.Create(correction, QuoteAudit.Discarded, _currentUser.UserName,
                    new { Reason = "AcceptedEarlierVersion" }));
            }
        }
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(await DescribeAsync(quote, cancellationToken));
    }

    public async Task<Result<QuoteSettingsDto>> Handle(UpdateQuoteSettingsCommand request, CancellationToken cancellationToken)
    {
        var settings = await _context.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == TenantId, cancellationToken);
        if (settings is null)
            return Result.Failure<QuoteSettingsDto>("Configura primero tu negocio.");

        var previous = settings.QuoteValidityDays;
        try
        {
            settings.SetQuoteValidityDays(request.ValidityDays);
        }
        catch (DomainException ex)
        {
            return Result.Failure<QuoteSettingsDto>(ex.Message);
        }

        if (previous != settings.QuoteValidityDays)
        {
            _context.AuditLogs.Add(new Domain.Audit.Entities.AuditLog(
                tenantId: TenantId,
                entityName: nameof(Domain.Tenants.Entities.TenantSettings),
                entityId: settings.Id.ToString(),
                action: QuoteAudit.SettingsUpdated,
                performedBy: _currentUser.UserName ?? "system",
                metadata: System.Text.Json.JsonSerializer.Serialize(new { Previous = previous, New = settings.QuoteValidityDays })));
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(await SettingsAsync(cancellationToken));
    }

    public async Task<Result<QuoteSettingsDto>> Handle(SetQuoteWhatsAppTemplateCommand request, CancellationToken cancellationToken)
    {
        var settings = await _context.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == TenantId, cancellationToken);
        if (settings is null)
            return Result.Failure<QuoteSettingsDto>("Configura primero tu negocio.");
        if (request.TemplateId is { } templateId
            && !await _context.MessageTemplates.AnyAsync(t => t.Id == templateId && t.TenantId == TenantId && t.IsActive
                && t.Purpose == Domain.Messaging.Enums.MessagePurpose.QuoteDocument, cancellationToken))
            return Result.Failure<QuoteSettingsDto>(TemplateNotFoundMessage);

        var previous = settings.QuoteWhatsAppTemplateId;
        try
        {
            settings.UseQuoteWhatsAppTemplate(request.TemplateId);
        }
        catch (DomainException ex)
        {
            return Result.Failure<QuoteSettingsDto>(ex.Message);
        }

        if (previous != settings.QuoteWhatsAppTemplateId)
        {
            _context.AuditLogs.Add(new Domain.Audit.Entities.AuditLog(
                tenantId: TenantId,
                entityName: nameof(Domain.Tenants.Entities.TenantSettings),
                entityId: settings.Id.ToString(),
                action: QuoteAudit.WhatsAppTemplateChanged,
                performedBy: _currentUser.UserName ?? "system",
                metadata: System.Text.Json.JsonSerializer.Serialize(new { Previous = previous, New = settings.QuoteWhatsAppTemplateId })));
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(await SettingsAsync(cancellationToken));
    }

    private async Task<QuoteSettingsDto> SettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _context.TenantSettings
            .AsNoTracking()
            .Where(s => s.TenantId == TenantId)
            .Select(s => new QuoteSettingsDto
            {
                ValidityDays = s.QuoteValidityDays,
                PricesIncludeTax = s.PricesIncludeTax,
                DefaultTaxRate = s.DefaultTaxRate,
                WhatsAppTemplateId = s.QuoteWhatsAppTemplateId
            })
            .FirstOrDefaultAsync(cancellationToken)
           ?? new QuoteSettingsDto
           {
               ValidityDays = QuoteRules.DefaultValidityDays,
               PricesIncludeTax = TenantSalesSettings.Legacy.PricesIncludeTax,
               DefaultTaxRate = TenantSalesSettings.Legacy.DefaultTaxRate
           };
        settings.WhatsAppConnected = await Messaging.Campaigns.CampaignRules.IsWhatsAppConnectedAsync(_context, TenantId, cancellationToken);
        settings.WhatsAppTemplates = await QuoteDeliveries.TemplatesAsync(_context, TenantId, cancellationToken);
        // A template deleted, turned off or moved to another purpose since is TienditApp's text again.
        if (settings.WhatsAppTemplates.All(t => t.Id != settings.WhatsAppTemplateId))
            settings.WhatsAppTemplateId = null;
        return settings;
    }

    private Task<Quote> FindAsync(Guid id, CancellationToken cancellationToken, bool tracking = true)
        => FindAsync(_context, TenantId, id, cancellationToken, tracking);

    /// <summary>A version of a quote of the business, besides the global filter; not found otherwise.</summary>
    internal static async Task<Quote> FindAsync(
        IApplicationDbContext context, Guid tenantId, Guid id, CancellationToken cancellationToken, bool tracking = true)
    {
        var quotes = context.Quotes.Include(q => q.Lines).Where(q => q.Id == id && q.TenantId == tenantId);
        return await (tracking ? quotes : quotes.AsNoTracking()).FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Quote), id);
    }

    private async Task<QuoteDto> DescribeAsync(Quote quote, CancellationToken cancellationToken)
        => await QuoteViews.DescribeAsync(_context, quote,
            await QuoteCalendar.TodayAsync(_context, TenantId, _clock.UtcNow, cancellationToken), cancellationToken);
}

/// <summary>Starts a quote with the next folio of the business, inside the request's transaction.</summary>
public class CreateQuoteCommandHandler : IRequestHandler<CreateQuoteCommand, Result<QuoteDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;
    private readonly IFolioGenerator _folios;
    private readonly IDateTimeProvider _clock;

    public CreateQuoteCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUser,
        IFolioGenerator folios,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUser = currentUser;
        _folios = folios;
        _clock = clock;
    }

    private Guid TenantId => _tenantService.TenantId;

    public async Task<Result<QuoteDto>> Handle(CreateQuoteCommand request, CancellationToken cancellationToken)
    {
        var settings = await _context.TenantSettings.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == TenantId, cancellationToken);
        if (settings is null)
            return Result.Failure<QuoteDto>("Configura primero tu negocio.");

        QuoteCustomer customer;
        QuoteVisitCredit? credit = null;
        try
        {
            if (request.AppointmentId is { } appointmentId)
            {
                // The business of the request besides the global filter.
                var appointment = await _context.Appointments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == appointmentId && a.TenantId == TenantId, cancellationToken);
                if (appointment is null)
                    return Result.Failure<QuoteDto>(QuotesHandler.AppointmentNotFoundMessage);
                // A visit open or done: a cancelled one, one the customer missed or a request not accepted yet is not quoted.
                if (appointment.Visit is null)
                    return Result.Failure<QuoteDto>(QuoteRules.NotAVisitMessage);
                if (appointment.Status is AppointmentStatus.Cancelled or AppointmentStatus.NoShow)
                    return Result.Failure<QuoteDto>(QuoteRules.VisitClosedMessage);
                if (appointment.Status == AppointmentStatus.Requested)
                    return Result.Failure<QuoteDto>(QuoteRules.VisitRequestedMessage);

                // An appointment has one quote: its current version opens.
                var existing = await _context.Quotes
                    .AsNoTracking()
                    .Where(q => q.TenantId == TenantId && q.AppointmentId == appointmentId && q.Status != QuoteStatus.Discarded
                        && q.Status != QuoteStatus.Superseded)
                    .OrderBy(q => q.Status == QuoteStatus.Draft ? 1 : 0).ThenByDescending(q => q.Revision)
                    .Select(q => (Guid?)q.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (existing is { } existingId)
                    return Result.Success(await DescribeAsync(await QuotesHandler.FindAsync(_context, TenantId, existingId, cancellationToken, tracking: false), cancellationToken));

                customer = new QuoteCustomer(null, appointment.CustomerName, appointment.CustomerPhone, appointment.CustomerEmail);
                credit = await QuoteVisitCredits.ForAsync(_context, TenantId, appointmentId, cancellationToken);
            }
            else
            {
                if (request.CustomerId is { } customerId
                    && !await _context.Customers.AnyAsync(c => c.Id == customerId && c.TenantId == TenantId, cancellationToken))
                    return Result.Failure<QuoteDto>(QuotesHandler.CustomerNotFoundMessage);
                customer = new QuoteCustomer(request.CustomerId, request.CustomerName!, request.CustomerPhone!, request.CustomerEmail);
            }
        }
        catch (DomainException ex)
        {
            return Result.Failure<QuoteDto>(ex.Message);
        }

        var now = _clock.UtcNow;
        var quote = Quote.Start(TenantId, await _folios.NextQuoteNumberAsync(TenantId, cancellationToken), customer,
            request.AppointmentId, settings.PricesIncludeTax, settings.QuoteValidityDays, now);
        quote.SetVisitCredit(credit, now);
        _context.Quotes.Add(quote);
        _context.AuditLogs.Add(QuoteAudit.Create(quote, QuoteAudit.Created, _currentUser.UserName));
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (request.AppointmentId is { } appointmentId)
        {
            // Someone started the quote of the visit at the same time (a visit has one): this one is undone whole, folio
            // included.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            if (await _context.Quotes.AsNoTracking().AnyAsync(
                    q => q.TenantId == TenantId && q.AppointmentId == appointmentId && q.Revision == 1
                        && q.Status != QuoteStatus.Discarded, cancellationToken))
                return Result.Failure<QuoteDto>(QuotesHandler.AlreadyQuotingMessage);
            throw;
        }

        return Result.Success(await DescribeAsync(quote, cancellationToken));
    }

    private async Task<QuoteDto> DescribeAsync(Quote quote, CancellationToken cancellationToken)
        => await QuoteViews.DescribeAsync(_context, quote,
            await QuoteCalendar.TodayAsync(_context, TenantId, _clock.UtcNow, cancellationToken), cancellationToken);
}
