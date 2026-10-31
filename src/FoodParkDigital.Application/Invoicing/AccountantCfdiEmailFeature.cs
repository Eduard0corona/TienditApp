using System.Globalization;
using System.Net;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Fiscal.Events;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Invoicing;

/// <summary>An email of a month's CFDI to the accountant, as Reportes › CFDI emitidos shows it (Fase 28O, ADR 0060).</summary>
public class CfdiMonthEmailDto
{
    public Guid Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string To { get; set; } = null!;
    public DateTimeOffset RequestedAt { get; set; }
    /// <summary>TienditApp sent it on its own at the start of the month; otherwise a person asked for it.</summary>
    public bool ByTienditApp { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    /// <summary>How many CFDI its list carried when it went out.</summary>
    public int? CfdiCount { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public CfdiMonthEmailFailure? Failure { get; set; }
    /// <summary>The tries the mail server did not take; the queue tries again while it is neither sent nor refused.</summary>
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LastFailedAttemptAt { get; set; }

    public static CfdiMonthEmailDto From(CfdiMonthEmail email) => new()
    {
        Id = email.Id,
        Year = email.Year,
        Month = email.Month,
        To = email.To,
        RequestedAt = email.RequestedAt,
        ByTienditApp = email.IsMonthly,
        SentAt = email.SentAt,
        CfdiCount = email.CfdiCount,
        FailedAt = email.FailedAt,
        Failure = email.Failure,
        FailedAttempts = email.FailedAttempts,
        LastFailedAttemptAt = email.LastFailedAttemptAt
    };
}

/// <summary>
/// The CFDI of a month to the business's accountant by email (Fase 28O, ADR 0060): the file of Reportes › CFDI emitidos,
/// sent by hand with «Mandar al contador» or by TienditApp at the start of each month for the month before.
/// </summary>
public static class AccountantCfdiEmails
{
    /// <summary>The most emails to the accountant a business asks for by hand in a day: a typo fixed, not a mailing.</summary>
    public const int MaxPerBusinessPerDay = 5;
    /// <summary>How many of a month's latest emails Reportes › CFDI emitidos shows.</summary>
    public const int Shown = 5;
    /// <summary>
    /// The biggest file an email carries: encoded for mail it grows by a third, and many mail servers refuse a message of more
    /// than 10 MB. A bigger one is downloaded and sent by hand.
    /// </summary>
    public const int MaxFileBytes = 7 * 1024 * 1024;
    /// <summary>TienditApp sends the month before from the first day of each month at this hour, in the business's time zone…</summary>
    public const int MonthlyFromHour = 8;
    /// <summary>…until the end of this day: after it, a month that did not go waits for a person, never arrives by surprise.</summary>
    public const int MonthlyLastDay = 7;

    public static readonly TimeSpan Day = TimeSpan.FromDays(1);

    public const string NotStartedMessage = "Ese mes todavía no empieza: elige uno que ya empezó.";
    public const string NoCfdiMessage = "Ese mes no tiene CFDI que mandar al contador.";
    public static readonly string TooManyTodayMessage =
        $"Hoy ya se mandaron {MaxPerBusinessPerDay} veces los CFDI al contador: vuelve a intentarlo mañana o descarga el archivo.";

    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-MX");

    /// <summary>
    /// The month TienditApp sends on its own at <paramref name="now"/> in <paramref name="zone"/>: the one before, from the
    /// first day at <see cref="MonthlyFromHour"/> to the end of day <see cref="MonthlyLastDay"/>; null outside those days or
    /// for a month before CFDI 4.0.
    /// </summary>
    public static (int Year, int Month)? MonthlyMonth(DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone);
        if (local.Day > MonthlyLastDay || (local.Day == 1 && local.Hour < MonthlyFromHour))
            return null;
        var month = new DateOnly(local.Year, local.Month, 1).AddMonths(-1);
        return month.Year < CfdiMonthEmail.FirstYear ? null : (month.Year, month.Month);
    }

    /// <summary>
    /// Whether some time zone is within the days the month goes on its own: UTC's last day of the month (zones ahead of it are
    /// already in the next one) to day <see cref="MonthlyLastDay"/> + 1 (zones behind it are still in day 7).
    /// </summary>
    public static bool InMonthlyDaysSomewhere(DateTimeOffset now)
    {
        var utc = now.UtcDateTime;
        return utc.Day <= MonthlyLastDay + 1 || utc.AddDays(1).Day == 1;
    }

    public static string MonthName(int year, int month) => new DateTime(year, month, 1).ToString("MMMM 'de' yyyy", Spanish);

    public static string Subject(int year, int month, string businessName)
        => $"CFDI de {MonthName(year, month)} de {businessName.ReplaceLineEndings(" ").Trim()}";

    /// <summary>
    /// What the accountant needs to know of the file: whose and which month (up to today, at <paramref name="now"/>, while the
    /// month has not ended where the business is), how many of each kind, and what it holds.
    /// </summary>
    internal static string Body(IssuedCfdiFile file, int year, int month, string businessName, DateTimeOffset now)
    {
        var business = WebUtility.HtmlEncode(businessName.ReplaceLineEndings(" ").Trim());
        var ended = now >= file.Month.End;
        var lastDay = ended ? file.Month.LastDay : DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, file.Month.Zone).DateTime);
        var days = $"del {file.Month.FirstDay.Day} al {lastDay.ToString("d 'de' MMMM 'de' yyyy", Spanish)}";
        var soFar = ended ? string.Empty : "<p>El mes todavía no termina: el archivo lleva lo timbrado hasta hoy.</p>";
        var kinds = string.Join("\n", file.Counts.Select(count => $"  <li>{KindLine(count)}</li>"));
        return $"""
            <p>Hola:</p>
            <p>{business} te envía sus CFDI de {MonthName(year, month)} ({days}), con fecha en ese mes:</p>
            <ul>
            {kinds}
            </ul>
            {soFar}
            <p>El archivo adjunto trae el XML timbrado de cada uno, por tipo (los cancelados, en su carpeta «cancelados»), y la lista de todos en CSV, con su estado ante el SAT.</p>
            <p>Si tienes dudas, responde este correo para escribirle a {business}.</p>
            """;
    }

    private static string KindLine(IssuedCfdiCount count)
    {
        var (label, feminine) = count.Kind switch
        {
            IssuedCfdiKind.OrderInvoice => ("Facturas", true),
            IssuedCfdiKind.GlobalInvoice => ("Facturas globales", true),
            IssuedCfdiKind.PaymentComplement => ("Complementos de pago", false),
            _ => ("Notas de crédito", true)
        };
        var parts = new List<string> { $"{count.InForce} {(count.InForce == 1 ? "vigente" : "vigentes")}" };
        if (count.Cancelled > 0)
            parts.Add($"{count.Cancelled} {(feminine ? "cancelada" : "cancelado")}{(count.Cancelled == 1 ? string.Empty : "s")}");
        if (count.WithoutStamp > 0)
            parts.Add($"{count.WithoutStamp} sin timbre (solo en la lista)");
        return $"{label}: {string.Join(", ", parts)}.";
    }
}

