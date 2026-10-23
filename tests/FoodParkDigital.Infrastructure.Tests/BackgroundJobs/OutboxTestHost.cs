using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.BackgroundJobs;
using FoodParkDigital.Infrastructure.BackgroundJobs.Outbox;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Persistence.Interceptors;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Infrastructure.Tests.BackgroundJobs;

/// <summary>
/// Production-like wiring of the outbox (interceptor, scoped tenant and actor, dispatcher) over a shared
/// in-memory database. InMemory checks concurrency tokens, which is what claiming relies on; it does not
/// reproduce SQL Server transactions or locking.
/// </summary>
internal sealed class OutboxTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private OutboxTestHost(ServiceProvider provider)
    {
        _provider = provider;
    }

    public MutableClock Clock => _provider.GetRequiredService<MutableClock>();
    public HandlerLog Log => _provider.GetRequiredService<HandlerLog>();
    public OutboxDispatcher Dispatcher => _provider.GetRequiredService<OutboxDispatcher>();

    /// <summary>Resolves a singleton (such as a worker) from the root provider.</summary>
    public T Get<T>()
        where T : notnull
        => _provider.GetRequiredService<T>();

    public static OutboxTestHost Create(int maxAttempts = 3, Action<IServiceCollection>? configure = null)
    {
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        var clock = new MutableClock();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDateTimeProvider>(clock);
        services.AddSingleton<MutableClock>(clock);
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentTenantService, CurrentTenantService>();
        services.AddScoped<IBackgroundExecutionContext, BackgroundExecutionContext>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddSingleton<IMediator, NullMediator>();
        services.AddSingleton<OutboxSignal>();
        services.AddSingleton<HandlerLog>();
        services.AddScoped<TenantSaveChangesInterceptor>();
        services.AddScoped<OutboxSaveChangesInterceptor>();
        services.AddScoped<ApplicationDbContext>(sp => new TestDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName, databaseRoot)
                .AddInterceptors(
                    sp.GetRequiredService<TenantSaveChangesInterceptor>(),
                    sp.GetRequiredService<OutboxSaveChangesInterceptor>())
                .Options,
            sp.GetRequiredService<ICurrentTenantService>(),
            sp.GetRequiredService<IMediator>()));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.Configure<BackgroundJobOptions>(options => options.Outbox.MaxAttempts = maxAttempts);
        services.AddSingleton<OutboxDispatcher>();
        configure?.Invoke(services);

        return new OutboxTestHost(services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }));
    }

    public async Task<TResult> InScopeAsync<TResult>(Func<IServiceProvider, Task<TResult>> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    /// <summary>Saves a new order as the given user would in a request, raising <see cref="OrderCreatedEvent"/>.</summary>
    public Task<Order> CreateOrderAsync(Guid tenantId, string actorName = "cajero@negocio.mx")
        => InScopeAsync(async services =>
        {
            services.GetRequiredService<ICurrentTenantService>().SetTenant(tenantId);
            services.GetRequiredService<IBackgroundExecutionContext>().SetActor("user-1", actorName);
            var context = services.GetRequiredService<ApplicationDbContext>();
            var order = new Order(tenantId, null, null, true, $"ORD-{Random.Shared.Next(100000, 999999)}",
                BusinessType.Retail, OrderType.Retail, "Público en general", null, null, null);
            order.MarkAsCreated();
            context.Orders.Add(order);
            await context.SaveChangesAsync();
            return order;
        });

    public Task<List<OutboxMessage>> MessagesAsync()
        => InScopeAsync(services => services.GetRequiredService<ApplicationDbContext>()
            .OutboxMessages.AsNoTracking().OrderBy(m => m.CreatedAt).ToListAsync());

    public Task<List<OutboxMessageConsumer>> ConsumersAsync()
        => InScopeAsync(services => services.GetRequiredService<ApplicationDbContext>()
            .OutboxMessageConsumers.AsNoTracking().ToListAsync());

    public Task<List<AuditLog>> AuditLogsAsync()
        => InScopeAsync(services => services.GetRequiredService<ApplicationDbContext>()
            .AuditLogs.IgnoreQueryFilters().AsNoTracking().ToListAsync());

    public ValueTask DisposeAsync() => _provider.DisposeAsync();

    private sealed class TestDbContext : ApplicationDbContext
    {
        public TestDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentTenantService tenantService, IMediator mediator)
            : base(options, tenantService, mediator)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // The in-memory provider does not generate SQL Server row versions.
            modelBuilder.Entity<Order>().Property(o => o.Version).IsRequired(false).ValueGeneratedNever();
            modelBuilder.Entity<OnlinePaymentAccount>().Property(a => a.Version).IsRequired(false).ValueGeneratedNever();
            modelBuilder.Entity<OrderInvoice>().Property(i => i.Version).IsRequired(false).ValueGeneratedNever();
        }
    }

    private sealed class NullMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) => Task.FromResult<TResponse>(default!);
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => Task.CompletedTask;
        public Task<object?> Send(object request, CancellationToken ct = default) => Task.FromResult<object?>(null);
        public Task Publish(object notification, CancellationToken ct = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<object?>();
    }
}

internal sealed class MutableClock : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 4, 18, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

/// <summary>What the test handlers saw, shared across scopes.</summary>
internal sealed class HandlerLog
{
    public List<(string Handler, Guid TenantId, string? Actor, int Attempt)> Runs { get; } = [];

    /// <summary>How many more times <see cref="FlakyHandler"/> fails before succeeding.</summary>
    public int FlakyFailuresLeft { get; set; }
}

/// <summary>Audits the order through the scoped context without saving, as real outbox handlers do.</summary>
internal sealed class AuditingHandler : IOutboxEventHandler<OrderCreatedEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _user;
    private readonly HandlerLog _log;

    public AuditingHandler(IApplicationDbContext context, ICurrentTenantService tenant, ICurrentUserService user, HandlerLog log)
    {
        _context = context;
        _tenant = tenant;
        _user = user;
        _log = log;
    }

    public Task HandleAsync(OrderCreatedEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
    {
        _log.Runs.Add((nameof(AuditingHandler), _tenant.TenantId, _user.UserName, context.Attempt));
        _context.AuditLogs.Add(new AuditLog(domainEvent.TenantId, nameof(Order), domainEvent.OrderId.ToString(), "Test", context.ActorName ?? "system"));
        return Task.CompletedTask;
    }
}

internal sealed class FlakyHandler : IOutboxEventHandler<OrderCreatedEvent>
{
    private readonly HandlerLog _log;
    private readonly ICurrentTenantService _tenant;

    public FlakyHandler(HandlerLog log, ICurrentTenantService tenant)
    {
        _log = log;
        _tenant = tenant;
    }

    public Task HandleAsync(OrderCreatedEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
    {
        _log.Runs.Add((nameof(FlakyHandler), _tenant.TenantId, null, context.Attempt));
        if (_log.FlakyFailuresLeft > 0)
        {
            _log.FlakyFailuresLeft--;
            throw new TimeoutException("Provider timed out.");
        }

        return Task.CompletedTask;
    }
}
