namespace FoodParkDigital.Domain.Identity.Enums;

/// <summary>Why a refresh token stopped working. Stored as its number.</summary>
public enum RefreshTokenRevocationReason
{
    /// <summary>Replaced by the next token of its session: presenting it again means it was copied.</summary>
    Rotated = 1,

    /// <summary>The person signed out on that device.</summary>
    SignedOut = 2,

    /// <summary>A replaced token of the session was presented again, so the whole session ended.</summary>
    ReuseDetected = 3,

    /// <summary>The password changed: every session of the account ended.</summary>
    PasswordChanged = 4,

    /// <summary>The password was recovered: every session of the account ended.</summary>
    PasswordReset = 5
}
