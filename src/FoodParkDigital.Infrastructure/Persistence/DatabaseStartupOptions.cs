using Microsoft.Data.SqlClient;

namespace FoodParkDigital.Infrastructure.Persistence;

/// <summary>
/// Controla la migración y el seed automáticos al arrancar. Solo se aplican en Development.
/// </summary>
public class DatabaseStartupOptions
{
    public const string SectionName = "Database";

    public bool AutoMigrateOnStartup { get; set; }
    public bool SeedOnStartup { get; set; }

    /// <summary>
    /// Permite migrar o sembrar una base que no es local. Debe activarse de forma explícita.
    /// </summary>
    public bool AllowRemoteDatabase { get; set; }

    /// <summary>
    /// Indica si el servidor de la cadena de conexión es local: loopback, LocalDB o un nombre de host
    /// de una sola etiqueta (nombre de equipo o servicio de docker-compose). Cualquier FQDN o IP no
    /// loopback se considera remoto.
    /// </summary>
    public static bool IsLocalDataSource(string? connectionString)
    {
        var host = GetHost(connectionString);
        if (string.IsNullOrWhiteSpace(host))
            return false;

        if (host is "." or "(local)" || host.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase))
            return true;

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host is "127.0.0.1" or "::1" or "[::1]")
            return true;

        return !host.Contains('.') && !host.Contains(':');
    }

    /// <summary>
    /// Devuelve solo el nombre del servidor (sin credenciales) para mensajes de diagnóstico.
    /// </summary>
    public static string? GetHost(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        string dataSource;
        try
        {
            dataSource = new SqlConnectionStringBuilder(connectionString).DataSource;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException)
        {
            return null;
        }

        var host = dataSource.Trim();

        var protocolSeparator = host.IndexOf(':');
        if (protocolSeparator > 0 && host[..protocolSeparator] is var protocol
            && (protocol.Equals("tcp", StringComparison.OrdinalIgnoreCase)
                || protocol.Equals("np", StringComparison.OrdinalIgnoreCase)
                || protocol.Equals("lpc", StringComparison.OrdinalIgnoreCase)))
        {
            host = host[(protocolSeparator + 1)..];
        }

        var portSeparator = host.IndexOf(',');
        if (portSeparator >= 0)
            host = host[..portSeparator];

        var instanceSeparator = host.IndexOf('\\');
        if (instanceSeparator >= 0)
            host = host[..instanceSeparator];

        return host.Trim();
    }
}
