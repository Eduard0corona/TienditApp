using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Tenants.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants.Queries;

[RequiresTenantManager]
public record GetWhatsAppConfigQuery : IRequest<Result<WhatsAppConfigDto>>;

public class GetWhatsAppConfigQueryHandler : IRequestHandler<GetWhatsAppConfigQuery, Result<WhatsAppConfigDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetWhatsAppConfigQueryHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<Result<WhatsAppConfigDto>> Handle(GetWhatsAppConfigQuery request, CancellationToken cancellationToken)
    {
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
            HasAccessToken = !string.IsNullOrEmpty(config.AccessToken),
            VerifyToken = config.VerifyToken
        });
    }
}
