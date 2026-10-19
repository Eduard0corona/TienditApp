using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.Identity.EventHandlers;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Events;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.Reviews.Entities;
using FoodParkDigital.Infrastructure.Identity;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Persistence.Interceptors;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using InfrastructureDependencyInjection = FoodParkDigital.Infrastructure.DependencyInjection;

namespace FoodParkDigital.Application.Tests.Support;

/// <summary>
/// In-memory SQLite database with foreign keys enforced, behind a retrying execution strategy like
/// the SQL Server one configured in production, so user-initiated transactions must go through
/// <c>CreateExecutionStrategy()</c>. Services are wired as in production DI: real Identity stores,
/// validators and token providers with the production <see cref="IdentityOptions"/>.
/// It does not reproduce SQL Server semantics (locking, isolation, collation).
/// </summary>
internal sealed class SqliteTestHost : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly MutableTenantService _tenantService = new();

    private SqliteTestHost(SqliteConnection connection)
    {
        _connection = connection;

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection, sqlite => sqlite
                .ExecutionStrategy(dependencies => new TestRetryingExecutionStrategy(dependencies))
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .AddInterceptors(
                new AuditableEntityInterceptor(new FixedCurrentUserService(), new FixedDateTimeProvider()),
                new TenantSaveChangesInterceptor(_tenantService),
                BeforeSave,
                CommandFaults,
                CommitFaults)
            .Options;

        var services = new ServiceCollection();
        services.AddLogging();
        // Services such as CustomerLookupService redact sensitive values outside Development.
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = Environments.Production });
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton<ICurrentTenantService>(_tenantService);
        services.AddSingleton<ICurrentUserService, FixedCurrentUserService>();
        services.AddSingleton<IDateTimeProvider, FixedDateTimeProvider>();
        services.AddSingleton<IMediator>(Mediator);
        services.AddScoped<ApplicationDbContext>(sp => new SqliteTestApplicationDbContext(
            options,
            sp.GetRequiredService<ICurrentTenantService>(),
            sp.GetRequiredService<IMediator>()));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        services.AddIdentityCore<ApplicationUser>(InfrastructureDependencyInjection.ConfigureIdentityOptions)
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddTienditAppIdentityDefaults();

        services.AddScoped<IPhoneNormalizationService, PhoneNormalizationService>();
        services.AddScoped<IFolioGenerator, FolioGenerator>();
        // Sales here are about customers and phones: supplies and the cash drawer stay out, explicitly.
        services.AddSingleton(NoOpOrderInventoryService.Instance);
        services.AddSingleton<IOrderCashService>(NoOpOrderCashService.Instance);
        services.AddScoped<ICustomerLookupService, CustomerLookupService>();
        services.AddScoped<ITemporaryCustomerClaimService, TemporaryCustomerClaimService>();
        services.AddSingleton<IEmailService>(Email);
        services.AddSingleton<ISmsService>(Sms);
        services.AddScoped<IOutbox>(Outbox.For);
        services.AddSingleton<IJwtTokenService>(Jwt);

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public RecordingEmailService Email { get; } = new();
    /// <summary>Realtime events published by the contexts of the host (none is sent: requests return default).</summary>
    public RecordingMediator Mediator { get; } = new();
    public FakeJwtTokenService Jwt { get; } = new();
    public RecordingSmsService Sms { get; } = new();
    public RecordingOutbox Outbox { get; } = new();
    public BeforeSaveInterceptor BeforeSave { get; } = new();
    public CommandFaultInterceptor CommandFaults { get; } = new();
    public CommitFaultInterceptor CommitFaults { get; } = new();

    public static async Task<SqliteTestHost> CreateAsync(params string[] roles)
    {
        var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        SqliteSqlServerFunctions.Register(connection);

        var host = new SqliteTestHost(connection);
        await host.InScopeAsync(async services =>
        {
            var context = services.GetRequiredService<ApplicationDbContext>();
            await context.Database.EnsureCreatedAsync();
            await SqliteConversationVersion.CreateTriggerAsync(context);

            var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
            foreach (var role in roles)
                await roleManager.CreateAsync(new ApplicationRole(role));
        });

        return host;
    }

    public void SetTenant(Guid tenantId) => _tenantService.SetTenant(tenantId);

    /// <summary>Runs <paramref name="action"/> in its own DI scope, like a separate request.</summary>
    public async Task<TResult> InScopeAsync<TResult>(Func<IServiceProvider, Task<TResult>> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public Task InScopeAsync(Func<IServiceProvider, Task> action)
        => InScopeAsync(async services =>
        {
            await action(services);
            return true;
        });

    /// <summary>Builds <typeparamref name="THandler"/> from DI in a fresh scope and runs it.</summary>
    public Task<TResult> HandleAsync<THandler, TResult>(Func<THandler, Task<TResult>> handle)
        => InScopeAsync(services => handle(ActivatorUtilities.CreateInstance<THandler>(services)));

    /// <summary>
    /// What the outbox dispatcher does after the commit: runs <typeparamref name="THandler"/> for each event of
    /// <typeparamref name="TEvent"/> the requests added and no dispatch ran yet, each in its own scope.
    /// </summary>
    public async Task DispatchAsync<TEvent, THandler>()
        where TEvent : DomainEvent
        where THandler : IOutboxEventHandler<TEvent>
    {
        var committed = await InScopeAsync(services => Outbox.TakeCommittedAsync<TEvent>(services.GetRequiredService<ApplicationDbContext>()));
        foreach (var (domainEvent, tenantId) in committed)
        {
            await InScopeAsync(services => ActivatorUtilities.CreateInstance<THandler>(services).HandleAsync(
                domainEvent,
                new OutboxEventContext(
                    domainEvent.EventId, tenantId, null, null, services.GetRequiredService<IDateTimeProvider>().UtcNow, 1),
                CancellationToken.None));
        }
    }

    /// <summary>Sends the account emails the requests asked for (confirmation, reset, invitation), as the outbox does.</summary>
    public Task DispatchAccountEmailsAsync()
        => DispatchAsync<AccountEmailRequestedEvent, AccountEmailRequestedHandler>();

    /// <summary>Reads committed data through a fresh context, never the handler's change tracker.</summary>
    public Task<TResult> QueryAsync<TResult>(Func<ApplicationDbContext, Task<TResult>> query)
        => InScopeAsync(services => query(services.GetRequiredService<ApplicationDbContext>()));

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class SqliteTestApplicationDbContext : ApplicationDbContext
    {
        public SqliteTestApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ICurrentTenantService tenantService,
            IMediator mediator)
            : base(options, tenantService, mediator)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
            {
                // SQL Server-only column types such as nvarchar(max) are not valid SQLite DDL.
                if (property.GetColumnType()?.Contains("(max)", StringComparison.OrdinalIgnoreCase) == true)
                    property.SetColumnType("TEXT");

                // SQLite cannot compare or order DateTimeOffset values stored as text.
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                    property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
            }

            // SQL Server generates rowversion values; emulate them so inserts satisfy NOT NULL.
            modelBuilder.Entity<Order>()
                .Property(o => o.Version)
                .HasDefaultValueSql("randomblob(8)");
            modelBuilder.Entity<Appointment>()
                .Property(a => a.Version)
                .HasDefaultValueSql("randomblob(8)");
            modelBuilder.Entity<BusinessReview>()
                .Property(r => r.Version)
                .HasDefaultValueSql("randomblob(8)");
            modelBuilder.Entity<OnlinePaymentAccount>()
                .Property(a => a.Version)
                .HasDefaultValueSql("randomblob(8)");
            modelBuilder.Entity<OnlineCheckout>()
                .Property(c => c.Version)
                .HasDefaultValueSql("randomblob(8)");
            modelBuilder.Entity<OnlinePaymentRefund>()
                .Property(r => r.Version)
                .HasDefaultValueSql("randomblob(8)");

            SqliteConversationVersion.Configure(modelBuilder);
        }
    }

    private sealed class MutableTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class FixedCurrentUserService : ICurrentUserService
    {
        public string? UserId => null;
        public string? UserName => "sqlite-test";
        public string? Role => null;
        public string? TenantRole => null;
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }

    private sealed class FixedDateTimeProvider : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => new(2026, 2, 16, 16, 0, 0, TimeSpan.Zero);
    }

    internal sealed class FakeJwtTokenService : IJwtTokenService
    {
        public string GenerateAccessToken(ApplicationUser user, Guid sessionId, TenantTokenContext? tenantContext = null)
            => $"access-token-{user.Id}";

        public IssuedRefreshToken GenerateRefreshToken(Guid userId, Guid sessionId)
        {
            var value = Guid.NewGuid().ToString("N");
            return new IssuedRefreshToken(
                new RefreshToken(userId, sessionId, value, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7)),
                value);
        }
    }

    internal sealed class RecordingMediator : IMediator
    {
        private readonly List<object> _published = [];

        public IReadOnlyList<object> Published
        {
            get
            {
                lock (_published)
                    return _published.ToList();
            }
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) => Task.FromResult<TResponse>(default!);
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => Task.CompletedTask;
        public Task Publish(object notification, CancellationToken ct = default) => Record(notification);
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification => Record(notification!);
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<object?>();
        public Task<object?> Send(object request, CancellationToken ct = default) => Task.FromResult<object?>(null);

        private Task Record(object notification)
        {
            lock (_published)
                _published.Add(notification);
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// Retries only <see cref="SimulatedTransientException"/>, but shares the base
/// <see cref="ExecutionStrategy"/> behavior that rejects user-initiated transactions.
/// </summary>
internal sealed class TestRetryingExecutionStrategy : ExecutionStrategy
{
    public TestRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : base(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.Zero)
    {
    }

    protected override bool ShouldRetryOn(Exception exception) => exception is SimulatedTransientException;
}

internal sealed class SimulatedTransientException : Exception
{
    public SimulatedTransientException()
        : base("Simulated transient database failure.")
    {
    }
}
