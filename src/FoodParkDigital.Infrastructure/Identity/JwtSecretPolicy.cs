using System.Security.Cryptography;
using System.Text;

namespace FoodParkDigital.Infrastructure.Identity;

/// <summary>
/// Reglas mínimas para la llave HMAC con la que se firman los JWT.
/// </summary>
public static class JwtSecretPolicy
{
    public const int MinimumSecretBytes = 32;

    // SHA-256 de llaves que estuvieron versionadas en el repositorio. Cualquiera con acceso al
    // historial puede firmar tokens con ellas, así que nunca deben aceptarse.
    private static readonly HashSet<string> CompromisedSecretHashes = new(StringComparer.OrdinalIgnoreCase)
    {
        "A11310D09E1C687F2926DE0F39673B3BD629A1F751CD55FF165B595D58A9E58A"
    };

    /// <summary>
    /// Devuelve el motivo por el que la llave no es utilizable, o <c>null</c> si es válida.
    /// </summary>
    public static string? GetValidationError(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            return "JwtSettings:Secret no está configurado.";

        var secretBytes = Encoding.UTF8.GetBytes(secret);
        if (secretBytes.Length < MinimumSecretBytes)
            return $"JwtSettings:Secret debe tener al menos {MinimumSecretBytes} bytes.";

        var hash = Convert.ToHexString(SHA256.HashData(secretBytes));
        if (CompromisedSecretHashes.Contains(hash))
            return "JwtSettings:Secret usa una llave que estuvo publicada en el repositorio y debe rotarse.";

        return null;
    }

    /// <summary>
    /// Genera una llave aleatoria para desarrollo local. Los tokens emitidos dejan de ser válidos al reiniciar.
    /// </summary>
    public static string CreateEphemeralSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
}
