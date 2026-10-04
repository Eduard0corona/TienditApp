using System.Globalization;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.EventHandlers;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>Customers read order states and totals in Spanish, whatever the server culture is.</summary>
public class OrderNotificationLabelsTests
{
    [Fact]
    public void EveryOrderStatus_ShouldHaveASpanishLabel()
    {
        foreach (var status in Enum.GetValues<OrderStatus>())
            Assert.NotEqual(status.ToString(), OrderStatusLabels.ToSpanish(status));

        Assert.Equal("En preparación", OrderStatusLabels.ToSpanish(OrderStatus.InPreparation));
    }

    [Fact]
    public void FormatMoney_ShouldNotDependOnTheServerCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("$1,234.50", OrderStatusLabels.FormatMoney(1234.5m));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task StatusChangeTemplate_ShouldCarryTheSpanishStatus()
    {
        var tenantService = new FixedTenantService();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new InMemoryContext(options, tenantService);
        var order = new Order(tenantService.TenantId, Guid.NewGuid(), null, true, "ORD-000001",
            BusinessType.Food, OrderType.Retail, "Karla", "+525511112222", null, null);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        var whatsApp = new RecordingWhatsAppService();

        var handler = new OrderStatusChangedEventHandler(
            context, new FixedUserService(), new NoOpOrderNotificationService(), whatsApp,
            NullLogger<OrderStatusChangedEventHandler>.Instance);
        await handler.Handle(
            new OrderStatusChangedEvent(order.Id, tenantService.TenantId, OrderStatus.Confirmed, OrderStatus.InPreparation),
            CancellationToken.None);

        var parameters = Assert.Single(whatsApp.Templates);
        Assert.Equal(["ORD-000001", "En preparación"], parameters);
    }

    private sealed class InMemoryContext(DbContextOptions<ApplicationDbContext> options, ICurrentTenantService tenantService)
        : ApplicationDbContext(options, tenantService, new NoOpMediator())
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // The in-memory provider does not generate SQL Server row versions.
            modelBuilder.Entity<Order>().Property(o => o.Version).IsRequired(false).ValueGeneratedNever();
        }
    }

    private sealed class RecordingWhatsAppService : IWhatsAppService
    {
        public List<string[]> Templates { get; } = [];

        public Task<Result> SendTemplateMessageAsync(Guid tenantId, string phoneNumber, string templateName,
            string languageCode, string[] parameters, CancellationToken cancellationToken = default)
        {
            Templates.Add(parameters);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> SendTextMessageAsync(Guid tenantId, string phoneNumber, string message,
            CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
    }

    private sealed class NoOpOrderNotificationService : IOrderNotificationService
    {
        public Task NotifyOrderCreated(Guid tenantId, Guid orderId, string orderNumber, BusinessType businessType, decimal total) => Task.CompletedTask;
        public Task NotifyOrderStatusChanged(Guid tenantId, Guid orderId, OrderStatus previousStatus, OrderStatus newStatus) => Task.CompletedTask;
        public Task NotifyOrderCancelled(Guid tenantId, Guid orderId, OrderStatus previousStatus, string? reason) => Task.CompletedTask;
        public Task NotifyServiceBoardPieceUpdated(Guid tenantId, Guid orderId, Guid pieceId, ServiceOrderPieceStatus status, Guid? assignedToUserId) => Task.CompletedTask;
    }

    private sealed class FixedTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; } = Guid.NewGuid();
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class FixedUserService : ICurrentUserService
    {
        public string? UserId => null;
        public string? UserName => "cocina";
        public string? Role => "KITCHEN";
        public string? TenantRole => null;
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }

    private sealed class NoOpMediator : IMediator
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
