using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Reports.ReadModels;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.DeliveryPlatforms;

/// <summary>Why a CFDI de retenciones is not registered or removed.</summary>
public static class PlatformWithholdingErrors
{
    /// <summary>Someone removed it since the person loaded it: the same text as any concurrent save.</summary>
    public const string Stale = DeliveryPlatformErrors.Stale;

    /// <summary>
    /// It goes to another RFC than the business's. The message names neither: the person sees both in the file and in
    /// Configuración, and an RFC does not go into a log.
    /// </summary>
    public const string OtherReceiver =
        "Este CFDI es para otro RFC, no para el de tu negocio. Revisa que lo hayas descargado de la cuenta de este negocio en la plataforma.";

    private const string AlreadyRegisteredStart = "Este CFDI ya está registrado en ";

    /// <summary>The same folio is in force in the business: where, so the person finds it.</summary>
    public static string AlreadyRegistered(PlatformWithholdingCertificate existing)
        => $"{AlreadyRegisteredStart}{OrderChannelRules.DisplayName(existing.Channel)}, {PlatformWithholdingText.Period(existing)}.";

    private const string ReplacedStart = "Este CFDI ya se sustituyó por otro que está registrado en ";

    /// <summary>The platform cancelled this CFDI and the one that replaces it is in force: this one does not count.</summary>
    public static string Replaced(PlatformWithholdingCertificate replacement)
        => $"{ReplacedStart}{OrderChannelRules.DisplayName(replacement.Channel)}, {PlatformWithholdingText.Period(replacement)}: no cuenta.";

    /// <summary>The reason of a CFDI taken out because the one registered replaces it.</summary>
    public static string ReplacedBy(PlatformWithholdingCertificate replacement)
        => $"Lo sustituye el CFDI con folio {replacement.Uuid.ToString()[..8].ToUpperInvariant()}.";

    /// <summary>The same CFDI, or the one that replaces it, is already in the business: the API answers 409 with the reason.</summary>
    public static bool IsConflict(string? error)
        => error?.StartsWith(AlreadyRegisteredStart, StringComparison.Ordinal) == true
           || error?.StartsWith(ReplacedStart, StringComparison.Ordinal) == true;

    /// <summary>Someone else's removal in between: the API answers 409 and the person reloads.</summary>
    public static bool IsStale(string? error) => error is Stale;
}

/// <summary>
/// Registers the CFDI de retenciones a platform issued to the business: what its XML says is kept, never the file. The
/// person says which platform issued it; the voucher counts once in the business.
/// </summary>
[RequiresTenantManager]
public record RegisterPlatformWithholdingCertificateCommand(OrderChannel Channel, string? Xml)
    : IRequest<Result<PlatformWithholdingCertificateDto>>;

/// <summary>Takes a CFDI registered by mistake out of the reconciliation, with a reason; it stays as history.</summary>
[RequiresTenantManager]
public record RemovePlatformWithholdingCertificateCommand(Guid Id, string? Reason)
    : IRequest<Result<PlatformWithholdingCertificateDto>>;

public class RegisterPlatformWithholdingCertificateCommandValidator : AbstractValidator<RegisterPlatformWithholdingCertificateCommand>
{
    public RegisterPlatformWithholdingCertificateCommandValidator()
    {
        RuleFor(x => x.Channel)
            .Must(OrderChannelRules.IsDeliveryPlatform)
            .WithMessage(DeliveryPlatformSetting.NotADeliveryPlatformMessage);
        RuleFor(x => x.Xml).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(PlatformWithholdingXml.EmptyMessage)
            .Must(xml => xml!.Length <= PlatformWithholdingXml.MaxLength).WithMessage(PlatformWithholdingXml.TooLongMessage);
    }
}

public class RemovePlatformWithholdingCertificateCommandValidator : AbstractValidator<RemovePlatformWithholdingCertificateCommand>
{
    public RemovePlatformWithholdingCertificateCommandValidator()
    {
        RuleFor(x => x.Reason).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(PlatformWithholdingCertificate.RemovalReasonRequiredMessage)
            .Must(reason => reason!.Trim().Length <= PlatformWithholdingCertificate.MaxRemovalReasonLength)
            .WithMessage(PlatformWithholdingCertificate.RemovalReasonTooLongMessage);
    }
}

