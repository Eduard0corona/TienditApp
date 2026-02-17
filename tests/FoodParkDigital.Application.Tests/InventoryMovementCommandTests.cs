using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Mappings;
using FoodParkDigital.Application.Inventory.Commands;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

public class InventoryMovementCommandTests
{
    [Fact]
    public async Task CreateEntry_ShouldCreateStockAndMovement()
    {
        var tenantService = new TestCurrentTenantService();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options, tenantService, new NullMediator());
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();

        var supply = new SupplyItem(tenantService.TenantId, "Harina", UnitType.Kg, code: "INS-001");
        var location = new InventoryLocation(tenantService.TenantId, "Almacen", "MAIN", isDefault: true);
        context.SupplyItems.Add(supply);
        context.InventoryLocations.Add(location);
        await context.SaveChangesAsync();

        var handler = new CreateInventoryMovementCommandHandler(
            context,
            tenantService,
            new TestCurrentUserService(),
            mapper);

        var result = await handler.Handle(new CreateInventoryMovementCommand
        {
            MovementType = InventoryMovementType.Entry,
            MovementReason = InventoryMovementReason.Purchase,
            Lines =
            [
                new CreateInventoryMovementLineRequest
                {
                    SupplyItemId = supply.Id,
                    InventoryLocationId = location.Id,
                    Quantity = 5,
                    UnitCost = 20
                }
            ]
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value.Lines.Single().Quantity);

        var stock = await context.InventoryStocks.SingleAsync();
        Assert.Equal(5, stock.OnHandQuantity);
        Assert.Equal(20, stock.LastCost);
    }

    [Fact]
    public async Task CreateExitWithoutStock_ShouldThrowDomainException()
    {
        var tenantService = new TestCurrentTenantService();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ApplicationDbContext(options, tenantService, new NullMediator());
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper();

        var supply = new SupplyItem(tenantService.TenantId, "Queso", UnitType.Kg, code: "INS-002");
        var location = new InventoryLocation(tenantService.TenantId, "Almacen", "MAIN", isDefault: true);
        context.SupplyItems.Add(supply);
        context.InventoryLocations.Add(location);
        await context.SaveChangesAsync();

        var handler = new CreateInventoryMovementCommandHandler(
            context,
            tenantService,
            new TestCurrentUserService(),
            mapper);

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(new CreateInventoryMovementCommand
        {
            MovementType = InventoryMovementType.Exit,
            MovementReason = InventoryMovementReason.Consumption,
            Lines =
            [
                new CreateInventoryMovementLineRequest
                {
                    SupplyItemId = supply.Id,
                    InventoryLocationId = location.Id,
                    Quantity = 1
                }
            ]
        }, CancellationToken.None));
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
}
