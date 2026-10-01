using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Identity.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FoodParkDigital.Infrastructure.Identity;

/// <summary>
/// El desafío es un JWT firmado con una clave derivada y con audiencia propia, por lo que nunca
/// valida como access token. Su huella (jti + security stamp) se guarda en AspNetUserTokens:
/// así es de un solo uso y deja de valer si cambia el security stamp (contraseña, 2FA, etc.).
/// </summary>
public class TwoFactorLoginChallengeService : ITwoFactorLoginChallengeService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private const string LoginProvider = "[FoodParkDigital]";
    private const string TokenName = "TwoFactorLoginChallenge";
    private const string TokenUseClaim = "token_use";
    private const string TokenUse = "2fa_challenge";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly JwtSettings _settings;
    private readonly SymmetricSecurityKey _signingKey;

    public TwoFactorLoginChallengeService(
        UserManager<ApplicationUser> userManager,
        IDateTimeProvider dateTimeProvider,
        IOptions<JwtSettings> settings)
    {
        _userManager = userManager;
        _dateTimeProvider = dateTimeProvider;
        _settings = settings.Value;
        _signingKey = new SymmetricSecurityKey(HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(_settings.Secret),
            outputLength: 32,
            info: Encoding.UTF8.GetBytes("FoodParkDigital.TwoFactorLoginChallenge.v1")));
    }

    private string Audience => $"{_settings.Audience}.{TokenUse}";

    public async Task<string?> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var challengeId = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

        var fingerprint = await ComputeFingerprintAsync(user, challengeId);
        var stored = await _userManager.SetAuthenticationTokenAsync(user, LoginProvider, TokenName, fingerprint);
        if (!stored.Succeeded)
            return null;

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, challengeId),
                new Claim(TokenUseClaim, TokenUse)
            ],
            expires: _dateTimeProvider.UtcNow.Add(Lifetime).UtcDateTime,
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<ApplicationUser?> ValidateAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        JwtSecurityToken challenge;
        try
        {
            new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(
                token,
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = _settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = _signingKey,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    LifetimeValidator = (_, expires, _, _) => expires > _dateTimeProvider.UtcNow.UtcDateTime
                },
                out var validatedToken);
            challenge = (JwtSecurityToken)validatedToken;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return null;
        }

        var tokenUse = challenge.Claims.FirstOrDefault(c => c.Type == TokenUseClaim)?.Value;
        if (tokenUse != TokenUse || string.IsNullOrEmpty(challenge.Id) || !Guid.TryParse(challenge.Subject, out var userId))
            return null;

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return null;

        var storedFingerprint = await _userManager.GetAuthenticationTokenAsync(user, LoginProvider, TokenName);
        if (storedFingerprint is null)
            return null;

        var expectedFingerprint = await ComputeFingerprintAsync(user, challenge.Id);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(storedFingerprint),
            Encoding.UTF8.GetBytes(expectedFingerprint))
            ? user
            : null;
    }

    public async Task<bool> ConsumeAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        if (await _userManager.GetAuthenticationTokenAsync(user, LoginProvider, TokenName) is null)
            return false;

        // El ConcurrencyStamp del usuario hace que, si dos peticiones consumen el mismo desafío, solo una gane.
        var removed = await _userManager.RemoveAuthenticationTokenAsync(user, LoginProvider, TokenName);
        return removed.Succeeded;
    }

    private async Task<string> ComputeFingerprintAsync(ApplicationUser user, string challengeId)
    {
        var securityStamp = await _userManager.GetSecurityStampAsync(user);
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes($"{challengeId}:{securityStamp}")));
    }
}
