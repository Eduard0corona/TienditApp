using System.Text;
using System.Web;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Infrastructure.Identity;
using Google.Authenticator;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.Infrastructure.Services;

public class TwoFactorService : ITwoFactorService
{
    private readonly JwtSettings _settings;
    private const string AuthenticatorUriFormat = "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6";

    public TwoFactorService(IOptions<JwtSettings> settings)
    {
        _settings = settings.Value;
    }

    public TwoFactorInfo GenerateQrCodeUri(string email, string authenticatorKey)
    {
        var issuer = _settings.Issuer ?? "FoodParkDigital";

        var manualEntryKey = FormatKeyForManualEntry(authenticatorKey);

        var twoFactorAuthenticator = new TwoFactorAuthenticator();
        var setupInfo = twoFactorAuthenticator.GenerateSetupCode(
            issuer,
            email,
            authenticatorKey,
            false,
            3);

        var qrCodeUrl = setupInfo.QrCodeSetupImageUrl;

        return new TwoFactorInfo(qrCodeUrl, manualEntryKey);
    }

    private static string FormatKeyForManualEntry(string key)
    {
        var sb = new StringBuilder();
        var currentPosition = 0;

        while (currentPosition + 4 < key.Length)
        {
            sb.Append(key.AsSpan(currentPosition, 4)).Append(' ');
            currentPosition += 4;
        }

        if (currentPosition < key.Length)
        {
            sb.Append(key.AsSpan(currentPosition));
        }

        return sb.ToString().TrimEnd();
    }
}
