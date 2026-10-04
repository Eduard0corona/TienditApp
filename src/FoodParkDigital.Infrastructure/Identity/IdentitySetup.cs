using FoodParkDigital.Application.Employees;
using FoodParkDigital.Domain.Identity.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.Infrastructure.Identity;

/// <summary>Token providers and messages of Identity, shared by the API and the tests that run real Identity.</summary>
public static class IdentitySetup
{
    public static IdentityBuilder AddTienditAppIdentityDefaults(this IdentityBuilder builder) => builder
        .AddDefaultTokenProviders()
        .AddTokenProvider<InvitationTokenProvider>(EmployeeInvitations.TokenProvider)
        .AddErrorDescriber<SpanishIdentityErrorDescriber>();
}

/// <summary>An invitation lasts a week (a password reset link, one day): people open it when they can.</summary>
public sealed class InvitationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public InvitationTokenProviderOptions()
    {
        Name = EmployeeInvitations.TokenProvider;
        TokenLifespan = TimeSpan.FromDays(7);
    }
}

/// <summary>Its own provider so an invitation link only accepts the invitation, never resets a password.</summary>
public sealed class InvitationTokenProvider : DataProtectorTokenProvider<ApplicationUser>
{
    public InvitationTokenProvider(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<InvitationTokenProviderOptions> options,
        ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
        : base(dataProtectionProvider, options, logger)
    {
    }
}

/// <summary>The messages of Identity that reach people (passwords, duplicated accounts, expired links), in Spanish.</summary>
public sealed class SpanishIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => Error(nameof(DefaultError), "Ocurrió un error. Intenta de nuevo.");
    public override IdentityError ConcurrencyFailure() => Error(nameof(ConcurrencyFailure), "La cuenta cambió al mismo tiempo en otro lugar. Intenta de nuevo.");
    public override IdentityError PasswordMismatch() => Error(nameof(PasswordMismatch), "La contraseña no es correcta.");
    public override IdentityError InvalidToken() => Error(nameof(InvalidToken), "El enlace o el código no es válido o ya expiró.");
    public override IdentityError InvalidUserName(string? userName) => Error(nameof(InvalidUserName), "El correo tiene caracteres que no se permiten.");
    public override IdentityError InvalidEmail(string? email) => Error(nameof(InvalidEmail), "El correo no es válido.");
    public override IdentityError DuplicateUserName(string userName) => Error(nameof(DuplicateUserName), "Ya existe una cuenta con ese correo.");
    public override IdentityError DuplicateEmail(string email) => Error(nameof(DuplicateEmail), "Ya existe una cuenta con ese correo.");
    public override IdentityError UserAlreadyHasPassword() => Error(nameof(UserAlreadyHasPassword), "La cuenta ya tiene contraseña.");
    public override IdentityError UserLockoutNotEnabled() => Error(nameof(UserLockoutNotEnabled), "La cuenta no se puede bloquear.");
    public override IdentityError PasswordTooShort(int length) => Error(nameof(PasswordTooShort), $"La contraseña debe tener al menos {length} caracteres.");
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Error(nameof(PasswordRequiresUniqueChars), $"La contraseña debe tener al menos {uniqueChars} caracteres distintos.");
    public override IdentityError PasswordRequiresNonAlphanumeric() => Error(nameof(PasswordRequiresNonAlphanumeric), "La contraseña debe tener al menos un símbolo.");
    public override IdentityError PasswordRequiresDigit() => Error(nameof(PasswordRequiresDigit), "La contraseña debe tener al menos un número.");
    public override IdentityError PasswordRequiresLower() => Error(nameof(PasswordRequiresLower), "La contraseña debe tener al menos una minúscula.");
    public override IdentityError PasswordRequiresUpper() => Error(nameof(PasswordRequiresUpper), "La contraseña debe tener al menos una mayúscula.");
    public override IdentityError RecoveryCodeRedemptionFailed() => Error(nameof(RecoveryCodeRedemptionFailed), "El código de recuperación no es válido.");

    private static IdentityError Error(string code, string description) => new() { Code = code, Description = description };
}
