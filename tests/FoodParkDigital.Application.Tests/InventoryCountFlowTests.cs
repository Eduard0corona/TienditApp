using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Inventory.Commands;
using FoodParkDigital.Application.Inventory.Counts;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Conteo físico (Fase 27A, ADR 0037), on a relational database: a count takes the items of a location, records what was
/// counted with what the system had at that moment, and posting it moves the stock of now by each difference with
/// adjustments of reason InventoryCount, so what was sold after counting is kept; once posted or cancelled it no longer
/// changes.
/// </summary>
public class InventoryCountFlowTests
{
    private const string CountFolio = "CNT-20260216-0001";

    [Fact]
    public async Task Start_ShouldTakeTheItemsWithStockInTheLocation_AllOfThemOneCategoryOrTheChosenOnes()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var shop = await SeedShopAsync(host);

        var all = await StartAsync(host, new StartInventoryCountCommand { InventoryLocationId = shop.Warehouse, Notes = "  Cierre de mes " });
        var again = await StartAsync(host, new StartInventoryCountCommand { InventoryLocationId = shop.Warehouse });

        Assert.True(all.IsSuccess, all.Error);
        Assert.Equal((CountFolio, "Bodega", InventoryCountStatus.InProgress, 3, 0, "sqlite-test", "Cierre de mes"),
            (all.Value.CountNumber, all.Value.InventoryLocationName, all.Value.Status, all.Value.LineCount, all.Value.CountedLineCount,
                all.Value.StartedBy, all.Value.Notes));
        Assert.Equal((0m, 0m, 0), (all.Value.SurplusValue, all.Value.ShortageValue, all.Value.DifferenceLineCount));
        Assert.Equal($"Ya hay un conteo abierto de Bodega ({CountFolio}): termínalo o cancélalo antes de empezar otro.", again.Error);

        await HandleAsync<CancelInventoryCountCommandHandler, InventoryCountDto>(host,
            h => h.Handle(new CancelInventoryCountCommand { CountId = all.Value.Id }, default));
        var drinks = await StartAsync(host, new StartInventoryCountCommand { InventoryLocationId = shop.Warehouse, CategoryName = " bebidas " });
        var chosen = await StartAsync(host, new StartInventoryCountCommand { InventoryLocationId = shop.Counter, SupplyItemIds = [shop.Chips, shop.Chips] });

