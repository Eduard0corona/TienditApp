using Microsoft.Data.Sqlite;

namespace FoodParkDigital.Application.Tests.Support;

/// <summary>
/// SQL Server functions that check constraints of the model call and SQLite lacks, giving SQL Server's results, so the
/// relational tests can write to those tables and those checks refuse the same values there.
/// </summary>
internal static class SqliteSqlServerFunctions
{
    /// <summary>Adds them to <paramref name="connection"/>, the one every context of a test database shares.</summary>
    public static void Register(SqliteConnection connection)
        // LEN counts UTF-16 code units and leaves out trailing spaces (CK_SpecialDays_Reason). A CHECK may only call a
        // deterministic function.
        => connection.CreateFunction("LEN", (string? value) => value?.TrimEnd(' ').Length, isDeterministic: true);
}
