using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.DeliveryPlatforms;

/// <summary>
/// Turns a delivery platform on or off and saves the terms of its contract (owners and admins). Orders registered
/// before keep the terms they copied; only the next ones take the new terms. Answers the whole list, as the screen shows it.
/// </summary>
/// <param name="Revision">The revision the person saw: 0 for a platform never saved.</param>
[RequiresTenantManager]
public record SaveDeliveryPlatformCommand(
    OrderChannel Channel,
    int Revision,
    bool IsActive,
    decimal CommissionPercent,
    PlatformCommissionBase CommissionBase,
    decimal CommissionVatPercent,
    decimal IsrWithholdingPercent,
    decimal VatWithholdingPercent)
    : IRequest<Result<DeliveryPlatformsDto>>;

public static class DeliveryPlatformErrors
{
    /// <summary>
    /// Someone saved the platform since the person loaded it, or saved its first terms at the same time: the same text
    /// as any other change saved at the same time (409 <c>concurrency_conflict</c>). The client reads it again.
    /// </summary>
    public const string Stale = "Alguien más modificó este registro al mismo tiempo. Recarga e intenta de nuevo.";
}

public class SaveDeliveryPlatformCommandValidator : AbstractValidator<SaveDeliveryPlatformCommand>
{
    public const string ReloadMessage = "Recarga e intenta de nuevo.";

    public SaveDeliveryPlatformCommandValidator()
    {
        RuleFor(x => x.Channel)
            .Must(OrderChannelRules.IsDeliveryPlatform)
            .WithMessage(DeliveryPlatformSetting.NotADeliveryPlatformMessage);
        RuleFor(x => x.Revision).GreaterThanOrEqualTo(0).WithMessage(ReloadMessage);
        RuleFor(x => x.CommissionPercent).Cascade(CascadeMode.Stop)
            .InclusiveBetween(0m, PlatformTerms.MaxCommissionPercent).WithMessage(PlatformTerms.CommissionMessage)
            .PrecisionScale(5, 2, ignoreTrailingZeros: true).WithMessage(PlatformTerms.CommissionMessage);
        RuleFor(x => x.CommissionBase).IsInEnum().WithMessage(PlatformTerms.CommissionBaseMessage);
        // The platforms invoice their commission with an IVA rate a business can have: general, border or zero.
        RuleFor(x => x.CommissionVatPercent).Must(VatRateRules.IsAllowed).WithMessage(VatRateRules.InvalidRateMessage);
        RuleFor(x => x.IsrWithholdingPercent).Cascade(CascadeMode.Stop)
            .InclusiveBetween(0m, PlatformTerms.MaxIsrWithholdingPercent).WithMessage(PlatformTerms.IsrWithholdingMessage)
            .PrecisionScale(5, 2, ignoreTrailingZeros: true).WithMessage(PlatformTerms.IsrWithholdingMessage);
        RuleFor(x => x.VatWithholdingPercent).Cascade(CascadeMode.Stop)
            .InclusiveBetween(0m, PlatformTerms.MaxVatWithholdingPercent).WithMessage(PlatformTerms.VatWithholdingMessage)
            .PrecisionScale(5, 2, ignoreTrailingZeros: true).WithMessage(PlatformTerms.VatWithholdingMessage);
    }
}

public class SaveDeliveryPlatformCommandHandler : IRequestHandler<SaveDeliveryPlatformCommand, Result<DeliveryPlatformsDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;

    public SaveDeliveryPlatformCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
    }

    public async Task<Result<DeliveryPlatformsDto>> Handle(SaveDeliveryPlatformCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        PlatformTerms terms;
        try
        {
            terms = new PlatformTerms(
                request.CommissionPercent,
                request.CommissionBase,
                request.CommissionVatPercent,
                request.IsrWithholdingPercent,
                request.VatWithholdingPercent);
        }
        catch (DomainException ex)
        {
            return Result.Failure<DeliveryPlatformsDto>(ex.Message);
        }

        var setting = await _context.DeliveryPlatformSettings
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Channel == request.Channel, cancellationToken);
        DeliveryPlatformAudit.Terms? previous = null;
        if (setting is null)
        {
            // The first save of the platform is made from the defaults the person saw.
            if (request.Revision != 0)
                return Result.Failure<DeliveryPlatformsDto>(DeliveryPlatformErrors.Stale);

            setting = new DeliveryPlatformSetting(tenantId, request.Channel, request.IsActive, terms);
            _context.DeliveryPlatformSettings.Add(setting);
        }
        else
        {
            if (setting.Revision != request.Revision)
                return Result.Failure<DeliveryPlatformsDto>(DeliveryPlatformErrors.Stale);

            previous = DeliveryPlatformAudit.Describe(setting);
            // Nothing changed: nothing is saved or recorded.
            if (!setting.Update(request.IsActive, terms))
                return Result.Success(await DeliveryPlatformsReader.LoadAsync(_context, tenantId, cancellationToken));
        }

        // The terms decide the commission and the withholdings of every order of the platform: who changed them, and
        // from what, stays recorded in the same SaveChanges.
        _context.AuditLogs.Add(DeliveryPlatformAudit.Create(tenantId, setting, previous, _currentUserService.UserName ?? "system"));
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone saved the platform after it was read here: this edit was made over an older revision. It is undone
            // whole, its activity entry included.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            return Result.Failure<DeliveryPlatformsDto>(DeliveryPlatformErrors.Stale);
        }
        catch (DbUpdateException) when (previous is null)
        {
            // Someone saved the first terms of the platform at the same time (IX_DeliveryPlatformSettings_TenantId_Channel):
            // this copy is undone whole, and the person reloads to see what the other one saved.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            if (await _context.DeliveryPlatformSettings.AnyAsync(x => x.TenantId == tenantId && x.Channel == request.Channel, cancellationToken))
                return Result.Failure<DeliveryPlatformsDto>(DeliveryPlatformErrors.Stale);
            throw;
        }

        return Result.Success(await DeliveryPlatformsReader.LoadAsync(_context, tenantId, cancellationToken));
    }
}

/// <summary>The activity entry of a change of a platform: its terms before (none for the first save) and after.</summary>
internal static class DeliveryPlatformAudit
{
    public const string Updated = "DeliveryPlatformUpdated";

    internal sealed record Terms(
        bool IsActive,
        decimal CommissionPercent,
        PlatformCommissionBase CommissionBase,
        decimal CommissionVatPercent,
        decimal IsrWithholdingPercent,
        decimal VatWithholdingPercent,
        int Revision);

    public static Terms Describe(DeliveryPlatformSetting setting) => new(
        setting.IsActive,
        setting.CommissionPercent,
        setting.CommissionBase,
        setting.CommissionVatPercent,
        setting.IsrWithholdingPercent,
        setting.VatWithholdingPercent,
        setting.Revision);

    public static AuditLog Create(Guid tenantId, DeliveryPlatformSetting setting, Terms? previous, string performedBy)
        => new(
            tenantId: tenantId,
            entityName: nameof(DeliveryPlatformSetting),
            entityId: setting.Id.ToString(),
            action: Updated,
            performedBy: performedBy,
            metadata: JsonSerializer.Serialize(new { setting.Channel, Previous = previous, New = Describe(setting) }));
}