/// <summary>
/// «Mandar al contador» in Reportes › CFDI emitidos (owners and admins, Fase 28O): the month's file goes by email to that
/// address, which is kept as the accountant's for the next time (and for the month that goes on its own; audited when it
/// changes). A month that already started, with at least one CFDI; up to
/// <see cref="AccountantCfdiEmails.MaxPerBusinessPerDay"/> a day, counted under the business's invoicing lock so two requests
/// at once do not both pass the last one. It goes out from the queue.
/// </summary>
[RequiresTenantManager]
public record SendIssuedCfdisToAccountantCommand(int Year, int Month, string Email)
    : IRequest<Result<CfdiMonthEmailDto>>, ITransactionalRequest;

public class SendIssuedCfdisToAccountantCommandValidator : AbstractValidator<SendIssuedCfdisToAccountantCommand>
{
    public SendIssuedCfdisToAccountantCommandValidator()
    {
        IssuedCfdis.RuleForMonth(this, x => x.Year, x => x.Month);
        RuleFor(x => x.Email).NotEmpty().WithMessage(CfdiMonthEmail.MissingEmailMessage)
            .Must(email => InvoiceEmailAddress.IsValid(email.Trim())).WithMessage(InvoiceEmailAddress.InvalidMessage)
            .When(x => !string.IsNullOrWhiteSpace(x.Email), ApplyConditionTo.CurrentValidator);
    }
}

