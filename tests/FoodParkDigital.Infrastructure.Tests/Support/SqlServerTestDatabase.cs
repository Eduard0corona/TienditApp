using System.Reflection;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// Runs a test against a real SQL Server only when <see cref="ConnectionVariable"/> names one (for example a SQL Server
/// 2022 container): what depends on its locking cannot be proven with the in-memory provider.
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    /// <summary>
    /// How the name of every class with these tests ends. Only the Migrations job of CI has a SQL Server, and it runs the
    /// tests whose name contains this (<c>--filter "FullyQualifiedName~SqlServerTests"</c>): in a class named otherwise,
    /// a test is skipped in every job.
    /// </summary>
    public const string ClassNameSuffix = "SqlServerTests";

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerTestDatabase.ConnectionVariable)))
            Skip = $"Define {SqlServerTestDatabase.ConnectionVariable} (a SQL Server connection without database) to run it.";
    }

    /// <summary>
    /// The tests of <paramref name="assembly"/> with this attribute, each with the class xUnit runs it in (an abstract
    /// class only lends its tests to the classes that inherit them).
    /// </summary>
    public static List<(Type TestClass, MethodInfo Method)> TestsIn(Assembly assembly)
        => assembly.GetTypes()
            .Where(type => !type.IsAbstract || type.IsSealed)
            .SelectMany(type => type
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(method => method.IsDefined(typeof(SqlServerFactAttribute), inherit: true))
                .Select(method => (type, method)))
            .ToList();
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

    /// <summary>The connection to this database, for an API host under test.</summary>
    public string ConnectionString => _connectionString;

    public static async Task<SqlServerTestDatabase> CreateAsync()
    {
        var database = CreateEmpty();
        await using var context = database.CreateContext();
        await context.Database.EnsureCreatedAsync();
        return database;
    }

    /// <summary>A database not created yet: the test creates it, for example by applying the migrations.</summary>
    public static SqlServerTestDatabase CreateEmpty()
    {
        var server = Environment.GetEnvironmentVariable(ConnectionVariable)!.TrimEnd(';');
        return new SqlServerTestDatabase($"{server};Database=TienditAppTest_{Guid.NewGuid():N}");
    }

    /// <param name="tenantId">The business in context: its rows pass the tenant filter (none without one).</param>
    public ApplicationDbContext CreateContext(Guid? tenantId = null)
        => CreateContext(tenantId, (options, tenant, mediator) => new ApplicationDbContext(options, tenant, mediator));

    /// <summary>
    /// A context of an earlier version of the app: a subclass of <see cref="ApplicationDbContext"/> that maps less, for
    /// a test that takes the database back to the migrations that version ran on.
    /// </summary>
    public TContext CreateContext<TContext>(
        Guid? tenantId,
        Func<DbContextOptions<ApplicationDbContext>, ICurrentTenantService, IMediator, TContext> create)
        where TContext : ApplicationDbContext
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(_connectionString, sql => sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .Options;
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        if (tenantId is { } id)
            tenant.SetTenant(id);
        return create(options, tenant, new InMemoryApplicationDbContext.NullMediator());
    }

    /// <summary>
    /// A context set up like production where a test depends on it: retries on transient errors and MARS on, as in the
    /// connection strings of the app (with MARS, SQL Server keeps no savepoint when a save fails inside a transaction).
    /// </summary>
    public ApplicationDbContext CreateProductionLikeContext(ICurrentTenantService tenant, IMediator mediator, params IInterceptor[] interceptors)
        => CreateProductionLikeContext(
            tenant, mediator, (options, currentTenant, currentMediator) => new ApplicationDbContext(options, currentTenant, currentMediator), interceptors);

    /// <summary>The same, for a context of an earlier version of the app (see <see cref="CreateContext{TContext}"/>).</summary>
    public TContext CreateProductionLikeContext<TContext>(
        ICurrentTenantService tenant,
        IMediator mediator,
        Func<DbContextOptions<ApplicationDbContext>, ICurrentTenantService, IMediator, TContext> create,
        params IInterceptor[] interceptors)
        where TContext : ApplicationDbContext
    {
        var connection = new SqlConnectionStringBuilder(_connectionString) { MultipleActiveResultSets = true };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection.ConnectionString, sql => sql
                .EnableRetryOnFailure(maxRetryCount: 3)
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .AddInterceptors(interceptors)
            .Options;
        return create(options, tenant, mediator);
    }

    public async ValueTask DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}
