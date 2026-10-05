using FoodParkDigital.Domain.Identity.Entities;
using Microsoft.AspNetCore.Identity;

namespace FoodParkDigital.Infrastructure.Identity;

/// <summary>
/// Refuses the passwords tried first when guessing: the most common ones that pass the length and character rules,
/// and those containing the person's email, their name or the name of the app.
/// </summary>
public sealed class GuessablePasswordValidator : IPasswordValidator<ApplicationUser>
{
    private const int ShortestPersonalWord = 4;

    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password1", "Password12", "Password123", "Passw0rd", "Passw0rd1", "Qwerty12", "Qwerty123", "Qwerty1234",
        "Qwertyuiop1", "Abc12345", "Abc123456", "Abcd1234", "Abcde123", "Abcdefg1", "Aa123456", "Aa12345678",
        "Aa111111", "Qwe12345", "Asdf1234", "Asdfgh123", "Zxcv1234", "Zaq12wsx", "Admin123", "Admin1234",
        "Administrador1", "Welcome1", "Welcome123", "Bienvenido1", "Bienvenido123", "Contraseña1", "Contrasena1",
        "Contraseña123", "Contrasena123", "Mexico123", "Mexico1234", "Mexico2024", "Mexico2025", "Mexico2026",
        "Hola1234", "Hola12345", "Holamundo1", "Iloveyou1", "Teamo123", "Teamo1234", "Usuario1", "Usuario123",
        "Prueba123", "Prueba1234", "Test1234", "Test12345", "Temporal1", "Temporal123", "Cambiar123", "Cambiame1",
        "Cambiame123", "Changeme1", "Changeme123", "Letmein1", "Sunshine1", "Princess1", "Monkey123", "Dragon123",
        "Football1", "Futbol123", "America1", "Chivas123", "Superman1", "Batman123", "Master123", "Tienda123",
        "Tienda1234", "Negocio123", "Ventas123", "Ventas2025", "Fonda1234", "Taqueria1"
    };

    private static readonly string[] AppNames = ["tienditapp", "tiendita", "foodpark"];

    public const string Message = "Esa contraseña es fácil de adivinar: no uses tu correo, tu nombre ni una contraseña común.";

    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        if (string.IsNullOrEmpty(password))
            return Task.FromResult(IdentityResult.Success);

        return Task.FromResult(IsGuessable(password, user)
            ? IdentityResult.Failed(new IdentityError { Code = "PasswordGuessable", Description = Message })
            : IdentityResult.Success);
    }

    internal static bool IsGuessable(string password, ApplicationUser user)
    {
        if (Common.Contains(password))
            return true;

        var emailName = user.Email?.Split('@')[0];
        return new[] { emailName, user.FirstName, user.LastName }
            .Concat(AppNames)
            .Any(word => word is { Length: >= ShortestPersonalWord }
                && password.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}
