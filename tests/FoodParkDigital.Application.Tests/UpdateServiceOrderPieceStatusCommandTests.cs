using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

public class UpdateServiceOrderPieceStatusCommandTests
{
    [Fact]
    public async Task UpdatePieceStatus_WhenFlowCompletes_ShouldMarkOrderCompleted()
    {
        var tenantService = new TestCurrentTenantService();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new TestApplicationDbContext(options, tenantService, new NullMediator());

        var order = new Order(
            tenantId: tenantService.TenantId,
            customerUserId: Guid.NewGuid(),
            temporaryCustomerId: null,
            createdByBusiness: true,
            orderNumber: "ORD-000001",
            businessType: BusinessType.Service,
            orderType: OrderType.Service,
            customerName: "Cliente Test",
            customerPhone: "+525511112222",
            customerNotes: null,
            tableNumber: null);

        order.AddItem(Guid.NewGuid(), "Limpieza de tenis", 1, 250m);
        order.AddServicePiece(
            itemName: "Par Nike",
            brand: "Nike",
            model: "Air",
            color: "Blanco",
            size: "27",
            material: "Piel",
            conditionNotes: null,
            requestedService: "Lavado premium",
            internalNotes: null);

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var pieceId = order.ServicePieces.Single().Id;
        var handler = new UpdateServiceOrderPieceStatusCommandHandler(
            context,
            new TestCurrentUserService());

        var transitions = new[]
        {
            ServiceOrderPieceStatus.InProgress,
            ServiceOrderPieceStatus.Completed
        };

        foreach (var status in transitions)
        {
            var result = await handler.Handle(new UpdateServiceOrderPieceStatusCommand
            {
                OrderId = order.Id,
                PieceId = pieceId,
                NewStatus = status
            }, CancellationToken.None);

            Assert.True(result.IsSuccess, result.Error);
        }

        var persisted = await context.Orders
            .Include(x => x.ServicePieces)
            .FirstAsync(x => x.Id == order.Id);

        Assert.Equal(OrderStatus.Completed, persisted.Status);
        Assert.Equal(ServiceOrderPieceStatus.Completed, persisted.ServicePieces.Single().Status);
    }

    [Fact]
    public async Task UpdatePieceStatus_WhenTransitionIsInvalid_ShouldFail()
    {
        var tenantService = new TestCurrentTenantService();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new TestApplicationDbContext(options, tenantService, new NullMediator());

        var order = new Order(
            tenantId: tenantService.TenantId,
            customerUserId: Guid.NewGuid(),
            temporaryCustomerId: null,
            createdByBusiness: true,
            orderNumber: "ORD-000002",
            businessType: BusinessType.Service,
            orderType: OrderType.Service,
            customerName: "Cliente Test",
            customerPhone: "+525511112223",
            customerNotes: null,
            tableNumber: null);

        order.AddItem(Guid.NewGuid(), "Limpieza de tenis", 1, 250m);
        order.AddServicePiece(
            itemName: "Par Adidas",
            brand: null,
            model: null,
            color: null,
            size: null,
            material: null,
            conditionNotes: null,
            requestedService: null,
            internalNotes: null);

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var pieceId = order.ServicePieces.Single().Id;
        var handler = new UpdateServiceOrderPieceStatusCommandHandler(
            context,
            new TestCurrentUserService());

        var result = await handler.Handle(new UpdateServiceOrderPieceStatusCommand
        {
            OrderId = order.Id,
            PieceId = pieceId,
            NewStatus = ServiceOrderPieceStatus.Completed
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("No se puede cambiar", result.Error);
    }

    private sealed class TestCurrentTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; } = Guid.NewGuid();
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public string? UserId => Guid.NewGuid().ToString();
        public string? UserName => "tester";
        public string? Role => "TENANT_ADMIN";
        public string? TenantRole => "TENANT_ADMIN";
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
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

    private sealed class TestApplicationDbContext : ApplicationDbContext
    {
        public TestApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ICurrentTenantService tenantService,
            IMediator mediator)
            : base(options, tenantService, mediator)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Order>()
                .Property(o => o.Version)
                .IsRequired(false)
                .ValueGeneratedNever();
        }
    }
}
