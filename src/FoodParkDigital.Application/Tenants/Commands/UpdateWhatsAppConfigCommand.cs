using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Domain.Tenants.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants.Commands;

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
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICurrentTenantService _tenantService;

    public UpdateWhatsAppConfigCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        ICurrentTenantService tenantService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _tenantService = tenantService;
    }

    public async Task<Result<WhatsAppConfigDto>> Handle(UpdateWhatsAppConfigCommand request, CancellationToken cancellationToken)
    {
        if (_currentUserService.Role != "TENANT_ADMIN")
            return Result.Failure<WhatsAppConfigDto>("Solo el administrador puede configurar WhatsApp.");

        var tenantId = _tenantService.TenantId;
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

        config.Update(
            request.IsEnabled,
            request.AccessToken,
            request.PhoneNumberId,
            request.WabaId,
            request.DisplayPhoneNumber);

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new WhatsAppConfigDto
        {
            Id = config.Id,
            TenantId = config.TenantId,
            IsEnabled = config.IsEnabled,
            PhoneNumberId = config.PhoneNumberId,
            WabaId = config.WabaId,
            DisplayPhoneNumber = config.DisplayPhoneNumber,
            HasAccessToken = !string.IsNullOrEmpty(config.AccessToken)
        });
    }
}
