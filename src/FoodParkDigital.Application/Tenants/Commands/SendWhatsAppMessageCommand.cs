using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;

namespace FoodParkDigital.Application.Tenants.Commands;

[RequiresPermission(TenantPermission.ManageWhatsApp)]
public record SendWhatsAppMessageCommand : IRequest<Result>
{
    public string PhoneNumber { get; init; } = null!;
    public string Message { get; init; } = null!;
}

public class SendWhatsAppMessageCommandValidator : AbstractValidator<SendWhatsAppMessageCommand>
{
    public SendWhatsAppMessageCommandValidator()
    {
        RuleFor(x => x.PhoneNumber).NotEmpty()
            .Matches(@"^\+[1-9]\d{6,14}$")
            .WithMessage("El teléfono debe estar en formato E.164 (ej: +5215512345678).");
        RuleFor(x => x.Message).NotEmpty()
            .MaximumLength(4096)
            .WithMessage("El mensaje no puede exceder 4096 caracteres.");
    }
}

public class SendWhatsAppMessageCommandHandler : IRequestHandler<SendWhatsAppMessageCommand, Result>
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly ICurrentTenantService _tenantService;

    public SendWhatsAppMessageCommandHandler(
        IWhatsAppService whatsAppService,
        ICurrentTenantService tenantService)
    {
        _whatsAppService = whatsAppService;
        _tenantService = tenantService;
    }

    public async Task<Result> Handle(SendWhatsAppMessageCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        return await _whatsAppService.SendTextMessageAsync(
            tenantId, request.PhoneNumber, request.Message, cancellationToken);
    }
}
