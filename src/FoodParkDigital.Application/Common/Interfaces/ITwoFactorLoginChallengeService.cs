using FoodParkDigital.Domain.Identity.Entities;

namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// Desafío intermedio del login con 2FA. Se emite solo tras validar la contraseña, es de corta vida,
/// de un solo uso y queda ligado al usuario y a su security stamp.
/// </summary>
public interface ITwoFactorLoginChallengeService
{
    /// <summary>
    /// Emite un desafío para el usuario y reemplaza cualquier desafío previo.
    /// Devuelve <c>null</c> si no se pudo registrar.
    /// </summary>
    Task<string?> CreateAsync(ApplicationUser user, CancellationToken cancellationToken);

    /// <summary>
    /// Devuelve el usuario del desafío si el token es auténtico, está vigente, es el desafío activo
    /// del usuario y su security stamp no cambió; en otro caso, <c>null</c>.
    /// </summary>
    Task<ApplicationUser?> ValidateAsync(string? token, CancellationToken cancellationToken);

    /// <summary>
    /// Invalida el desafío activo del usuario. Devuelve <c>false</c> si ya no existía o si otra
    /// petición lo consumió o reemplazó en paralelo.
    /// </summary>
    Task<bool> ConsumeAsync(ApplicationUser user, CancellationToken cancellationToken);
}
