using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Domain.Tenants.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants.Commands;

[RequiresTenantManager]
public record UpdateWhatsAppConfigCommand : IRequest<Result<WhatsAppConfigDto>>
{
    public bool IsEnabled { get; init; }
    public string? AccessToken { get; init; }
    public string? PhoneNumberId { get; init; }
    public string? WabaId { get; init; }
    public string? DisplayPhoneNumber { get; init; }
}

public class UpdateWhatsAppConfigCommandValidator : AbstractValidator<UpdateWhatsAppConfigCommand>
{
    public UpdateWhatsAppConfigCommandValidator()
    {
        When(x => x.IsEnabled, () =>
        {
            RuleFor(x => x.PhoneNumberId).NotEmpty()
                .WithMessage("El Phone Number ID es requerido cuando WhatsApp está habilitado.");
            RuleFor(x => x.WabaId).NotEmpty()
                .WithMessage("El WABA ID es requerido cuando WhatsApp está habilitado.");
        });

        RuleFor(x => x.PhoneNumberId).MaximumLength(50).When(x => x.PhoneNumberId != null);
        RuleFor(x => x.WabaId).MaximumLength(50).When(x => x.WabaId != null);
        RuleFor(x => x.DisplayPhoneNumber).MaximumLength(20).When(x => x.DisplayPhoneNumber != null);
        RuleFor(x => x.AccessToken).MaximumLength(500).When(x => x.AccessToken != null);
    }
}

public class UpdateWhatsAppConfigCommandHandler : IRequestHandler<UpdateWhatsAppConfigCommand, Result<WhatsAppConfigDto>>
{
    public const string PhoneNumberIdInUseError =
        "El Phone Number ID ya está vinculado a otro negocio. Si este número te pertenece, contacta a soporte para liberarlo.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public UpdateWhatsAppConfigCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<Result<WhatsAppConfigDto>> Handle(UpdateWhatsAppConfigCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        var phoneNumberId = WhatsAppConfig.NormalizePhoneNumberId(request.PhoneNumberId);

        if (phoneNumberId is not null
            && await IsPhoneNumberIdUsedByAnotherTenantAsync(phoneNumberId, tenantId, cancellationToken))
            return Result.Failure<WhatsAppConfigDto>(PhoneNumberIdInUseError);

        var config = await _context.WhatsAppConfigs
            .FirstOrDefaultAsync(w => w.TenantId == tenantId, cancellationToken);

        if (config is null)
        {
            config = new WhatsAppConfig(tenantId);
            _context.WhatsAppConfigs.Add(config);
        }

        // Validate: if enabling and no existing token, require one
        if (request.IsEnabled && string.IsNullOrEmpty(request.AccessToken) && string.IsNullOrEmpty(config.AccessToken))
            return Result.Failure<WhatsAppConfigDto>("Se requiere un Access Token para habilitar WhatsApp.");

        // Auto-generate verify token if none exists
        var verifyToken = string.IsNullOrEmpty(config.VerifyToken)
            ? Guid.NewGuid().ToString("N")[..24]
            : null;

        config.Update(
            request.IsEnabled,
            request.AccessToken,
            phoneNumberId,
            request.WabaId,
            request.DisplayPhoneNumber,
            verifyToken);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (phoneNumberId is not null)
        {
            // The unique index on PhoneNumberId settles concurrent claims of the same number.
            if (!await IsPhoneNumberIdUsedByAnotherTenantAsync(phoneNumberId, tenantId, cancellationToken))
                throw;

            return Result.Failure<WhatsAppConfigDto>(PhoneNumberIdInUseError);
        }

        return Result.Success(new WhatsAppConfigDto
        {
            Id = config.Id,
            TenantId = config.TenantId,
            IsEnabled = config.IsEnabled,
            PhoneNumberId = config.PhoneNumberId,
            WabaId = config.WabaId,
            DisplayPhoneNumber = config.DisplayPhoneNumber,
            HasAccessToken = !string.IsNullOrEmpty(config.AccessToken),
            VerifyToken = config.VerifyToken
        });
    }

    // Deliberately cross-tenant: the phone number ID routes anonymous webhooks, so it must
    // map to a single tenant. Only existence is checked; no data of the other tenant is read.
    private Task<bool> IsPhoneNumberIdUsedByAnotherTenantAsync(
        string phoneNumberId, Guid tenantId, CancellationToken cancellationToken)
        => _context.WhatsAppConfigs
            .IgnoreQueryFilters()
            .AnyAsync(w => w.PhoneNumberId == phoneNumberId && w.TenantId != tenantId, cancellationToken);
}
