using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// Runs a test against a real SQL Server only when <see cref="ConnectionVariable"/> names one (for example a SQL Server
/// 2022 container): what depends on its locking cannot be proven with the in-memory provider.
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerTestDatabase.ConnectionVariable)))
            Skip = $"Define {SqlServerTestDatabase.ConnectionVariable} (a SQL Server connection without database) to run it.";
    }
}

/// <summary>A throwaway database with the model of the app, dropped at the end of the test.</summary>
public sealed class SqlServerTestDatabase : IAsyncDisposable
{
    public const string ConnectionVariable = "TIENDITAPP_TEST_SQLSERVER";

    private readonly string _connectionString;

    private SqlServerTestDatabase(string connectionString)
    {
        _connectionString = connectionString;
    }

    public static async Task<SqlServerTestDatabase> CreateAsync()
    {
        var server = Environment.GetEnvironmentVariable(ConnectionVariable)!.TrimEnd(';');
        var database = new SqlServerTestDatabase($"{server};Database=TienditAppTest_{Guid.NewGuid():N}");
        await using var context = database.CreateContext();
        await context.Database.EnsureCreatedAsync();
        return database;
    }

    /// <param name="tenantId">The business in context: its rows pass the tenant filter (none without one).</param>
    public ApplicationDbContext CreateContext(Guid? tenantId = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(_connectionString)
            .Options;
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        if (tenantId is { } id)
            tenant.SetTenant(id);
        return new ApplicationDbContext(options, tenant, new InMemoryApplicationDbContext.NullMediator());
    }

    public async ValueTask DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}
