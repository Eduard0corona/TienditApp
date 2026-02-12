using FoodParkDigital.Domain.Identity.Entities;

namespace FoodParkDigital.Application.Common.Interfaces;

public interface IJwtTokenService
{
    string GenerateAccessToken(ApplicationUser user);
    RefreshToken GenerateRefreshToken(Guid userId);
}
