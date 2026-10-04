using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Domain.Identity.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands;

[RequiresAuthentication]
public record UpdateOwnerWhatsAppConfigCommand : IRequest<Result<OwnerWhatsAppConfigDto>>
{
    public bool IsEnabled { get; init; }
    public bool UseForOtp { get; init; } = true;
    public string? AccessToken { get; init; }
    public string? PhoneNumberId { get; init; }
    public string? WabaId { get; init; }
    public string? DisplayPhoneNumber { get; init; }
    public string? OtpTemplateName { get; init; }
    public string? OtpTemplateLanguageCode { get; init; }
}

public class UpdateOwnerWhatsAppConfigCommandValidator : AbstractValidator<UpdateOwnerWhatsAppConfigCommand>
{
    public UpdateOwnerWhatsAppConfigCommandValidator()
    {
        RuleFor(x => x.PhoneNumberId).MaximumLength(50).When(x => x.PhoneNumberId is not null);
        RuleFor(x => x.WabaId).MaximumLength(50).When(x => x.WabaId is not null);
        RuleFor(x => x.DisplayPhoneNumber).MaximumLength(20).When(x => x.DisplayPhoneNumber is not null);
        RuleFor(x => x.AccessToken).MaximumLength(500).When(x => x.AccessToken is not null);
        RuleFor(x => x.OtpTemplateName).MaximumLength(100).When(x => x.OtpTemplateName is not null);
        RuleFor(x => x.OtpTemplateLanguageCode).MaximumLength(10).When(x => x.OtpTemplateLanguageCode is not null);

        When(x => x.IsEnabled, () =>
        {
            RuleFor(x => x.PhoneNumberId).NotEmpty()
                .WithMessage("El Phone Number ID es requerido cuando WhatsApp está habilitado.");
            RuleFor(x => x.WabaId).NotEmpty()
                .WithMessage("El WABA ID es requerido cuando WhatsApp está habilitado.");
        });
    }
}

public class UpdateOwnerWhatsAppConfigCommandHandler
    : IRequestHandler<UpdateOwnerWhatsAppConfigCommand, Result<OwnerWhatsAppConfigDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public UpdateOwnerWhatsAppConfigCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<OwnerWhatsAppConfigDto>> Handle(
        UpdateOwnerWhatsAppConfigCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUserService.Role is not ("TENANT_ADMIN" or "SUPER_ADMIN"))
            return Result.Failure<OwnerWhatsAppConfigDto>("Solo un owner o super admin puede configurar WhatsApp personal.");

        if (!Guid.TryParse(_currentUserService.UserId, out var userId))
            return Result.Failure<OwnerWhatsAppConfigDto>("Usuario autenticado invalido.");

        var config = await _context.OwnerWhatsAppConfigs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        if (config is null)
        {
            config = new OwnerWhatsAppConfig(userId);
            _context.OwnerWhatsAppConfigs.Add(config);
        }

        if (request.IsEnabled
            && string.IsNullOrWhiteSpace(request.AccessToken)
            && string.IsNullOrWhiteSpace(config.AccessToken))
        {
            return Result.Failure<OwnerWhatsAppConfigDto>("Se requiere un Access Token para habilitar WhatsApp.");
        }

        config.Update(
            request.IsEnabled,
            request.UseForOtp,
            request.AccessToken,
            request.PhoneNumberId,
            request.WabaId,
            request.DisplayPhoneNumber,
            request.OtpTemplateName,
            request.OtpTemplateLanguageCode);

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new OwnerWhatsAppConfigDto
        {
            Id = config.Id,
            UserId = config.UserId,
            IsEnabled = config.IsEnabled,
            UseForOtp = config.UseForOtp,
            PhoneNumberId = config.PhoneNumberId,
            WabaId = config.WabaId,
            DisplayPhoneNumber = config.DisplayPhoneNumber,
            OtpTemplateName = config.OtpTemplateName,
            OtpTemplateLanguageCode = config.OtpTemplateLanguageCode,
            HasAccessToken = !string.IsNullOrWhiteSpace(config.AccessToken)
        });
    }
}
