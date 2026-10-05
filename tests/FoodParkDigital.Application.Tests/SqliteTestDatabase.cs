using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// In-memory SQLite database for tests that depend on relational constraints such as
/// unique filtered indexes, which the InMemory provider ignores. It does not reproduce
/// SQL Server semantics; migrations still need verification against SQL Server.
/// </summary>
internal sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BeforeSaveChangesInterceptor _beforeSave = new();
    private readonly TestCurrentTenantService _tenantService = new();

    private SqliteTestDatabase(SqliteConnection connection)
    {
        _connection = connection;
    }

    public static async Task<SqliteTestDatabase> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var database = new SqliteTestDatabase(connection);
        await using var context = database.CreateIndependentContext();
        await context.Database.EnsureCreatedAsync();
        await SqliteConversationVersion.CreateTriggerAsync(context);

        return database;
    }

    /// <summary>
    /// Fails the next matching command of a context from <see cref="CreateContext"/>, e.g. a write
    /// that fails for a reason other than a concurrent request.
    /// </summary>
    public CommandFaultInterceptor CommandFaults { get; } = new();

    /// <summary>
    /// Context for the code under test; <see cref="BeforeNextSave"/> and <see cref="CommandFaults"/> hook into it.
    /// </summary>
    public ApplicationDbContext CreateContext() => Create(_beforeSave, CommandFaults);

    /// <summary>Another context on the same database, e.g. acting as a concurrent request.</summary>
    public ApplicationDbContext CreateIndependentContext() => Create();

    /// <summary>
    /// Runs <paramref name="action"/> right before the next SaveChanges of a context from
    /// <see cref="CreateContext"/>: after the code under test ran its checks, before it writes.
    /// </summary>
    public void BeforeNextSave(Func<Task> action) => _beforeSave.Arm(action);

    /// <summary>
    /// Tenant of the request for the global query filters of every context, for code that relies on
    /// them; without it the filters only match rows of no tenant.
    /// </summary>
    public void SetTenant(Guid tenantId) => _tenantService.SetTenant(tenantId);

    /// <summary>The tenant of every context, for code under test that checks the business in scope.</summary>
    public ICurrentTenantService TenantService => _tenantService;

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private ApplicationDbContext Create(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptors)
            .Options;

        return new SqliteApplicationDbContext(options, _tenantService, new NullMediator());
    }

    private sealed class SqliteApplicationDbContext : ApplicationDbContext
    {
        public SqliteApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ICurrentTenantService tenantService,
            IMediator mediator)
            : base(options, tenantService, mediator)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // SQL Server-only column types such as nvarchar(max) are not valid SQLite DDL.
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
            {
                if (property.GetColumnType()?.Contains("(max)", StringComparison.OrdinalIgnoreCase) == true)
                    property.SetColumnType("TEXT");
            }

            SqliteConversationVersion.Configure(modelBuilder);
        }
    }

    private sealed class BeforeSaveChangesInterceptor : SaveChangesInterceptor
    {
        private Func<Task>? _pending;

        public void Arm(Func<Task> action) => _pending = action;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var pending = Interlocked.Exchange(ref _pending, null);
            if (pending is not null)
                await pending();

            return result;
        }
    }

    private sealed class TestCurrentTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class NullMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
            => Task.FromResult<TResponse>(default!);

        public Task Send<TRequest>(TRequest request, CancellationToken ct = default)
            where TRequest : IRequest
            => Task.CompletedTask;

        public Task Publish(object notification, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(object request, CancellationToken ct = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default)
            => AsyncEnumerable.Empty<object?>();

        public Task<object?> Send(object request, CancellationToken ct = default)
            => Task.FromResult<object?>(null);
    }
}
