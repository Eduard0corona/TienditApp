namespace FoodParkDigital.Application.Common.Interfaces;

public record SignInResultDto(
    bool Succeeded,
    bool IsLockedOut = false,
    bool IsNotAllowed = false,
    bool RequiresTwoFactor = false);

public interface ISignInService
{
    Task<SignInResultDto> CheckPasswordSignInAsync(Guid userId, string password, bool lockoutOnFailure);
    Task<SignInResultDto> TwoFactorAuthenticatorSignInAsync(string code, bool isPersistent, bool rememberClient);
    Task<SignInResultDto> TwoFactorRecoveryCodeSignInAsync(string recoveryCode);
}
