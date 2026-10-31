using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Messaging.Campaigns;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Invoicing;

/// <summary>
/// Configuración › Facturación (Fase 28A, ADR 0045): whether the platform invoices, the business's fiscal data the invoice
/// needs, its CSD, the series of its invoices and payment complements (Fase 28C), whether its customers invoice their
/// tickets online (Fase 28F), the template its CFDI go by WhatsApp with (Fase 28M), and its accountant's email (Fase 28O).
/// </summary>
public class InvoicingSettingsDto
{
    /// <summary>Whether the platform has its PAC; without it nothing else applies.</summary>
    public bool Available { get; set; }
    /// <summary>The PAC's test environment: the invoices have no fiscal effect.</summary>
    public bool Sandbox { get; set; }
    public string Serie { get; set; } = OrderInvoice.DefaultSerie;
    /// <summary>The serie of its payment complements (Fase 28C).</summary>
    public string PaymentSerie { get; set; } = PaymentComplement.DefaultSerie;
    /// <summary>The serie of its credit notes (Fase 28D).</summary>
    public string CreditNoteSerie { get; set; } = CreditNote.DefaultSerie;
    /// <summary>The business's RFC, legal name, regime and fiscal postal code, from Mi negocio.</summary>
    public string? Rfc { get; set; }
    public string? LegalName { get; set; }
    public string? FiscalRegime { get; set; }
    public string? PostalCode { get; set; }
    /// <summary>What Mi negocio still lacks for an invoice.</summary>
    public List<string> MissingData { get; set; } = new();
    public InvoicingCertificateDto? Certificate { get; set; }
    /// <summary>Whether the business can stamp now: PAC, fiscal data and a CSD of its RFC in force.</summary>
    public bool Ready { get; set; }
    /// <summary>Whether its customers invoice their tickets online (Fase 28F, ADR 0050).</summary>
    public bool SelfInvoicing { get; set; }
    /// <summary>The address of the page where they do, «/m/{slug}/facturar».</summary>
    public string SelfInvoicePath { get; set; } = null!;
    /// <summary>Whether the business's WhatsApp is connected (Fase 28M): only then its CFDI go by WhatsApp.</summary>
    public bool WhatsAppConnected { get; set; }
    /// <summary>The template its CFDI go by WhatsApp with, while it is active and for invoices; null, TienditApp's text.</summary>
    public Guid? WhatsAppTemplateId { get; set; }
    /// <summary>Its active templates for invoices, to choose from.</summary>
    public List<InvoiceWhatsAppTemplateDto> WhatsAppTemplates { get; set; } = [];
    /// <summary>TienditApp's text, the one its CFDI go with without a template.</summary>
    public string WhatsAppDefaultText { get; set; } = CfdiWhatsApps.DefaultText;
    /// <summary>The accountant's email (Fase 28O, ADR 0060): where «Mandar al contador» sends the month's CFDI.</summary>
    public string? AccountantEmail { get; set; }
    /// <summary>Whether TienditApp sends the accountant the month before's CFDI on its own, at the start of each month.</summary>
    public bool SendsMonthToAccountant { get; set; }
}

/// <summary>A message template for the CFDI by WhatsApp (Fase 28M).</summary>
public class InvoiceWhatsAppTemplateDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Body { get; set; } = null!;
    /// <summary>The template approved in Meta; without it the text only goes within 24 hours of the customer's last message.</summary>
    public string? ProviderTemplateName { get; set; }
}

public class InvoicingCertificateDto
{
    public string Number { get; set; } = null!;
    public string Rfc { get; set; } = null!;
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset ValidTo { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    /// <summary>It is of the RFC in Mi negocio and in force now.</summary>
    public bool Usable { get; set; }
    /// <summary>It expires within 30 days: renew it in the SAT and upload the new one.</summary>
    public bool ExpiresSoon { get; set; }
    public bool Expired { get; set; }
    /// <summary>Its RFC is not the one in Mi negocio.</summary>
    public bool OtherRfc { get; set; }
}

/// <summary>The business's invoicing settings (owners and admins).</summary>
[RequiresTenantManager]
public record GetInvoicingSettingsQuery : IRequest<InvoicingSettingsDto>;

public class GetInvoicingSettingsQueryHandler : IRequestHandler<GetInvoicingSettingsQuery, InvoicingSettingsDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly IInvoicingGateway _gateway;
    private readonly IDateTimeProvider _clock;

