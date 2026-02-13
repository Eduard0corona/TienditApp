namespace FoodParkDigital.Application.Common.Interfaces;

public record TwoFactorInfo(string QrCodeUrl, string ManualEntryKey);

public interface ITwoFactorService
{
    TwoFactorInfo GenerateQrCodeUri(string email, string authenticatorKey);
}