public class SendIssuedCfdisToAccountantCommandHandler
    : IRequestHandler<SendIssuedCfdisToAccountantCommand, Result<CfdiMonthEmailDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IInvoicingLock _lock;
    private readonly IDateTimeProvider _clock;

    public SendIssuedCfdisToAccountantCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser,
        IInvoicingLock invoicingLock,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _lock = invoicingLock;
        _clock = clock;
    }

    public async Task<Result<CfdiMonthEmailDto>> Handle(SendIssuedCfdisToAccountantCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return Result.Failure<CfdiMonthEmailDto>(InvoicingSettings.NotAvailableMessage);

        var tenantId = _tenant.TenantId;
        var now = _clock.UtcNow;
        var month = await IssuedCfdis.MonthAsync(_context, tenantId, request.Year, request.Month, cancellationToken);
        if (month.Start > now)
            return Result.Failure<CfdiMonthEmailDto>(AccountantCfdiEmails.NotStartedMessage);
        if (!await IssuedCfdis.Rows(_context, month, kind: null, status: null).AnyAsync(cancellationToken))
            return Result.Failure<CfdiMonthEmailDto>(AccountantCfdiEmails.NoCfdiMessage);

        await _lock.AcquireAsync(tenantId, cancellationToken);
        var since = now - AccountantCfdiEmails.Day;
        if (await _context.CfdiMonthEmails.CountAsync(e => e.RequestedByUserId != null && e.RequestedAt > since, cancellationToken)
            >= AccountantCfdiEmails.MaxPerBusinessPerDay)
            return Result.Failure<CfdiMonthEmailDto>(AccountantCfdiEmails.TooManyTodayMessage);

        var profile = await _context.InvoicingProfiles.FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            profile = InvoicingProfile.Create(tenantId);
            _context.InvoicingProfiles.Add(profile);
        }
        CfdiMonthEmail email;
        var before = profile.AccountantEmail;
        try
        {
            email = CfdiMonthEmail.Request(tenantId, request.Year, request.Month, request.Email, userId, now);
            profile.RememberAccountant(email.To);
        }
        catch (DomainException ex)
        {
            return Result.Failure<CfdiMonthEmailDto>(ex.Message);
        }

        // Where the month goes on its own moves with it: audited as in Configuración › Facturación, without the address.
        if (profile.AccountantEmail != before)
            InvoicingSettings.Audit(_context, tenantId, _currentUser, "AccountantChanged", new
            {
                HasEmail = true,
                EmailChanged = true,
                Monthly = profile.SendsMonthToAccountant
            });
        _context.CfdiMonthEmails.Add(email);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(CfdiMonthEmailDto.From(email));
    }
}

/// <summary>The latest emails of a month's CFDI to the accountant, the newest first (owners and admins).</summary>
[RequiresTenantManager]
public record GetCfdiMonthEmailsQuery(int Year, int Month) : IRequest<List<CfdiMonthEmailDto>>;

public class GetCfdiMonthEmailsQueryValidator : AbstractValidator<GetCfdiMonthEmailsQuery>
{
    public GetCfdiMonthEmailsQueryValidator()
    {
        IssuedCfdis.RuleForMonth(this, x => x.Year, x => x.Month);
    }
}

public class GetCfdiMonthEmailsQueryHandler : IRequestHandler<GetCfdiMonthEmailsQuery, List<CfdiMonthEmailDto>>
{
    private readonly IApplicationDbContext _context;

    public GetCfdiMonthEmailsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<CfdiMonthEmailDto>> Handle(GetCfdiMonthEmailsQuery request, CancellationToken cancellationToken)
        => (await _context.CfdiMonthEmails.AsNoTracking()
                .Where(e => e.Year == request.Year && e.Month == request.Month)
                .OrderByDescending(e => e.RequestedAt)
                .Take(AccountantCfdiEmails.Shown)
                .ToListAsync(cancellationToken))
            .Select(CfdiMonthEmailDto.From)
            .ToList();
}

/// <summary>
/// The month before to the accountant, on its own (the invoicing worker sends it in the business's scope, Fase 28O): when the
/// business asked for it and has the accountant's email, from the first day of the month at 8:00 to day 7 in its time zone,
/// once, and only when that month has CFDI. Two workers at once queue it once: the database keeps one per month and business.
/// </summary>
[AllowAnonymousRequest("Lo envía el worker de facturación (sistema), sin usuario, en el alcance del negocio.")]
public record QueueMonthlyAccountantEmailCommand : IRequest<Result<int>>;

public class QueueMonthlyAccountantEmailCommandHandler : IRequestHandler<QueueMonthlyAccountantEmailCommand, Result<int>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly IDateTimeProvider _clock;

    public QueueMonthlyAccountantEmailCommandHandler(IApplicationDbContext context, ICurrentTenantService tenant, IDateTimeProvider clock)
    {
        _context = context;
        _tenant = tenant;
        _clock = clock;
    }

    /// <returns>1 when it queued the month, 0 otherwise.</returns>
    public async Task<Result<int>> Handle(QueueMonthlyAccountantEmailCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenant.TenantId;
        var profile = await _context.InvoicingProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (profile is not { SendsMonthToAccountant: true, AccountantEmail: { } to })
            return Result.Success(0);

        var now = _clock.UtcNow;
        var timeZone = await _context.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);
        if (AccountantCfdiEmails.MonthlyMonth(now, TenantDateTimeResolver.Resolve(timeZone)) is not { } monthly)
            return Result.Success(0);
        var (year, month) = monthly;
        if (await QueuedAsync(year, month, cancellationToken))
            return Result.Success(0);
        var range = await IssuedCfdis.MonthAsync(_context, tenantId, year, month, cancellationToken);
        if (!await IssuedCfdis.Rows(_context, range, kind: null, status: null).AnyAsync(cancellationToken))
            return Result.Success(0);

        _context.CfdiMonthEmails.Add(CfdiMonthEmail.Request(tenantId, year, month, to, requestedByUserId: null, now));
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (!cancellationToken.IsCancellationRequested)
        {
            // Another worker queued it at the same moment: the unique index kept that one.
            if (!await QueuedAsync(year, month, cancellationToken))
                throw;
            return Result.Success(0);
        }
        return Result.Success(1);
    }

    private Task<bool> QueuedAsync(int year, int month, CancellationToken cancellationToken)
        => _context.CfdiMonthEmails.AnyAsync(e => e.RequestedByUserId == null && e.Year == year && e.Month == month, cancellationToken);
}