public class RegisterPlatformWithholdingCertificateCommandHandler
    : IRequestHandler<RegisterPlatformWithholdingCertificateCommand, Result<PlatformWithholdingCertificateDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _clock;

    public RegisterPlatformWithholdingCertificateCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
        _clock = clock;
    }

    public async Task<Result<PlatformWithholdingCertificateDto>> Handle(
        RegisterPlatformWithholdingCertificateCommand request, CancellationToken cancellationToken)
    {
        var reading = PlatformWithholdingXml.Read(request.Xml);
        if (reading.Error is { } readError)
            return Result.Failure<PlatformWithholdingCertificateDto>(readError);
        var data = reading.Data!;

        var tenantId = _tenantService.TenantId;
        // A business with its RFC written takes only the vouchers made out to it; without one, the person's word stands.
        var tenantRfc = await _context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Rfc)
            .SingleOrDefaultAsync(cancellationToken);
        var businessRfc = tenantRfc?.Trim().ToUpperInvariant();
        if (SatCatalog.IsValidRfc(businessRfc) && !string.Equals(businessRfc, data.ReceiverRfc.Trim().ToUpperInvariant(), StringComparison.Ordinal))
            return Result.Failure<PlatformWithholdingCertificateDto>(PlatformWithholdingErrors.OtherReceiver);

        if (await RegisteredAsync(data.Uuid, cancellationToken) is { } existing)
            return Result.Failure<PlatformWithholdingCertificateDto>(PlatformWithholdingErrors.AlreadyRegistered(existing));
        // A CFDI the platform cancelled and replaced, uploaded after its replacement (a downloaded folder brings both).
        var replacement = await _context.PlatformWithholdingCertificates
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ReplacesUuid == data.Uuid && c.RemovedAt == null, cancellationToken);
        if (replacement is not null)
            return Result.Failure<PlatformWithholdingCertificateDto>(PlatformWithholdingErrors.Replaced(replacement));

        var zone = await PlatformSettlementReader.ZoneAsync(_context, tenantId, cancellationToken);
        var today = DailySalesReadModel.LocalToday(zone, _clock.UtcNow);
        PlatformWithholdingCertificate certificate;
        try
        {
            certificate = new PlatformWithholdingCertificate(tenantId, request.Channel, data, new DateOnly(today.Year, today.Month, 1));
        }
        catch (DomainException ex)
        {
            return Result.Failure<PlatformWithholdingCertificateDto>(ex.Message);
        }

        var performedBy = _currentUserService.UserName ?? "system";
        // The one it replaces stops counting in the same save, with why: the platform cancelled it.
        if (certificate.ReplacesUuid is { } replacedUuid
            && await _context.PlatformWithholdingCertificates
                .FirstOrDefaultAsync(c => c.Uuid == replacedUuid && c.RemovedAt == null, cancellationToken) is { } replaced)
        {
            replaced.Remove(PlatformWithholdingErrors.ReplacedBy(certificate), _clock.UtcNow);
            _context.AuditLogs.Add(PlatformWithholdingAudit.Removed(tenantId, replaced, performedBy));
        }

        _context.PlatformWithholdingCertificates.Add(certificate);
        _context.AuditLogs.Add(PlatformWithholdingAudit.Registered(tenantId, certificate, performedBy));
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone removed the one it replaces at the same time: nothing is saved, and the person uploads it again.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            return Result.Failure<PlatformWithholdingCertificateDto>(PlatformWithholdingErrors.Stale);
        }
        catch (DbUpdateException)
        {
            // Someone registered the same CFDI at the same time (the unique index): this one is undone, and the person is
            // told where the other one is.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            if (await RegisteredAsync(data.Uuid, cancellationToken) is { } meanwhile)
                return Result.Failure<PlatformWithholdingCertificateDto>(PlatformWithholdingErrors.AlreadyRegistered(meanwhile));
            throw;
        }

        return Result.Success(PlatformWithholdingText.ToDto(certificate));
    }

    private Task<PlatformWithholdingCertificate?> RegisteredAsync(Guid uuid, CancellationToken cancellationToken)
        => _context.PlatformWithholdingCertificates
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Uuid == uuid && c.RemovedAt == null, cancellationToken);
}

public class RemovePlatformWithholdingCertificateCommandHandler
    : IRequestHandler<RemovePlatformWithholdingCertificateCommand, Result<PlatformWithholdingCertificateDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _clock;

    public RemovePlatformWithholdingCertificateCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
        _clock = clock;
    }

    public async Task<Result<PlatformWithholdingCertificateDto>> Handle(
        RemovePlatformWithholdingCertificateCommand request, CancellationToken cancellationToken)
    {
        var certificate = await _context.PlatformWithholdingCertificates
            .SingleOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(PlatformWithholdingCertificate), request.Id);

        // Removed already (by someone else, after the person loaded the list): theirs stands, and the list is read again.
        if (certificate.IsRemoved)
            return Result.Failure<PlatformWithholdingCertificateDto>(PlatformWithholdingErrors.Stale);
        try
        {
            certificate.Remove(request.Reason, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure<PlatformWithholdingCertificateDto>(ex.Message);
        }

        _context.AuditLogs.Add(PlatformWithholdingAudit.Removed(_tenantService.TenantId, certificate, _currentUserService.UserName ?? "system"));
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone removed it after it was read here: theirs stands, with their reason.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            return Result.Failure<PlatformWithholdingCertificateDto>(PlatformWithholdingErrors.Stale);
        }

        return Result.Success(PlatformWithholdingText.ToDto(certificate));
    }
}

/// <summary>The activity entries of a CFDI de retenciones: when it is registered and when it is removed.</summary>
internal static class PlatformWithholdingAudit
{
    public const string RegisteredAction = "PlatformWithholdingCertificateRegistered";
    public const string RemovedAction = "PlatformWithholdingCertificateRemoved";

    // Neither RFC: the folio, the platform and the period say which voucher it is.
    public static AuditLog Registered(Guid tenantId, PlatformWithholdingCertificate certificate, string performedBy)
        => Create(tenantId, certificate, RegisteredAction, performedBy, new
        {
            certificate.Channel,
            certificate.Uuid,
            certificate.ReplacesUuid,
            certificate.Year,
            certificate.MonthFrom,
            certificate.MonthTo,
            certificate.OperationTotal,
            certificate.IsrWithheld,
            certificate.VatWithheld,
            certificate.TotalWithheld
        });

    public static AuditLog Removed(Guid tenantId, PlatformWithholdingCertificate certificate, string performedBy)
        => Create(tenantId, certificate, RemovedAction, performedBy, new
        {
            certificate.Channel,
            certificate.Uuid,
            certificate.Year,
            certificate.MonthFrom,
            certificate.MonthTo,
            Reason = certificate.RemovalReason
        });

    private static AuditLog Create(Guid tenantId, PlatformWithholdingCertificate certificate, string action, string performedBy, object metadata)
        => new(
            tenantId: tenantId,
            entityName: nameof(PlatformWithholdingCertificate),
            entityId: certificate.Id.ToString(),
            action: action,
            performedBy: performedBy,
            metadata: JsonSerializer.Serialize(metadata));
}
