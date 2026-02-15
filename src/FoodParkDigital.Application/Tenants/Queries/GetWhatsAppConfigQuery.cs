using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Tenants.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants.Queries;

public record GetWhatsAppConfigQuery : IRequest<Result<WhatsAppConfigDto>>;

public class GetWhatsAppConfigQueryHandler : IRequestHandler<GetWhatsAppConfigQuery, Result<WhatsAppConfigDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICurrentTenantService _tenantService;

    public GetWhatsAppConfigQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        ICurrentTenantService tenantService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _tenantService = tenantService;
    }

    public async Task<Result<WhatsAppConfigDto>> Handle(GetWhatsAppConfigQuery request, CancellationToken cancellationToken)
    {
        if (_currentUserService.Role != "TENANT_ADMIN")
            return Result.Failure<WhatsAppConfigDto>("Solo el administrador puede ver la configuración de WhatsApp.");

        var tenantId = _tenantService.TenantId;
        var config = await _context.WhatsAppConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.TenantId == tenantId, cancellationToken);

        if (config is null)
        {
            return Result.Success(new WhatsAppConfigDto
            {
                TenantId = tenantId,
                IsEnabled = false,
                HasAccessToken = false
            });
        }

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