    public GetInvoicingSettingsQueryHandler(
        IApplicationDbContext context, ICurrentTenantService tenant, IInvoicingGateway gateway, IDateTimeProvider clock)
    {
        _context = context;
        _tenant = tenant;
        _gateway = gateway;
        _clock = clock;
    }

    public Task<InvoicingSettingsDto> Handle(GetInvoicingSettingsQuery request, CancellationToken cancellationToken)
        => InvoicingSettings.ReadAsync(_context, _tenant.TenantId, _gateway, _clock.UtcNow, cancellationToken);
}

/// <summary>
/// The business uploads its CSD (owners and admins): TienditApp checks it, the PAC keeps it, and only what identifies it is
/// stored here. It replaces the one before, which the PAC then forgets. The files and the password are never kept or logged.
/// </summary>
[RequiresTenantManager]
public record UploadInvoicingCertificateCommand(string Certificate, string PrivateKey, string Password)
    : IRequest<Result<InvoicingSettingsDto>>;

public class UploadInvoicingCertificateCommandValidator : AbstractValidator<UploadInvoicingCertificateCommand>
{
    // Base64 of the largest file the reader takes.
    private const int MaxBase64Length = (SealingCertificateReader.MaxFileBytes + 2) / 3 * 4;

    public UploadInvoicingCertificateCommandValidator()
    {
        RuleFor(x => x.Certificate).NotEmpty().WithMessage("Sube el archivo .cer de tu certificado de sello digital.")
            .MaximumLength(MaxBase64Length).WithMessage(SealingCertificateReader.NotACertificateMessage);
        RuleFor(x => x.PrivateKey).NotEmpty().WithMessage("Sube el archivo .key de tu certificado de sello digital.")
            .MaximumLength(MaxBase64Length).WithMessage(SealingCertificateReader.WrongPasswordMessage);
        RuleFor(x => x.Password).NotEmpty().WithMessage("Escribe la contraseña de la llave privada.")
            .MaximumLength(SealingCertificateReader.MaxPasswordLength);
    }
}