/// <summary>
/// Sends an email of a month's CFDI to the accountant from the outbox, after the change that asked for it: builds the file of
/// Reportes › CFDI emitidos as it is now and sends it, with how many of each kind it carries.
/// <list type="bullet">
/// <item>A month of more than <see cref="IssuedCfdiFiles.MaxCount"/> CFDI, or a file bigger than
/// <see cref="AccountantCfdiEmails.MaxFileBytes"/>, ends it: the business downloads it and sends it by hand.</item>
/// <item>A mail server that does not take it now counts the try and throws, for the outbox to retry; one that refuses the
/// address for good, or no provider, ends it. On the outbox's last attempt any failure ends it as not delivered, so it
/// never stays pending.</item>
/// </list>
/// </summary>
public sealed class CfdiMonthEmailRequestedHandler : IOutboxEventHandler<CfdiMonthEmailRequestedEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly IPrivateFileStorage _storage;
    private readonly IEmailService _email;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<CfdiMonthEmailRequestedHandler> _logger;

    public CfdiMonthEmailRequestedHandler(
        IApplicationDbContext context,
        IPrivateFileStorage storage,
        IEmailService email,
        IDateTimeProvider clock,
        ILogger<CfdiMonthEmailRequestedHandler> logger)
    {
        _context = context;
        _storage = storage;
        _email = email;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>The most CFDI an email takes (a test lowers it).</summary>
    internal int Limit { get; init; } = IssuedCfdiFiles.MaxCount;

    /// <summary>The biggest file an email carries (a test lowers it).</summary>
    internal int MaxFileBytes { get; init; } = AccountantCfdiEmails.MaxFileBytes;

    public async Task HandleAsync(CfdiMonthEmailRequestedEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
    {
        var email = await _context.CfdiMonthEmails.FirstOrDefaultAsync(e => e.Id == domainEvent.CfdiMonthEmailId, cancellationToken);
        if (email is null || !email.IsPending)
            return;

        try
        {
            await SendAsync(email, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && context.IsLastAttempt)
        {
            // The outbox sets the message aside after this attempt: the email ends, and a person can send it again.
            _logger.LogWarning("The email {CfdiMonthEmailId} to the accountant was not delivered after {Attempts} attempts: {Error}",
                email.Id, context.Attempt, ex.GetType().Name);
            email.Fail(CfdiMonthEmailFailure.NotDelivered, _clock.UtcNow);
        }
    }

    private async Task SendAsync(CfdiMonthEmail email, CancellationToken cancellationToken)
    {
        var file = await IssuedCfdiFiles.BuildAsync(
            _context, _storage, _logger, email.TenantId, email.Year, email.Month, IssuedCfdiExportFormat.Zip, Limit, cancellationToken);
        if (file is null)
        {
            _logger.LogInformation("The email {CfdiMonthEmailId} to the accountant was not sent: the month has more than {Limit} CFDI.",
                email.Id, Limit);
            email.Fail(CfdiMonthEmailFailure.TooMany, _clock.UtcNow);
            return;
        }

        byte[] content;
        await using (var download = file.Download.Content)
        {
            if (download.Length > MaxFileBytes)
            {
                _logger.LogInformation("The email {CfdiMonthEmailId} to the accountant was not sent: its file has {Bytes} bytes.",
                    email.Id, download.Length);
                email.Fail(CfdiMonthEmailFailure.TooLarge, _clock.UtcNow);
                return;
            }
            using var buffer = new MemoryStream();
            await download.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        var business = await _context.Tenants.AsNoTracking()
            .Where(t => t.Id == email.TenantId)
            .Select(t => new { t.Name, t.ContactEmail })
            .FirstAsync(cancellationToken);
        var message = new DocumentEmail(
            email.To,
            AccountantCfdiEmails.Subject(email.Year, email.Month, business.Name),
            AccountantCfdiEmails.Body(file, email.Year, email.Month, business.Name, _clock.UtcNow),
            $"{business.Name} vía TienditApp",
            business.ContactEmail,
            [new EmailAttachment(file.Download.FileName, file.Download.ContentType, content)]);

        EmailSendOutcome outcome;
        try
        {
            outcome = await _email.SendDocumentsAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            email.AttemptFailed(_clock.UtcNow);
            await _context.SaveChangesAsync(cancellationToken);
            throw;
        }

        var now = _clock.UtcNow;
        switch (outcome)
        {
            case EmailSendOutcome.Sent:
                email.Sent(file.CfdiCount, now);
                break;
            case EmailSendOutcome.NotConfigured:
                email.Fail(CfdiMonthEmailFailure.NotConfigured, now);
                break;
            default:
                email.Fail(CfdiMonthEmailFailure.Rejected, now);
                break;
        }
    }
}