        Assert.True(drinks.IsSuccess, drinks.Error);
        Assert.Equal(("CNT-20260216-0002", "bebidas", 1), (drinks.Value.CountNumber, drinks.Value.CategoryName, drinks.Value.LineCount));
        Assert.Equal(["Refresco de cola 600 ml"], (await LinesAsync(host, drinks.Value.Id)).Items.Select(l => l.SupplyItemName));
        Assert.True(chosen.IsSuccess, chosen.Error);
        var chips = Assert.Single((await LinesAsync(host, chosen.Value.Id)).Items);
        // Counting blind: nothing of what the system has until it is counted.
        Assert.Equal(("Papas 45 g", (decimal?)null, (decimal?)null), (chips.SupplyItemName, chips.CountedQuantity, chips.CurrentQuantity));
        var started = await host.QueryAsync(db => db.AuditLogs.Where(l => l.Action == "InventoryCountStarted").ToListAsync());
        Assert.Equal(3, started.Count);
        Assert.Contains(started, l => l.Metadata!.Contains($"\"CountNumber\":\"{CountFolio}\"") && l.Metadata.Contains("\"Items\":3"));
    }

    [Fact]
    public async Task Start_ShouldBeRefused_ForAnInactiveLocation_OrALocationWithNothingToCount()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var shop = await SeedShopAsync(host);
        var empty = await AddLocationAsync(host, "Cámara fría", "FRIO");
        await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            (await db.InventoryLocations.SingleAsync(l => l.Id == shop.Counter)).SetActive(false);
            await db.SaveChangesAsync();
        });

        var inactive = await StartAsync(host, new StartInventoryCountCommand { InventoryLocationId = shop.Counter });
        var nothing = await StartAsync(host, new StartInventoryCountCommand { InventoryLocationId = empty });
        var noCategory = await StartAsync(host, new StartInventoryCountCommand { InventoryLocationId = shop.Warehouse, CategoryName = "Limpieza" });
        var missing = await StartAsync(host, new StartInventoryCountCommand { InventoryLocationId = shop.Warehouse, SupplyItemIds = [Guid.NewGuid()] });

        Assert.Equal("La ubicación está inactiva: actívala para contarla.", inactive.Error);
        Assert.Equal("No hay artículos con existencias en Cámara fría: elige los artículos que vas a contar.", nothing.Error);
        Assert.Equal("No hay artículos de «Limpieza» con existencias en Bodega: elige los artículos que vas a contar.", noCategory.Error);
        Assert.Equal("Uno o más artículos no existen o están inactivos.", missing.Error);
        Assert.Equal(0, await host.QueryAsync(db => db.InventoryCounts.CountAsync()));
    }

    [Fact]
    public async Task Post_ShouldMoveTheStockByEachDifference_KeepingWhatWasSoldAfterCounting()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var shop = await SeedShopAsync(host);
        var count = (await StartAsync(host, new StartInventoryCountCommand { InventoryLocationId = shop.Warehouse })).Value;
        var lines = (await LinesAsync(host, count.Id)).Items.ToDictionary(l => l.SupplyItemId, l => l.Id);

        var recorded = await RecordAsync(host, count.Id,
            (lines[shop.Soda], 22m, "Dos latas abolladas"), (lines[shop.Chips], 15m, null), (lines[shop.Sugar], 10.5m, null));
        // Three sold after the soda was counted: the shelf has 19, not the 22 that were counted.
        await MoveAsync(host, InventoryMovementType.Exit, shop.Soda, shop.Warehouse, 3m);

        Assert.True(recorded.IsSuccess, recorded.Error);
        // Each item added or recorded raises the revision: three added, three recorded.
        Assert.Equal((3, 6), (recorded.Value.Count.CountedLineCount, recorded.Value.Count.Revision));
        var soda = recorded.Value.Lines.Single(l => l.SupplyItemId == shop.Soda);
        Assert.Equal(((decimal?)22m, (decimal?)24m, (decimal?)-2m, "sqlite-test", "Dos latas abolladas"),
            (soda.CountedQuantity, soda.SystemQuantity, soda.Difference, soda.CountedBy, soda.Notes));
        var reviewPage = await LinesAsync(host, count.Id, withDifference: true);
        var review = reviewPage.Items;
        Assert.Equal(["Azúcar estándar", "Refresco de cola 600 ml"], review.Select(l => l.SupplyItemName));
        Assert.Equal(((decimal?)21m, (decimal?)19m, (decimal?)-24m), (review[1].CurrentQuantity, review[1].ResultingQuantity, review[1].DifferenceValue));
        Assert.Equal(((decimal?)10m, (decimal?)10.5m, (decimal?)15m), (review[0].CurrentQuantity, review[0].ResultingQuantity, review[0].DifferenceValue));
        var reviewed = await CountAsync(host, count.Id);
        Assert.Equal((2, (decimal?)15m, (decimal?)24m), (reviewed.DifferenceLineCount, reviewed.SurplusValue, reviewed.ShortageValue));
        // The items reviewed are those of the revision posted.
        Assert.Equal(reviewed.Revision, reviewPage.Revision);

        var posted = await PostAsync(host, count.Id, reviewed.Revision);

        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Equal((InventoryCountStatus.Posted, "sqlite-test", (decimal?)15m, (decimal?)24m),
            (posted.Value.Status, posted.Value.PostedBy, posted.Value.SurplusValue, posted.Value.ShortageValue));
        Assert.StartsWith("INV-", posted.Value.IncreaseMovementNumber);
        Assert.StartsWith("INV-", posted.Value.DecreaseMovementNumber);
        Assert.Equal([10.5m, 15m, 19m], await StocksAsync(host, shop.Warehouse));
        Assert.Equal(6m, await host.QueryAsync(db => db.InventoryStocks.Where(s => s.InventoryLocationId == shop.Counter).SumAsync(s => s.OnHandQuantity)));

        var movements = await host.QueryAsync(db => db.InventoryMovements.Include(m => m.Lines)
            .Where(m => m.MovementReason == InventoryMovementReason.InventoryCount).OrderBy(m => m.MovementType).ToListAsync());
        Assert.Equal(
            [(InventoryMovementType.AdjustmentIncrease, CountFolio, InventoryCountPostings.IncreaseReference(count.Id), 15m),
             (InventoryMovementType.AdjustmentDecrease, CountFolio, InventoryCountPostings.DecreaseReference(count.Id), 24m)],
            movements.Select(m => (m.MovementType, m.ReferenceNumber!, m.ExternalReference!, m.TotalAmount)));
        var sugar = Assert.Single(movements[0].Lines);
        Assert.Equal((0.5m, 10m, 10.5m, (decimal?)30m, "Contado 10.5 kg; sistema al contar 10 kg"),
            (sugar.Quantity, sugar.QuantityBefore, sugar.QuantityAfter, sugar.UnitCost, sugar.Notes));
        var sold = Assert.Single(movements[1].Lines);
        Assert.Equal((2m, 21m, 19m, "Contado 22; sistema al contar 24"), (sold.Quantity, sold.QuantityBefore, sold.QuantityAfter, sold.Notes));
        // Adjustments move stock the business already valued: the average cost stays.
        Assert.Equal(30m, await host.QueryAsync(db => db.SupplyItems.Where(s => s.Id == shop.Sugar).Select(s => s.AverageCost).SingleAsync()));

        var history = (await LinesAsync(host, count.Id)).Items.ToDictionary(l => l.SupplyItemId);
        Assert.Equal(((decimal?)21m, (decimal?)19m, (decimal?)-24m), (history[shop.Soda].CurrentQuantity, history[shop.Soda].ResultingQuantity, history[shop.Soda].DifferenceValue));
        Assert.Equal(((decimal?)null, (decimal?)null, (decimal?)0m), (history[shop.Chips].CurrentQuantity, history[shop.Chips].ResultingQuantity, history[shop.Chips].Difference));
        var log = await host.QueryAsync(db => db.AuditLogs.SingleAsync(l => l.Action == "InventoryCountPosted"));
        Assert.Equal(count.Id.ToString(), log.EntityId);

        // Posted, it no longer changes.
        Assert.Equal(InventoryCount.NotOpenMessage, (await PostAsync(host, count.Id, posted.Value.Revision)).Error);
        Assert.Equal(InventoryCount.NotOpenMessage, (await RecordAsync(host, count.Id, (lines[shop.Chips], 1m, null))).Error);
        Assert.Equal(2, await host.QueryAsync(db => db.InventoryMovements.CountAsync(m => m.MovementReason == InventoryMovementReason.InventoryCount)));
    }

    [Fact]
    public async Task Post_ShouldBeRefused_WhenTheCountChanged_NothingWasCounted_OrAnItemWouldEndBelowZero()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var shop = await SeedShopAsync(host);
        var count = (await StartAsync(host, new StartInventoryCountCommand { InventoryLocationId = shop.Warehouse, CategoryName = "Bebidas" })).Value;
        var soda = Assert.Single((await LinesAsync(host, count.Id)).Items).Id;

        var nothing = await PostAsync(host, count.Id, count.Revision);
        await RecordAsync(host, count.Id, (soda, 2m, null));
        var stale = await PostAsync(host, count.Id, count.Revision);
        // 23 sold after counting 2 of 24: posting would leave -21.
        await MoveAsync(host, InventoryMovementType.Exit, shop.Soda, shop.Warehouse, 23m);
        var negative = await PostAsync(host, count.Id, (await CountAsync(host, count.Id)).Revision);

        Assert.Equal(InventoryCount.NothingCountedMessage, nothing.Error);
        Assert.Equal(PostInventoryCountCommandHandler.ChangedMessage, stale.Error);
        Assert.Equal("Quedarían en negativo porque se movieron después de contarlos: Refresco de cola 600 ml (-21). Vuélvelos a contar.", negative.Error);
        Assert.Equal(0, await host.QueryAsync(db => db.InventoryMovements.CountAsync(m => m.MovementReason == InventoryMovementReason.InventoryCount)));

        // Corrected, it is still measured against the 24 it was counted against, so it is still refused.
        var corrected = await RecordAsync(host, count.Id, (soda, 0m, null));
        var stillNegative = await PostAsync(host, count.Id, corrected.Value.Count.Revision);
        // Counted again from scratch (cleared, then counted), against what the system has now.
        await RecordAsync(host, count.Id, (soda, null, null));
        var recount = await RecordAsync(host, count.Id, (soda, 0m, null));
        var posted = await PostAsync(host, count.Id, recount.Value.Count.Revision);

        Assert.Equal((decimal?)24m, corrected.Value.Lines.Single().SystemQuantity);
        Assert.StartsWith("Quedarían en negativo porque se movieron después de contarlos: Refresco de cola 600 ml (-23).", stillNegative.Error);
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Equal((decimal?)-1m, recount.Value.Lines.Single().Difference);
        Assert.Equal(0m, await host.QueryAsync(db => db.InventoryStocks.Where(s => s.SupplyItemId == shop.Soda && s.InventoryLocationId == shop.Warehouse).Select(s => s.OnHandQuantity).SingleAsync()));
    }

    [Fact]
    public async Task Post_WithoutDifferences_ShouldCloseTheCount_WithoutMovements()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var shop = await SeedShopAsync(host);
        var count = (await StartAsync(host, new StartInventoryCountCommand { InventoryLocationId = shop.Warehouse, CategoryName = "Botanas" })).Value;
        var chips = Assert.Single((await LinesAsync(host, count.Id)).Items).Id;
        var recorded = await RecordAsync(host, count.Id, (chips, 15m, null));

        var posted = await PostAsync(host, count.Id, recorded.Value.Count.Revision);

        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Equal((InventoryCountStatus.Posted, (Guid?)null, (Guid?)null, (decimal?)0m, (decimal?)0m),
            (posted.Value.Status, posted.Value.IncreaseMovementId, posted.Value.DecreaseMovementId, posted.Value.SurplusValue, posted.Value.ShortageValue));
        Assert.Equal(0, await host.QueryAsync(db => db.InventoryMovements.CountAsync(m => m.MovementReason == InventoryMovementReason.InventoryCount)));
    }

    [Fact]
    public async Task Record_ShouldRefuseWhatTheUnitDoesNotTake_AndItemsOfAnotherCount_AndClearAQuantity()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var shop = await SeedShopAsync(host);
        var count = (await StartAsync(host, new StartInventoryCountCommand { InventoryLocationId = shop.Warehouse })).Value;
        var other = (await StartAsync(host, new StartInventoryCountCommand { InventoryLocationId = shop.Counter })).Value;
        var lines = (await LinesAsync(host, count.Id)).Items.ToDictionary(l => l.SupplyItemId, l => l.Id);
        var otherLine = Assert.Single((await LinesAsync(host, other.Id)).Items).Id;

        var half = await RecordAsync(host, count.Id, (lines[shop.Soda], 2.5m, null));
        var foreign = await RecordAsync(host, count.Id, (otherLine, 1m, null));
        await RecordAsync(host, count.Id, (lines[shop.Soda], 20m, null));
        var cleared = await RecordAsync(host, count.Id, (lines[shop.Soda], null, null));

        Assert.Equal("Refresco de cola 600 ml: la cantidad contada va de 0 a 9,999,999, en piezas enteras.", half.Error);
        Assert.Equal("Uno o más artículos no son de este conteo.", foreign.Error);
        Assert.True(cleared.IsSuccess, cleared.Error);
        Assert.Equal(((decimal?)null, (decimal?)null, 0), (cleared.Value.Lines.Single().CountedQuantity, cleared.Value.Lines.Single().SystemQuantity, cleared.Value.Count.CountedLineCount));
        // Counting it the first time leaves no entry; clearing what was counted does, with what it was.
        var corrected = await host.QueryAsync(db => db.AuditLogs.SingleAsync(l => l.Action == "InventoryCountCorrected"));
        Assert.Equal((count.Id.ToString(), "sqlite-test"), (corrected.EntityId, corrected.PerformedBy));
        Assert.Contains("\"From\":20", corrected.Metadata);
        Assert.Contains("\"To\":null", corrected.Metadata);
        var validation = new RecordInventoryCountCommandValidator().Validate(new RecordInventoryCountCommand
        {
            CountId = count.Id,
            Lines = [new InventoryCountEntry { LineId = lines[shop.Sugar], CountedQuantity = -1m }, new InventoryCountEntry { LineId = lines[shop.Sugar], CountedQuantity = 1.2345m }]
        });
        Assert.Equal(
            ["Un artículo va una sola vez.", "La cantidad contada va de 0 a 9,999,999, con máximo 3 decimales.", "La cantidad contada va de 0 a 9,999,999, con máximo 3 decimales."],
            validation.Errors.Select(e => e.ErrorMessage));
        // A list with a hole, or none, is refused as invalid data, not as a failure of the server.
        var hole = new RecordInventoryCountCommandValidator().Validate(new RecordInventoryCountCommand { CountId = count.Id, Lines = [null!] });
        var none = new RecordInventoryCountCommandValidator().Validate(new RecordInventoryCountCommand { CountId = count.Id, Lines = null! });
        Assert.Equal(["Falta un artículo en la lista."], hole.Errors.Select(e => e.ErrorMessage));
        Assert.Equal(["Escribe lo que contaste de al menos un artículo."], none.Errors.Select(e => e.ErrorMessage));
        Assert.False(new StartInventoryCountCommandValidator().Validate(new StartInventoryCountCommand { InventoryLocationId = shop.Warehouse, SupplyItemIds = null! }).IsValid);
        Assert.False(new AddInventoryCountItemsCommandValidator().Validate(new AddInventoryCountItemsCommand { CountId = count.Id, SupplyItemIds = null! }).IsValid);
    }

    [Fact]
    public async Task AddItems_ShouldAddWhatTurnedUp_KeepingTheItemsTheCountHas()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var shop = await SeedShopAsync(host);
        var count = (await StartAsync(host, new StartInventoryCountCommand { InventoryLocationId = shop.Counter })).Value;
        var soda = Assert.Single((await LinesAsync(host, count.Id)).Items).Id;
        await RecordAsync(host, count.Id, (soda, 5m, null));

        var added = await HandleAsync<AddInventoryCountItemsCommandHandler, InventoryCountDto>(host,
            h => h.Handle(new AddInventoryCountItemsCommand { CountId = count.Id, SupplyItemIds = [shop.Chips, shop.Soda, shop.Chips] }, default));
        var missing = await HandleAsync<AddInventoryCountItemsCommandHandler, InventoryCountDto>(host,
            h => h.Handle(new AddInventoryCountItemsCommand { CountId = count.Id, SupplyItemIds = [Guid.NewGuid()] }, default));

        Assert.True(added.IsSuccess, added.Error);
        Assert.Equal((2, 1, 3), (added.Value.LineCount, added.Value.CountedLineCount, added.Value.Revision));
        Assert.Equal("Uno o más artículos no existen o están inactivos.", missing.Error);
        var pending = Assert.Single((await LinesAsync(host, count.Id, pending: true)).Items);
        Assert.Equal(("Papas 45 g", (decimal?)null), (pending.SupplyItemName, pending.CurrentQuantity));
        var log = await host.QueryAsync(db => db.AuditLogs.SingleAsync(l => l.Action == "InventoryCountItemsAdded"));
        Assert.Contains(shop.Chips.ToString(), log.Metadata);
        Assert.DoesNotContain(shop.Soda.ToString(), log.Metadata);
        Assert.Equal("Papas 45 g", Assert.Single((await LinesAsync(host, count.Id, search: "7501055300")).Items).SupplyItemName);
        Assert.Equal("Refresco de cola 600 ml", Assert.Single((await LinesAsync(host, count.Id, search: "ref-600")).Items).SupplyItemName);
    }

    [Fact]
    public async Task Cancel_ShouldCloseTheCount_WithoutMovingAnything()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var shop = await SeedShopAsync(host);
        var count = (await StartAsync(host, new StartInventoryCountCommand { InventoryLocationId = shop.Warehouse })).Value;
        var soda = (await LinesAsync(host, count.Id)).Items.First(l => l.SupplyItemId == shop.Soda).Id;
        await RecordAsync(host, count.Id, (soda, 1m, null));

        var cancelled = await HandleAsync<CancelInventoryCountCommandHandler, InventoryCountDto>(host,
            h => h.Handle(new CancelInventoryCountCommand { CountId = count.Id, Reason = "  Se contó la ubicación equivocada " }, default));
        var again = await HandleAsync<CancelInventoryCountCommandHandler, InventoryCountDto>(host,
            h => h.Handle(new CancelInventoryCountCommand { CountId = count.Id }, default));

        Assert.True(cancelled.IsSuccess, cancelled.Error);
        Assert.Equal((InventoryCountStatus.Cancelled, "sqlite-test", "Se contó la ubicación equivocada", (decimal?)null, (decimal?)null),
            (cancelled.Value.Status, cancelled.Value.CancelledBy, cancelled.Value.CancelReason, cancelled.Value.SurplusValue, cancelled.Value.ShortageValue));
        Assert.Equal(InventoryCount.NotOpenMessage, again.Error);
        Assert.Equal([10m, 15m, 24m], await StocksAsync(host, shop.Warehouse));
        Assert.Equal(1, await host.QueryAsync(db => db.AuditLogs.CountAsync(l => l.Action == "InventoryCountCancelled")));
        var line = (await LinesAsync(host, count.Id)).Items.Single(l => l.SupplyItemId == shop.Soda);
        Assert.Equal(((decimal?)1m, (decimal?)null, (decimal?)null), (line.CountedQuantity, line.CurrentQuantity, line.DifferenceValue));

        var list = await host.HandleAsync<GetInventoryCountsQueryHandler, PaginatedList<InventoryCountListItemDto>>(
            h => h.Handle(new GetInventoryCountsQuery { Status = InventoryCountStatus.Cancelled }, default));
        Assert.Equal((CountFolio, "Bodega", 3, 1), (list.Items.Single().CountNumber, list.Items.Single().InventoryLocationName, list.Items.Single().LineCount, list.Items.Single().CountedLineCount));
        var byLocation = await host.HandleAsync<GetInventoryCountsQueryHandler, PaginatedList<InventoryCountListItemDto>>(
            h => h.Handle(new GetInventoryCountsQuery { Search = "bode" }, default));
        var byFolio = await host.HandleAsync<GetInventoryCountsQueryHandler, PaginatedList<InventoryCountListItemDto>>(
            h => h.Handle(new GetInventoryCountsQuery { Search = "0002" }, default));
        Assert.Equal([CountFolio], byLocation.Items.Select(c => c.CountNumber));
        Assert.Empty(byFolio.Items);
    }

    [Fact]
    public void ManualMovement_ShouldNotUseTheReferencesOfACount()
    {
        var validation = new CreateInventoryMovementCommandValidator().Validate(new CreateInventoryMovementCommand
        {
            MovementType = InventoryMovementType.AdjustmentIncrease,
            MovementReason = InventoryMovementReason.InventoryCount,
            ExternalReference = $" count:{Guid.NewGuid():N}:IN",
            Lines = [new CreateInventoryMovementLineRequest { SupplyItemId = Guid.NewGuid(), InventoryLocationId = Guid.NewGuid(), Quantity = 1 }]
        });

        Assert.Contains(validation.Errors, e => e.ErrorMessage == "La referencia externa usa un prefijo reservado por el sistema (ORDER:, PO: o COUNT:).");
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

    private static async Task<Guid> AddLocationAsync(SqliteTestHost host, string name, string code)
        => await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var location = new InventoryLocation(db.Tenants.Local.FirstOrDefault()?.Id ?? (await db.Tenants.SingleAsync()).Id, name, code);
            db.InventoryLocations.Add(location);
            await db.SaveChangesAsync();
            return location.Id;
        });

    /// <summary>A movement as the inventory screen posts it (a sale takes stock the same way).</summary>
    private static async Task MoveAsync(
        SqliteTestHost host, InventoryMovementType type, Guid supplyId, Guid locationId, decimal quantity, decimal? unitCost = null)
    {
        var moved = await host.HandleAsync<CreateInventoryMovementCommandHandler, Result<Inventory.DTOs.InventoryMovementDto>>(h => h.Handle(
            new CreateInventoryMovementCommand
            {
                MovementType = type,
                MovementReason = type == InventoryMovementType.Entry ? InventoryMovementReason.Purchase : InventoryMovementReason.Consumption,
                Lines = [new CreateInventoryMovementLineRequest { SupplyItemId = supplyId, InventoryLocationId = locationId, Quantity = quantity, UnitCost = unitCost }]
            }, default));
        Assert.True(moved.IsSuccess, moved.Error);
    }

    private static Task<Result<InventoryCountDto>> StartAsync(SqliteTestHost host, StartInventoryCountCommand command)
        => HandleAsync<StartInventoryCountCommandHandler, InventoryCountDto>(host, h => h.Handle(command, default));

    private static Task<Result<RecordedInventoryCountDto>> RecordAsync(
        SqliteTestHost host, Guid countId, params (Guid LineId, decimal? Quantity, string? Notes)[] entries)
        => host.HandleAsync<RecordInventoryCountCommandHandler, Result<RecordedInventoryCountDto>>(h => h.Handle(
            new RecordInventoryCountCommand
            {
                CountId = countId,
                Lines = entries.Select(e => new InventoryCountEntry { LineId = e.LineId, CountedQuantity = e.Quantity, Notes = e.Notes }).ToList()
            }, default));

    private static Task<Result<InventoryCountDto>> PostAsync(SqliteTestHost host, Guid countId, int revision)
        => HandleAsync<PostInventoryCountCommandHandler, InventoryCountDto>(host,
            h => h.Handle(new PostInventoryCountCommand { CountId = countId, ExpectedRevision = revision }, default));

    private static Task<InventoryCountDto> CountAsync(SqliteTestHost host, Guid countId)
        => host.HandleAsync<GetInventoryCountQueryHandler, InventoryCountDto>(h => h.Handle(new GetInventoryCountQuery(countId), default));

    private static Task<InventoryCountLinesPage> LinesAsync(
        SqliteTestHost host, Guid countId, string? search = null, bool? pending = null, bool? withDifference = null)
        => host.HandleAsync<GetInventoryCountLinesQueryHandler, InventoryCountLinesPage>(h => h.Handle(
            new GetInventoryCountLinesQuery { CountId = countId, Search = search, Pending = pending, WithDifference = withDifference, PageSize = 100 }, default));

    private static Task<List<decimal>> StocksAsync(SqliteTestHost host, Guid locationId)
        => host.QueryAsync(async db => (await db.InventoryStocks.Where(s => s.InventoryLocationId == locationId).Select(s => s.OnHandQuantity).ToListAsync())
            .Order().ToList());

    private static Task<Result<T>> HandleAsync<THandler, T>(SqliteTestHost host, Func<THandler, Task<Result<T>>> handle)
        => host.HandleAsync(handle);
}
