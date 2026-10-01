namespace FoodParkDigital.Application.Common.Interfaces;

public record SignInResultDto(
    bool Succeeded,
    bool IsLockedOut = false,
    bool IsNotAllowed = false,
    bool RequiresTwoFactor = false);

public interface ISignInService
{
    Task<SignInResultDto> CheckPasswordSignInAsync(Guid userId, string password, bool lockoutOnFailure);
}
