using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Application.Inventory.Commands;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
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

        var supply = new SupplyItem(tenantService.TenantId, "Harina", UnitType.Kg, code: "INS-001");
        var location = new InventoryLocation(tenantService.TenantId, "Almacen", "MAIN", isDefault: true);
        context.SupplyItems.Add(supply);
        context.InventoryLocations.Add(location);
        await context.SaveChangesAsync();

        var handler = new CreateInventoryMovementCommandHandler(context, tenantService, new InventoryLedger(context, new FolioGenerator(context)));

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

        var supply = new SupplyItem(tenantService.TenantId, "Queso", UnitType.Kg, code: "INS-002");
        var location = new InventoryLocation(tenantService.TenantId, "Almacen", "MAIN", isDefault: true);
        context.SupplyItems.Add(supply);
        context.InventoryLocations.Add(location);
        await context.SaveChangesAsync();

        var handler = new CreateInventoryMovementCommandHandler(context, tenantService, new InventoryLedger(context, new FolioGenerator(context)));

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

    // Bodies as the inventory screen posts them: the enums travel as the numbers of client/src/types/index.ts.
    [Theory]
    [InlineData("Entrada / Compra", 1, 1, 13)]
    [InlineData("Salida / Consumo", 2, 4, 7)]
    [InlineData("Ajuste (+) / Conteo fisico", 3, 7, 13)]
    [InlineData("Ajuste (-) / Merma", 4, 5, 7)]
    [InlineData("Devolucion produccion / Produccion", 7, 3, 13)]
    [InlineData("Devolucion proveedor / Devolucion", 8, 9, 7)]
    public async Task MovementPostedByTheInventoryScreen_ShouldMoveStockInTheDirectionOfItsType(
        string screenOption, int movementType, int movementReason, decimal expectedOnHand)
    {
        var setup = await SeedSupplyWithStockAsync(onHand: 10);
        await using var context = setup.Context;
        var command = DeserializeScreenBody(movementType, movementReason, setup.Supply.Id, setup.Origin.Id, quantity: 3);

        var validation = new CreateInventoryMovementCommandValidator().Validate(command);
        Assert.True(validation.IsValid, $"{screenOption}: {validation}");
        var result = await setup.Handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var stock = await context.InventoryStocks.AsNoTracking().SingleAsync();
        Assert.Equal(expectedOnHand, stock.OnHandQuantity);
        var movement = await context.InventoryMovements.AsNoTracking().SingleAsync();
        Assert.Equal(movementType, (int)movement.MovementType);
        Assert.Equal(movementReason, (int)movement.MovementReason);
    }

    // The screen before Fase 27B recorded a transfer as "Transferencia salida" at the origin plus "Transferencia entrada" at
    // the destination, one leg at a time. Now a transfer has its own use case: neither leg is a manual movement.
    [Theory]
    [InlineData(6, 99)]
    [InlineData(5, 99)]
    [InlineData(3, 11)]
    public void TransferLegPostedByTheOldScreen_ShouldBeRejected(int movementType, int movementReason)
    {
        var command = DeserializeScreenBody(movementType, movementReason, Guid.NewGuid(), Guid.NewGuid(), quantity: 4);

        var validation = new CreateInventoryMovementCommandValidator().Validate(command);

        Assert.Contains(validation.Errors, x => x.ErrorMessage == CreateInventoryMovementCommandValidator.TransfersElsewhereMessage);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void ReconciliationOfTheHistory_ShouldNotBeAManualMovement(int movementType)
    {
        // A reconciliation records a gap without moving the stock (Fase 27C): a manual movement always moves it.
        var command = DeserializeScreenBody(movementType, 12, Guid.NewGuid(), Guid.NewGuid(), quantity: 2);

        var validation = new CreateInventoryMovementCommandValidator().Validate(command);

        Assert.Contains(validation.Errors, x => x.ErrorMessage == CreateInventoryMovementCommandValidator.ReconciliationElsewhereMessage);
    }

    [Fact]
    public void BodyWithTheOldClientNumbering_ShouldBeRejected()
    {
        // Before the fix the screen's default "Entrada / Compra" posted 0 and 0, which no enum member uses.
        var command = DeserializeScreenBody(0, 0, Guid.NewGuid(), Guid.NewGuid(), quantity: 1);

        var validation = new CreateInventoryMovementCommandValidator().Validate(command);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, x => x.PropertyName == nameof(CreateInventoryMovementCommand.MovementType));
        Assert.Contains(validation.Errors, x => x.PropertyName == nameof(CreateInventoryMovementCommand.MovementReason));
    }

    private static CreateInventoryMovementCommand DeserializeScreenBody(
        int movementType,
        int movementReason,
        Guid supplyItemId,
        Guid inventoryLocationId,
        int quantity)
    {
        var body = $$"""
            {
              "movementType": {{movementType}},
              "movementReason": {{movementReason}},
              "lines": [{ "supplyItemId": "{{supplyItemId}}", "inventoryLocationId": "{{inventoryLocationId}}", "quantity": {{quantity}} }]
            }
            """;

        // The API binds request bodies with the web defaults (camelCase, enums as numbers).
        return JsonSerializer.Deserialize<CreateInventoryMovementCommand>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static async Task<StockSetup> SeedSupplyWithStockAsync(decimal onHand)
    {
        var tenantService = new TestCurrentTenantService();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new ApplicationDbContext(options, tenantService, new NullMediator());

        var supply = new SupplyItem(tenantService.TenantId, "Harina", UnitType.Kg, code: "INS-010");
        supply.ApplyInboundCost(5, 0, onHand);
        var origin = new InventoryLocation(tenantService.TenantId, "Almacen", "MAIN", isDefault: true);
        var stock = new InventoryStock(tenantService.TenantId, supply.Id, origin.Id);
        stock.ApplyMovement(onHand, 5, DateTimeOffset.UtcNow);
        context.SupplyItems.Add(supply);
        context.InventoryLocations.Add(origin);
        context.InventoryStocks.Add(stock);
        await context.SaveChangesAsync();

        var handler = new CreateInventoryMovementCommandHandler(context, tenantService, new InventoryLedger(context, new FolioGenerator(context)));
        return new StockSetup(context, handler, supply, origin);
    }

    private sealed record StockSetup(
        ApplicationDbContext Context,
        CreateInventoryMovementCommandHandler Handler,
        SupplyItem Supply,
        InventoryLocation Origin);

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
}