public class UploadInvoicingCertificateCommandHandler
    : IRequestHandler<UploadInvoicingCertificateCommand, Result<InvoicingSettingsDto>>
{
    public const string OtherRfcMessage = "El certificado es del RFC {0}, y el de Mi negocio es {1}: sube el CSD del negocio.";
    public const string NoRfcInBusinessMessage = "Captura primero el RFC del negocio en Mi negocio.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IInvoicingGateway _gateway;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<UploadInvoicingCertificateCommandHandler> _logger;

    public UploadInvoicingCertificateCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser,
        IInvoicingGateway gateway,
        IDateTimeProvider clock,
        ILogger<UploadInvoicingCertificateCommandHandler> logger)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _gateway = gateway;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<InvoicingSettingsDto>> Handle(UploadInvoicingCertificateCommand request, CancellationToken cancellationToken)
    {
        if (!_gateway.IsConfigured)
            return Result.Failure<InvoicingSettingsDto>(InvoicingSettings.NotAvailableMessage);
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return Result.Failure<InvoicingSettingsDto>(InvoicingSettings.NotAvailableMessage);

        byte[] certificateFile, keyFile;
        try
        {
            certificateFile = Convert.FromBase64String(request.Certificate);
            keyFile = Convert.FromBase64String(request.PrivateKey);
        }
        catch (FormatException)
        {
            return Result.Failure<InvoicingSettingsDto>(SealingCertificateReader.NotACertificateMessage);
        }

        var read = SealingCertificateReader.Read(certificateFile, keyFile, request.Password);
        if (read.IsFailure)
            return Result.Failure<InvoicingSettingsDto>(read.Error!);
        var certificate = read.Value;

        var tenantId = _tenant.TenantId;
        var rfc = await _context.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Rfc).FirstOrDefaultAsync(cancellationToken);
        var businessRfc = rfc?.Trim().ToUpperInvariant();
        if (!SatCatalog.IsValidRfc(businessRfc))
            return Result.Failure<InvoicingSettingsDto>(NoRfcInBusinessMessage);
        if (certificate.Rfc != businessRfc)
            return Result.Failure<InvoicingSettingsDto>(string.Format(OtherRfcMessage, certificate.Rfc, businessRfc));

        if (await InvoicingSettings.CertificateUsedByAnotherBusinessAsync(_context, certificate.Number, tenantId, cancellationToken))
            return Result.Failure<InvoicingSettingsDto>(InvoicingSettings.CertificateOfAnotherBusinessMessage);

        var now = _clock.UtcNow;
        var profile = await _context.InvoicingProfiles.FirstOrDefaultAsync(cancellationToken);
        var previous = profile?.CertificateNumber;
        try
        {
            // Checked before the PAC keeps it, so an expired one never reaches it.
            (profile ?? InvoicingProfile.Create(tenantId)).UseCertificate(
                certificate.Number, certificate.Rfc, certificate.ValidFrom, certificate.ValidTo, userId, now);
        }
        catch (DomainException ex)
        {
            return Result.Failure<InvoicingSettingsDto>(ex.Message);
        }

        try
        {
            await _gateway.SaveCertificateAsync(certificateFile, keyFile, request.Password, cancellationToken);
        }
        catch (InvoicingGatewayRefusedException ex)
        {
            return Result.Failure<InvoicingSettingsDto>(ex.Message);
        }
        catch (InvoicingGatewayUnavailableException ex)
        {
            return Result.Failure<InvoicingSettingsDto>(ex.Message);
        }

        if (profile is null)
        {
            profile = InvoicingProfile.Create(tenantId);
            profile.UseCertificate(certificate.Number, certificate.Rfc, certificate.ValidFrom, certificate.ValidTo, userId, now);
            _context.InvoicingProfiles.Add(profile);
        }
        else
        {
            profile.UseCertificate(certificate.Number, certificate.Rfc, certificate.ValidFrom, certificate.ValidTo, userId, now);
        }
        InvoicingSettings.Audit(_context, tenantId, _currentUser, "InvoicingCertificateUploaded",
            new { certificate.Number, certificate.Rfc, certificate.ValidTo });
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another business saved the same CSD at the same moment (the unique index kept theirs); anything else is
            // thrown on.
            _context.ChangeTracker.Clear();
            if (await InvoicingSettings.CertificateUsedByAnotherBusinessAsync(_context, certificate.Number, tenantId, cancellationToken))
                return Result.Failure<InvoicingSettingsDto>(InvoicingSettings.CertificateOfAnotherBusinessMessage);
            throw;
        }

        // The PAC stops sealing with the one before; if it does not answer now, the new one is already the one in use.
        if (previous is not null && previous != certificate.Number)
            await ForgetAsync(previous, cancellationToken);

        return Result.Success(await InvoicingSettings.ReadAsync(_context, tenantId, _gateway, now, cancellationToken));
    }

    private async Task ForgetAsync(string number, CancellationToken cancellationToken)
    {
        try
        {
            await _gateway.RemoveCertificateAsync(number, cancellationToken);
        }
        catch (InvoicingGatewayUnavailableException)
        {
            _logger.LogWarning("The PAC kept the business's previous CSD {Number}: it could not be removed now", number);
        }
    }
}

/// <summary>The business stops invoicing with its CSD (owners and admins): the PAC forgets it; stamped invoices stay.</summary>
[RequiresTenantManager]
public record RemoveInvoicingCertificateCommand : IRequest<Result<InvoicingSettingsDto>>;

