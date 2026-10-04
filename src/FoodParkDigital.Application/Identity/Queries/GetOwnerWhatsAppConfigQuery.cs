using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Identity.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Queries;

[RequiresAuthentication]
public record GetOwnerWhatsAppConfigQuery : IRequest<Result<OwnerWhatsAppConfigDto>>;

public class GetOwnerWhatsAppConfigQueryHandler
    : IRequestHandler<GetOwnerWhatsAppConfigQuery, Result<OwnerWhatsAppConfigDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetOwnerWhatsAppConfigQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<OwnerWhatsAppConfigDto>> Handle(
        GetOwnerWhatsAppConfigQuery request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var userId))
            return Result.Failure<OwnerWhatsAppConfigDto>("Usuario autenticado invalido.");

        var config = await _context.OwnerWhatsAppConfigs
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        if (config is null)
        {
            return Result.Success(new OwnerWhatsAppConfigDto
            {
                UserId = userId,
                IsEnabled = false,
                UseForOtp = true,
                OtpTemplateName = "otp_verification",
                OtpTemplateLanguageCode = "es_MX",
                HasAccessToken = false
            });
        }

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
