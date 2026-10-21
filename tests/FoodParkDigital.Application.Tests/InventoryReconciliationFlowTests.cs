using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Inventory.Commands;
using FoodParkDigital.Application.Inventory.Reconciliation;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Revisar que cuadre (Fase 27C, ADR 0039), on a relational database: a stock that something touched without a movement
/// does not add up to its history, the review finds it and says where, and reconciling records the gap in the history
/// without moving the stock.
/// </summary>
public class InventoryReconciliationFlowTests
{
    /// <summary>The clock of the test host: manual movements take their folio from their date, and the rest from it.</summary>
    private static readonly DateTimeOffset Today = new(2026, 2, 16, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Review_WhenEveryStockAddsUp_ShouldFindNothing()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var shop = await SeedShopAsync(host);
        await MoveAsync(host, InventoryMovementType.Exit, shop.Soda, shop.Warehouse, 5m);
        await MoveAsync(host, InventoryMovementType.AdjustmentDecrease, shop.Sugar, shop.Warehouse, 0.25m);

        var review = await ReviewAsync(host);

        Assert.Equal((4, 0), (review.CheckedCount, review.Mismatches.TotalCount));
    }

    [Fact]
    public async Task Review_ShouldFindWhatDoesNotAddUp_ByLocationAndName()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var shop = await SeedShopAsync(host);
        await SetStockAsync(host, shop.Soda, shop.Warehouse, 30m);
        await SetStockAsync(host, shop.Chips, shop.Warehouse, 13m);
        // Sugar in the counter, loaded without a movement.
        await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var stock = new InventoryStock((await db.Tenants.SingleAsync()).Id, shop.Sugar, shop.Counter);
            stock.ApplyMovement(4m, null, Today);
            db.InventoryStocks.Add(stock);
            await db.SaveChangesAsync();
        });

        var review = await ReviewAsync(host);

        Assert.Equal((5, 3), (review.CheckedCount, review.Mismatches.TotalCount));
        Assert.Equal(
            [
                ("Bodega", "Papas 45 g", 13m, 15m, -2m, 1),
                ("Bodega", "Refresco de cola 600 ml", 30m, 24m, 6m, 1),
                ("Mostrador", "Azúcar estándar", 4m, 0m, 4m, 0)
            ],
            review.Mismatches.Items.Select(i => (i.InventoryLocationName, i.SupplyItemName, i.OnHandQuantity, i.HistoryQuantity, i.Difference, i.MovementCount)));
        var counter = await ReviewAsync(host, shop.Counter);
        Assert.Equal(2, counter.CheckedCount);
        Assert.Equal(["Azúcar estándar"], counter.Mismatches.Items.Select(i => i.SupplyItemName));
        Assert.Equal(["Papas 45 g"], (await ReviewAsync(host, search: "papas")).Mismatches.Items.Select(i => i.SupplyItemName));
        Assert.Equal(2, (await ReviewAsync(host, search: "bodega")).Mismatches.TotalCount);
    }

    [Fact]
    public async Task Detail_ShouldSayWhereTheHistoryStopsAddingUp()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var shop = await SeedShopAsync(host);
        var cookies = await AddSupplyAsync(host, "Galletas surtidas");
        // There were 5 before the history started.
        await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var stock = new InventoryStock((await db.Tenants.SingleAsync()).Id, cookies, shop.Warehouse);
            stock.ApplyMovement(5m, null, Today);
            db.InventoryStocks.Add(stock);
            await db.SaveChangesAsync();
        });
        await MoveAsync(host, InventoryMovementType.Entry, cookies, shop.Warehouse, 10m, 8m);   // 0005: 5 → 15
        await SetStockAsync(host, cookies, shop.Warehouse, 18m);                                  // 15 → 18 without a movement
        await MoveAsync(host, InventoryMovementType.Exit, cookies, shop.Warehouse, 2m);           // 0006: 18 → 16
        await MoveAsync(host, InventoryMovementType.Exit, cookies, shop.Warehouse, 1m);           // 0007: 16 → 15
        // 0007 took 2 from the stock while saying it moved 1, and wrote 14.
        await host.QueryAsync(db => db.InventoryMovementLines
            .Where(l => l.SupplyItemId == cookies && l.Movement.MovementNumber == "INV-20260216-0007")
            .ExecuteUpdateAsync(l => l.SetProperty(x => x.QuantityAfter, 14m)));
        await SetStockAsync(host, cookies, shop.Warehouse, 14m);
        await SetStockAsync(host, cookies, shop.Warehouse, 20m);                                  // 14 → 20 after the last one

        var detail = await DetailAsync(host, cookies, shop.Warehouse);

        Assert.Equal(("Galletas surtidas", "Bodega", 20m, 7m, 13m, 3, 3, 4),
            (detail.SupplyItemName, detail.InventoryLocationName, detail.OnHandQuantity, detail.HistoryQuantity, detail.Difference,
                detail.MovementCount, detail.ReviewedCount, detail.EventCount));
        Assert.Equal(
            [
                (InventoryReconciliationEventKind.BeforeFirstMovement, 5m, (decimal?)0m, (decimal?)5m, "INV-20260216-0005", (string?)null, (decimal?)null),
                (InventoryReconciliationEventKind.BetweenMovements, 3m, 15m, 18m, "INV-20260216-0006", "INV-20260216-0005", null),
                (InventoryReconciliationEventKind.InMovement, -1m, 16m, 14m, "INV-20260216-0007", null, -1m),
                (InventoryReconciliationEventKind.AfterLastMovement, 6m, 14m, 20m, "INV-20260216-0007", null, null)
            ],
            detail.Events.Select(e => (e.Kind, e.Quantity, e.QuantityFrom, e.QuantityTo, e.MovementNumber!, e.PreviousMovementNumber, e.MovementQuantity)));
        Assert.Equal(detail.Difference, detail.Events.Sum(e => e.Quantity));
    }

    [Fact]
    public async Task Detail_ForAStockWithoutHistory_ShouldSaySo_AndForAnUnknownOne_ShouldNotFindIt()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var shop = await SeedShopAsync(host);
        await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var stock = new InventoryStock((await db.Tenants.SingleAsync()).Id, shop.Chips, shop.Counter);
            stock.ApplyMovement(3m, null, Today);
            db.InventoryStocks.Add(stock);
            await db.SaveChangesAsync();
        });

        var detail = await DetailAsync(host, shop.Chips, shop.Counter);

        Assert.Equal((3m, 0m, 3m, 0), (detail.OnHandQuantity, detail.HistoryQuantity, detail.Difference, detail.MovementCount));
        Assert.Equal([(InventoryReconciliationEventKind.NoHistory, 3m)], detail.Events.Select(e => (e.Kind, e.Quantity)));
        // An item that never touched the location adds up.
        var untouched = await DetailAsync(host, shop.Sugar, shop.Counter);
        Assert.Equal((0m, 0m, 0), (untouched.Difference, untouched.OnHandQuantity, untouched.EventCount));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => DetailAsync(host, Guid.NewGuid(), shop.Counter));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => DetailAsync(host, shop.Chips, Guid.NewGuid()));
    }

    [Fact]
    public async Task Reconcile_ShouldRecordTheGapsInTheHistory_WithoutMovingTheStock()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var shop = await SeedShopAsync(host);
        await SetStockAsync(host, shop.Soda, shop.Warehouse, 30m);
        await SetStockAsync(host, shop.Sugar, shop.Warehouse, 9.75m);

        var reconciled = await ReconcileAsync(host, shop.Warehouse, (shop.Soda, 6m), (shop.Sugar, -0.25m));

        Assert.True(reconciled.IsSuccess, reconciled.Error);
        Assert.Equal((2, 0), (reconciled.Value.ReconciledCount, reconciled.Value.AlreadyAddedUpCount));
        Assert.Equal(
            [("INV-20260216-0005", InventoryMovementType.AdjustmentIncrease), ("INV-20260216-0006", InventoryMovementType.AdjustmentDecrease)],
            reconciled.Value.Movements.Select(m => (m.MovementNumber, m.MovementType)));
        var movements = await host.QueryAsync(db => db.InventoryMovements
            .Include(m => m.Lines)
            .Where(m => m.MovementReason == InventoryMovementReason.LedgerReconciliation)
            .OrderBy(m => m.MovementNumber)
            .ToListAsync());
        Assert.Equal(
            [
                (InventoryMovementType.AdjustmentIncrease, 0m, "Cuadre del historial: la existencia no cambia.", shop.Soda, 6m, 30m, 30m, (decimal?)null, "Existencia 30; el historial sumaba 24"),
                (InventoryMovementType.AdjustmentDecrease, 0m, "Cuadre del historial: la existencia no cambia.", shop.Sugar, 0.25m, 9.75m, 9.75m, null, "Existencia 9.75 kg; el historial sumaba 10 kg")
            ],
            movements.Select(m => (m.MovementType, m.TotalAmount, m.Notes!, m.Lines.Single().SupplyItemId, m.Lines.Single().Quantity,
                m.Lines.Single().QuantityBefore, m.Lines.Single().QuantityAfter, m.Lines.Single().UnitCost, m.Lines.Single().Notes!)));
        // The stocks, their costs and the average costs stay; the review finds nothing now.
        Assert.Equal([9.75m, 15m, 30m], await StocksAsync(host, shop.Warehouse));
        Assert.Equal([12m, 30m], await host.QueryAsync(async db =>
            (await db.SupplyItems.Where(s => s.Id == shop.Soda || s.Id == shop.Sugar).Select(s => s.AverageCost).ToListAsync()).Order().ToList()));
        Assert.Equal(0, (await ReviewAsync(host)).Mismatches.TotalCount);
        var detail = await DetailAsync(host, shop.Soda, shop.Warehouse);
        Assert.Equal(
            [(InventoryReconciliationEventKind.BetweenMovements, 6m), (InventoryReconciliationEventKind.Reconciled, -6m)],
            detail.Events.Select(e => (e.Kind, e.Quantity)));
        var audit = await host.QueryAsync(db => db.AuditLogs.SingleAsync(l => l.Action == "InventoryLedgerReconciled"));
        Assert.Equal(shop.Warehouse.ToString(), audit.EntityId);
        Assert.Contains("\"Movements\":[\"INV-20260216-0005\",\"INV-20260216-0006\"]", audit.Metadata);

        // Sent again (a double tap), it finds them added up and records nothing.
        var again = await ReconcileAsync(host, shop.Warehouse, (shop.Soda, 6m), (shop.Sugar, -0.25m));
        Assert.True(again.IsSuccess, again.Error);
        Assert.Equal((0, 2, 0), (again.Value.ReconciledCount, again.Value.AlreadyAddedUpCount, again.Value.Movements.Count));
        Assert.Equal(2, await host.QueryAsync(db => db.InventoryMovements.CountAsync(m => m.MovementReason == InventoryMovementReason.LedgerReconciliation)));
    }

    [Fact]
    public async Task Reconcile_WhenWhatDoesNotAddUpChanged_ShouldRecordNothing()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var shop = await SeedShopAsync(host);
        await SetStockAsync(host, shop.Soda, shop.Warehouse, 30m);
        await SetStockAsync(host, shop.Chips, shop.Warehouse, 13m);

        var refused = await ReconcileAsync(host, shop.Warehouse, (shop.Soda, 5m), (shop.Chips, -2m));

        Assert.False(refused.IsSuccess);
        Assert.Equal("Cambió lo que no cuadra de: Refresco de cola 600 ml (ahora +6). Vuelve a revisar antes de cuadrar.", refused.Error);
        Assert.StartsWith(ReconcileInventoryLedgerCommandHandler.ChangedMessagePrefix, refused.Error);
        Assert.Equal(0, await host.QueryAsync(db => db.InventoryMovements.CountAsync(m => m.MovementReason == InventoryMovementReason.LedgerReconciliation)));
    }

    [Fact]
    public async Task Reconcile_ShouldRefuseALocationOrAnItemOfNobody()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var shop = await SeedShopAsync(host);

        var noLocation = await ReconcileAsync(host, Guid.NewGuid(), (shop.Soda, 1m));
        var noItem = await ReconcileAsync(host, shop.Warehouse, (shop.Soda, 1m), (Guid.NewGuid(), 1m));

        Assert.Equal("La ubicación no existe.", noLocation.Error);
        Assert.Equal("Uno o más artículos no existen.", noItem.Error);
    }

    [Fact]
    public void Validator_ShouldRefuseWhatCannotBeReconciled()
    {
        var validator = new ReconcileInventoryLedgerCommandValidator();
        var supply = Guid.NewGuid();
        ReconcileInventoryLedgerCommand Valid() => new()
        {
            InventoryLocationId = Guid.NewGuid(),
            Lines = [new InventoryReconciliationLineRequest { SupplyItemId = supply, ExpectedDifference = 2m }]
        };
        InventoryReconciliationLineRequest Line(decimal difference) => new() { SupplyItemId = Guid.NewGuid(), ExpectedDifference = difference };

        Assert.True(validator.Validate(Valid()).IsValid);
        var refusals = new (ReconcileInventoryLedgerCommand Command, string Message)[]
        {
            (Valid() with { InventoryLocationId = Guid.Empty }, "Elige la ubicación que vas a cuadrar."),
            (Valid() with { Lines = [] }, "Elige al menos un artículo."),
            (Valid() with { Lines = Enumerable.Range(0, 201).Select(_ => Line(1m)).ToList() }, "Se cuadran hasta 200 artículos a la vez."),
            (Valid() with { Lines = [Valid().Lines[0], Valid().Lines[0]] }, "Un artículo va una sola vez."),
            (Valid() with { Lines = [Line(0m)] }, "La diferencia revisada no es válida: vuelve a revisar."),
            (Valid() with { Lines = [Line(0.00001m)] }, "La diferencia revisada no es válida: vuelve a revisar.")
        };
        foreach (var (command, message) in refusals)
            Assert.Contains(validator.Validate(command).Errors, e => e.ErrorMessage == message);
        Assert.True(validator.Validate(Valid() with { Lines = [Line(-0.0001m)] }).IsValid);
    }

    private sealed record Shop(Guid Warehouse, Guid Counter, Guid Soda, Guid Chips, Guid Sugar);

    /// <summary>A shop with a warehouse (24 sodas at $12, 15 chips at $9, 10 kg of sugar at $30) and a counter with 6 sodas.</summary>
    private static async Task<Shop> SeedShopAsync(SqliteTestHost host)
    {
        var tenant = new Tenant("Abarrotes Lupita", "abarrotes-lupita", BusinessType.Retail, "lupita@demo.com");
        host.SetTenant(tenant.Id);
        var warehouse = new InventoryLocation(tenant.Id, "Bodega", "BOD", isDefault: true);
        var counter = new InventoryLocation(tenant.Id, "Mostrador", "MOS");
        var soda = new SupplyItem(tenant.Id, "Refresco de cola 600 ml", UnitType.Unit, code: "REF-600", categoryName: "Bebidas");
        var chips = new SupplyItem(tenant.Id, "Papas 45 g", UnitType.Unit, barcode: "7501055300", categoryName: "Botanas");
        var sugar = new SupplyItem(tenant.Id, "Azúcar estándar", UnitType.Kg, categoryName: "Abarrotes");
        await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.Tenants.Add(tenant);
            db.TenantSettings.Add(new TenantSettings(tenant.Id, TenantCapability.Retail | TenantCapability.Inventory));
            db.InventoryLocations.AddRange(warehouse, counter);
            db.SupplyItems.AddRange(soda, chips, sugar);
            await db.SaveChangesAsync();
        });

        await MoveAsync(host, InventoryMovementType.Entry, soda.Id, warehouse.Id, 24m, 12m);
        await MoveAsync(host, InventoryMovementType.Entry, chips.Id, warehouse.Id, 15m, 9m);
        await MoveAsync(host, InventoryMovementType.Entry, sugar.Id, warehouse.Id, 10m, 30m);
        await MoveAsync(host, InventoryMovementType.Entry, soda.Id, counter.Id, 6m, 12m);
        return new Shop(warehouse.Id, counter.Id, soda.Id, chips.Id, sugar.Id);
    }

    private static async Task<Guid> AddSupplyAsync(SqliteTestHost host, string name)
        => await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var supply = new SupplyItem((await db.Tenants.SingleAsync()).Id, name, UnitType.Unit);
            db.SupplyItems.Add(supply);
            await db.SaveChangesAsync();
            return supply.Id;
        });

    private static async Task MoveAsync(
        SqliteTestHost host, InventoryMovementType type, Guid supplyId, Guid locationId, decimal quantity, decimal? unitCost = null)
    {
        var moved = await host.HandleAsync<CreateInventoryMovementCommandHandler, Result<Inventory.DTOs.InventoryMovementDto>>(h => h.Handle(
            new CreateInventoryMovementCommand
            {
                MovementDate = Today,
                MovementType = type,
                MovementReason = type == InventoryMovementType.Entry ? InventoryMovementReason.Purchase : InventoryMovementReason.ManualAdjustment,
                Lines = [new CreateInventoryMovementLineRequest { SupplyItemId = supplyId, InventoryLocationId = locationId, Quantity = quantity, UnitCost = unitCost }]
            }, default));
        Assert.True(moved.IsSuccess, moved.Error);
    }

    /// <summary>What a script or an old version could leave: the stock changed without a movement.</summary>
    private static Task SetStockAsync(SqliteTestHost host, Guid supplyId, Guid locationId, decimal onHand)
        => host.QueryAsync(db => db.InventoryStocks
            .Where(s => s.SupplyItemId == supplyId && s.InventoryLocationId == locationId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.OnHandQuantity, onHand)));

    private static Task<InventoryReconciliationDto> ReviewAsync(SqliteTestHost host, Guid? locationId = null, string? search = null)
        => host.HandleAsync<GetInventoryReconciliationQueryHandler, InventoryReconciliationDto>(h => h.Handle(
            new GetInventoryReconciliationQuery { InventoryLocationId = locationId, Search = search }, default));

    private static Task<InventoryReconciliationDetailDto> DetailAsync(SqliteTestHost host, Guid supplyId, Guid locationId)
        => host.HandleAsync<GetInventoryReconciliationDetailQueryHandler, InventoryReconciliationDetailDto>(h => h.Handle(
            new GetInventoryReconciliationDetailQuery(supplyId, locationId), default));

    private static Task<Result<InventoryReconciliationResultDto>> ReconcileAsync(
        SqliteTestHost host, Guid locationId, params (Guid SupplyId, decimal Difference)[] lines)
        => host.HandleAsync<ReconcileInventoryLedgerCommandHandler, Result<InventoryReconciliationResultDto>>(h => h.Handle(
            new ReconcileInventoryLedgerCommand
            {
                InventoryLocationId = locationId,
                Lines = lines.Select(l => new InventoryReconciliationLineRequest { SupplyItemId = l.SupplyId, ExpectedDifference = l.Difference }).ToList()
            }, default));

    private static Task<List<decimal>> StocksAsync(SqliteTestHost host, Guid locationId)
        => host.QueryAsync(async db => (await db.InventoryStocks.Where(s => s.InventoryLocationId == locationId).Select(s => s.OnHandQuantity).ToListAsync())
            .Order().ToList());
}