public class RemoveInvoicingCertificateCommandHandler
    : IRequestHandler<RemoveInvoicingCertificateCommand, Result<InvoicingSettingsDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IInvoicingGateway _gateway;
    private readonly IDateTimeProvider _clock;

    public RemoveInvoicingCertificateCommandHandler(
        IApplicationDbContext context, ICurrentTenantService tenant, ICurrentUserService currentUser, IInvoicingGateway gateway,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _gateway = gateway;
        _clock = clock;
    }

    public async Task<Result<InvoicingSettingsDto>> Handle(RemoveInvoicingCertificateCommand request, CancellationToken cancellationToken)
    {
        var profile = await _context.InvoicingProfiles.FirstOrDefaultAsync(cancellationToken);
        if (profile?.CertificateNumber is { } number)
        {
            if (_gateway.IsConfigured)
            {
                try
                {
                    await _gateway.RemoveCertificateAsync(number, cancellationToken);
                }
                catch (InvoicingGatewayUnavailableException ex)
                {
                    return Result.Failure<InvoicingSettingsDto>(ex.Message);
                }
            }
            profile.RemoveCertificate();
            InvoicingSettings.Audit(_context, _tenant.TenantId, _currentUser, "InvoicingCertificateRemoved", new { Number = number });
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(await InvoicingSettings.ReadAsync(_context, _tenant.TenantId, _gateway, _clock.UtcNow, cancellationToken));
    }
}

/// <summary>
/// The serie of the business's next invoices and, when given, of its next payment complements (Fase 28C) and credit notes
/// (Fase 28D; owners and admins).
/// </summary>
[RequiresTenantManager]
public record ChangeInvoiceSerieCommand(string Serie, string? PaymentSerie = null, string? CreditNoteSerie = null)
    : IRequest<Result<InvoicingSettingsDto>>;

public class ChangeInvoiceSerieCommandHandler : IRequestHandler<ChangeInvoiceSerieCommand, Result<InvoicingSettingsDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IInvoicingGateway _gateway;
    private readonly IDateTimeProvider _clock;

    public ChangeInvoiceSerieCommandHandler(
        IApplicationDbContext context, ICurrentTenantService tenant, ICurrentUserService currentUser, IInvoicingGateway gateway,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _gateway = gateway;
        _clock = clock;
    }

    public async Task<Result<InvoicingSettingsDto>> Handle(ChangeInvoiceSerieCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenant.TenantId;
        var profile = await _context.InvoicingProfiles.FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            profile = InvoicingProfile.Create(tenantId);
            _context.InvoicingProfiles.Add(profile);
        }
        try
        {
            profile.ChangeSerie(request.Serie);
            if (request.PaymentSerie is not null)
                profile.ChangePaymentSerie(request.PaymentSerie);
            if (request.CreditNoteSerie is not null)
                profile.ChangeCreditNoteSerie(request.CreditNoteSerie);
        }
        catch (DomainException ex)
        {
            return Result.Failure<InvoicingSettingsDto>(ex.Message);
        }

        InvoicingSettings.Audit(
            _context, tenantId, _currentUser, "InvoiceSerieChanged", new { profile.Serie, profile.PaymentSerie, profile.CreditNoteSerie });
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(await InvoicingSettings.ReadAsync(_context, tenantId, _gateway, _clock.UtcNow, cancellationToken));
    }
}

/// <summary>
/// Turns self-invoicing on or off (Fase 28F, ADR 0050; owners and admins). On needs the business able to invoice now; the
/// first time it creates the key of the tickets' codes. Off keeps it, so the tickets already printed work again when it is
/// turned back on; the invoices customers already have stay.
/// </summary>
[RequiresTenantManager]
public record SetSelfInvoicingCommand(bool On) : IRequest<Result<InvoicingSettingsDto>>;

public class SetSelfInvoicingCommandHandler : IRequestHandler<SetSelfInvoicingCommand, Result<InvoicingSettingsDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IInvoicingGateway _gateway;
    private readonly IDateTimeProvider _clock;

    public SetSelfInvoicingCommandHandler(
        IApplicationDbContext context, ICurrentTenantService tenant, ICurrentUserService currentUser, IInvoicingGateway gateway,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _gateway = gateway;
        _clock = clock;
    }

    public async Task<Result<InvoicingSettingsDto>> Handle(SetSelfInvoicingCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenant.TenantId;
        var now = _clock.UtcNow;
        var profile = await _context.InvoicingProfiles.FirstOrDefaultAsync(cancellationToken);
        var wasOn = profile?.SelfInvoicing ?? false;
        if (request.On)
        {
            var settings = await InvoicingSettings.ReadAsync(_context, tenantId, _gateway, now, cancellationToken);
            if (InvoicingSettings.ReadinessProblem(settings) is { } problem)
                return Result.Failure<InvoicingSettingsDto>(InvoicingSettings.SelfInvoicingNeedsInvoicing(problem));
            // Ready means it has its CSD, so its profile is there.
            profile!.TurnOnSelfInvoicing();
        }
        else
            profile?.TurnOffSelfInvoicing();

        if (profile is not null && profile.SelfInvoicing != wasOn)
        {
            InvoicingSettings.Audit(
                _context, tenantId, _currentUser, request.On ? "SelfInvoicingTurnedOn" : "SelfInvoicingTurnedOff", new { request.On });
            await _context.SaveChangesAsync(cancellationToken);
        }
        return Result.Success(await InvoicingSettings.ReadAsync(_context, tenantId, _gateway, now, cancellationToken));
    }
}

