using System.Security.Cryptography;
using System.Text;

namespace FoodParkDigital.Application.Identity;

/// <summary>What an access token carries about its session, so that the API can tell when it no longer counts.</summary>
public static class SessionClaims
{
    /// <summary>The session (sign-in) of the token: <see cref="Domain.Identity.Entities.RefreshToken.SessionId"/>.</summary>
    public const string SessionId = "session_id";

    /// <summary>Fingerprint of the account's security stamp when the token was issued.</summary>
    public const string SecurityStamp = "stamp";

    /// <summary>
    /// The stamp also seeds the codes sent by SMS, and anyone holding a token can read it: the token carries only a
    /// hash of the stamp.
    /// </summary>
    public static string StampFingerprint(string? securityStamp) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(securityStamp ?? string.Empty)))[..32];
}