/// <summary>
/// The template the business's CFDI go by WhatsApp with (owners and admins, Fase 28M): one of its active templates for
/// invoices, approved in Meta with a document header, or TienditApp's text (null). The messages already queued keep theirs.
/// </summary>
[RequiresTenantManager]
public record SetInvoiceWhatsAppTemplateCommand(Guid? TemplateId) : IRequest<Result<InvoicingSettingsDto>>;

public class SetInvoiceWhatsAppTemplateCommandHandler : IRequestHandler<SetInvoiceWhatsAppTemplateCommand, Result<InvoicingSettingsDto>>
{
    public const string NotATemplateMessage = "Elige una plantilla activa de «Factura por WhatsApp», o el texto de TienditApp.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IInvoicingGateway _gateway;
    private readonly IDateTimeProvider _clock;

    public SetInvoiceWhatsAppTemplateCommandHandler(
        IApplicationDbContext context, ICurrentTenantService tenant, ICurrentUserService currentUser, IInvoicingGateway gateway,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _gateway = gateway;
        _clock = clock;
    }

    public async Task<Result<InvoicingSettingsDto>> Handle(SetInvoiceWhatsAppTemplateCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenant.TenantId;
        // Another business's template is not there (the query filter keeps it out).
        if (request.TemplateId is { } templateId
            && !await _context.MessageTemplates.AnyAsync(
                t => t.Id == templateId && t.IsActive && t.Purpose == MessagePurpose.CfdiDocument, cancellationToken))
            return Result.Failure<InvoicingSettingsDto>(NotATemplateMessage);

        var profile = await _context.InvoicingProfiles.FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            profile = InvoicingProfile.Create(tenantId);
            _context.InvoicingProfiles.Add(profile);
        }
        try
        {
            profile.UseWhatsAppTemplate(request.TemplateId);
        }
        catch (DomainException ex)
        {
            return Result.Failure<InvoicingSettingsDto>(ex.Message);
        }

        InvoicingSettings.Audit(_context, tenantId, _currentUser, "InvoiceWhatsAppTemplateChanged", new { request.TemplateId });
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(await InvoicingSettings.ReadAsync(_context, tenantId, _gateway, _clock.UtcNow, cancellationToken));
    }
}

/// <summary>
/// The business's accountant (owners and admins, Fase 28O, ADR 0060): their email, and whether TienditApp sends them the
/// month before's CFDI on its own at the start of each month, which needs the email. Without an email nothing goes on its
/// own; the emails already asked for keep theirs. The audit keeps what changed, never the address.
/// </summary>
[RequiresTenantManager]
public record SetAccountantCommand(string? Email, bool Monthly) : IRequest<Result<InvoicingSettingsDto>>;

public class SetAccountantCommandValidator : AbstractValidator<SetAccountantCommand>
{
    public SetAccountantCommandValidator()
    {
        RuleFor(x => x.Email)
            .Must(email => InvoiceEmailAddress.IsValid(email!.Trim())).WithMessage(InvoiceEmailAddress.InvalidMessage)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Email).NotEmpty().WithMessage(InvoicingProfile.MonthlyNeedsAccountantMessage).When(x => x.Monthly);
    }
}

public class SetAccountantCommandHandler : IRequestHandler<SetAccountantCommand, Result<InvoicingSettingsDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IInvoicingGateway _gateway;
    private readonly IDateTimeProvider _clock;

    public SetAccountantCommandHandler(
        IApplicationDbContext context, ICurrentTenantService tenant, ICurrentUserService currentUser, IInvoicingGateway gateway,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _gateway = gateway;
        _clock = clock;
    }

    public async Task<Result<InvoicingSettingsDto>> Handle(SetAccountantCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenant.TenantId;
        var profile = await _context.InvoicingProfiles.FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            profile = InvoicingProfile.Create(tenantId);
            _context.InvoicingProfiles.Add(profile);
        }
        var before = (profile.AccountantEmail, profile.SendsMonthToAccountant);
        try
        {
            profile.UseAccountant(request.Email, request.Monthly);
        }
        catch (DomainException ex)
        {
            return Result.Failure<InvoicingSettingsDto>(ex.Message);
        }

        if ((profile.AccountantEmail, profile.SendsMonthToAccountant) != before)
        {
            InvoicingSettings.Audit(_context, tenantId, _currentUser, "AccountantChanged", new
            {
                HasEmail = profile.AccountantEmail is not null,
                EmailChanged = profile.AccountantEmail != before.AccountantEmail,
                Monthly = profile.SendsMonthToAccountant
            });
            await _context.SaveChangesAsync(cancellationToken);
        }
        return Result.Success(await InvoicingSettings.ReadAsync(_context, tenantId, _gateway, _clock.UtcNow, cancellationToken));
    }
}

internal static class InvoicingSettings
{
    public const string NotAvailableMessage = "La facturación no está disponible en TienditApp por ahora.";
    public const string NoCertificateMessage =
        "Sube el certificado de sello digital (CSD) del negocio en Configuración › Facturación para facturar.";
    public const string ExpiredCertificateMessage =
        "El certificado de sello digital (CSD) del negocio venció: tramita uno nuevo en el SAT y súbelo en Configuración › Facturación.";
    public const string OtherRfcCertificateMessage =
        "El certificado de sello digital (CSD) no es del RFC de Mi negocio: sube el del negocio en Configuración › Facturación.";
    public const string CertificateOfAnotherBusinessMessage =
        "Ese certificado ya lo usa otro negocio en TienditApp: cada negocio sube el suyo. Tramita otro CSD en el SAT para este " +
        "negocio (un RFC puede tener varios).";

    /// <summary>
    /// Deliberately cross-tenant: the platform's PAC account keeps each CSD once, by its number, so two businesses with the
    /// same one would share it there, and one removing it would stop the other's invoices (or a forged certificate with
    /// another's number could take its place). Only whether another business holds that number is read, nothing of it.
    /// </summary>
    public static Task<bool> CertificateUsedByAnotherBusinessAsync(
        IApplicationDbContext context, string number, Guid tenantId, CancellationToken cancellationToken)
        => context.InvoicingProfiles
            .IgnoreQueryFilters()
            .AnyAsync(p => p.CertificateNumber == number && p.TenantId != tenantId, cancellationToken);

    public static void Audit(IApplicationDbContext context, Guid tenantId, ICurrentUserService currentUser, string action, object metadata)
        => context.AuditLogs.Add(new AuditLog(
            tenantId: tenantId,
            entityName: nameof(InvoicingProfile),
            entityId: tenantId.ToString(),
            action: action,
            performedBy: currentUser.UserName ?? "system",
            metadata: JsonSerializer.Serialize(metadata)));

    /// <summary>Self-invoicing is turned on only while the business can invoice.</summary>
    public static string SelfInvoicingNeedsInvoicing(string problem) => $"Para activar la autofactura, el negocio debe poder facturar. {problem}";

    /// <summary>Why the business cannot invoice now (the platform, Mi negocio or its CSD), or null when it can.</summary>
    public static string? ReadinessProblem(InvoicingSettingsDto settings)
    {
        if (!settings.Available)
            return NotAvailableMessage;
        if (settings.MissingData.Count > 0)
            return "Para facturar, completa en Mi negocio: " + string.Join(" ", settings.MissingData);
        return settings.Certificate switch
        {
            null => NoCertificateMessage,
            { Expired: true } => ExpiredCertificateMessage,
            { OtherRfc: true } => OtherRfcCertificateMessage,
            { Usable: false } => NoCertificateMessage,
            _ => null
        };
    }

    /// <summary>Why the business cannot seal now, or null when its CSD seals for its RFC.</summary>
    public static string? CertificateProblem(InvoicingProfile? profile, string? businessRfc, DateTimeOffset now)
    {
        if (profile is null || !profile.HasCertificate)
            return NoCertificateMessage;
        if (profile.CanSealFor(businessRfc, now))
            return null;
        return profile.CertificateValidTo <= now ? ExpiredCertificateMessage : OtherRfcCertificateMessage;
    }

    public static async Task<InvoicingSettingsDto> ReadAsync(
        IApplicationDbContext context, Guid tenantId, IInvoicingGateway gateway, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var tenant = await context.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Slug, t.Rfc, t.LegalName, t.TaxRegime, PostalCode = t.Address == null ? null : t.Address.ZipCode })
            .FirstOrDefaultAsync(cancellationToken);
        var profile = await context.InvoicingProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        var rfc = tenant?.Rfc?.Trim().ToUpperInvariant();
        var regime = SatCatalog.RegimeCode(tenant?.TaxRegime);
        var missing = new List<string>();
        if (!SatCatalog.IsValidRfc(rfc))
            missing.Add("El RFC del negocio.");
        if (string.IsNullOrWhiteSpace(tenant?.LegalName))
            missing.Add("La razón social, como aparece en la constancia de situación fiscal.");
        if (regime is null)
            missing.Add("El régimen fiscal del negocio.");
        if (!SatCatalog.IsValidPostalCode(tenant?.PostalCode?.Trim()))
            missing.Add("El código postal fiscal (5 dígitos).");

        var settings = new InvoicingSettingsDto
        {
            Available = gateway.IsConfigured,
            Sandbox = gateway.Sandbox,
            Serie = profile?.Serie ?? OrderInvoice.DefaultSerie,
            PaymentSerie = profile?.PaymentSerie ?? PaymentComplement.DefaultSerie,
            CreditNoteSerie = profile?.CreditNoteSerie ?? CreditNote.DefaultSerie,
            Rfc = rfc,
            LegalName = tenant?.LegalName?.Trim(),
            FiscalRegime = regime,
            PostalCode = tenant?.PostalCode?.Trim(),
            MissingData = missing,
            SelfInvoicing = profile?.SelfInvoicing ?? false,
            SelfInvoicePath = Invoicing.SelfInvoicing.PagePath(tenant?.Slug ?? string.Empty),
            AccountantEmail = profile?.AccountantEmail,
            SendsMonthToAccountant = profile?.SendsMonthToAccountant ?? false,
            WhatsAppConnected = await CampaignRules.IsWhatsAppConnectedAsync(context, tenantId, cancellationToken),
            WhatsAppTemplates = await context.MessageTemplates.AsNoTracking()
                .Where(t => t.IsActive && t.Purpose == MessagePurpose.CfdiDocument)
                .OrderBy(t => t.Name)
                .Select(t => new InvoiceWhatsAppTemplateDto { Id = t.Id, Name = t.Name, Body = t.Body, ProviderTemplateName = t.ProviderTemplateName })
                .ToListAsync(cancellationToken)
        };
        settings.WhatsAppTemplateId = settings.WhatsAppTemplates.Any(t => t.Id == profile?.WhatsAppTemplateId) ? profile!.WhatsAppTemplateId : null;
        if (profile is { HasCertificate: true })
        {
            settings.Certificate = new InvoicingCertificateDto
            {
                Number = profile.CertificateNumber!,
                Rfc = profile.CertificateRfc!,
                ValidFrom = profile.CertificateValidFrom!.Value,
                ValidTo = profile.CertificateValidTo!.Value,
                UploadedAt = profile.CertificateUploadedAt!.Value,
                Usable = profile.CanSealFor(rfc, now),
                ExpiresSoon = profile.CertificateExpiresSoon(now),
                Expired = profile.CertificateValidTo <= now,
                OtherRfc = profile.CertificateRfc != rfc
            };
        }
        settings.Ready = settings.Available && missing.Count == 0 && settings.Certificate is { Usable: true };
        return settings;
    }
}
