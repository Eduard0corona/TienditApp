using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Inventory.Commands;
using FoodParkDigital.Application.Inventory.Counts;
using FoodParkDigital.Application.Inventory.Transfers;
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
/// Transferencias entre ubicaciones (Fase 27B, ADR 0038), on a relational database: the items leave one location and enter
/// the other as a TransferOut and a TransferIn of reason Transfer, valued alike, or nothing moves; the same key again answers
/// the same transfer.
/// </summary>
public class InventoryTransferFlowTests
{
    private const string TransferFolio = "TRF-20260216-0001";

    [Fact]
    public async Task Transfer_ShouldMoveTheItems_InTwoMovementsValuedAlike_WithoutChangingTheAverageCost()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var shop = await SeedShopAsync(host);

        var made = await TransferAsync(host, shop.Warehouse, shop.Counter, "  Para el fin de semana ", (shop.Soda, 10m), (shop.Sugar, 2.5m));

        Assert.True(made.IsSuccess, made.Error);
        var transfer = made.Value;
        Assert.Equal((TransferFolio, "Bodega", "Mostrador", "sqlite-test", 2, 195m, "Para el fin de semana"),
            (transfer.TransferNumber, transfer.FromLocationName, transfer.ToLocationName, transfer.TransferredBy, transfer.LineCount,
                transfer.TotalCost, transfer.Notes));
        Assert.Equal(["Azúcar estándar", "Refresco de cola 600 ml"], transfer.Lines.Select(l => l.SupplyItemName));
        Assert.Equal((2.5m, (decimal?)30m, (decimal?)75m, 10m, 7.5m, 0m, 2.5m), Row(transfer.Lines[0]));
        Assert.Equal((10m, (decimal?)12m, (decimal?)120m, 24m, 14m, 6m, 16m), Row(transfer.Lines[1]));
        Assert.Equal([7.5m, 14m, 15m], await StocksAsync(host, shop.Warehouse));
        Assert.Equal([2.5m, 16m], await StocksAsync(host, shop.Counter));

        var legs = await host.QueryAsync(db => db.InventoryMovements
            .Where(m => m.MovementReason == InventoryMovementReason.Transfer)
            .OrderBy(m => m.MovementType)
            .ToListAsync());
        Assert.Equal(
            [
                (InventoryMovementType.TransferIn, transfer.InMovementNumber, TransferFolio, $"TRF:{transfer.Id:N}:IN", 195m, $"Transferencia {TransferFolio} de Bodega"),
                (InventoryMovementType.TransferOut, transfer.OutMovementNumber, TransferFolio, $"TRF:{transfer.Id:N}:OUT", 195m, $"Transferencia {TransferFolio} a Mostrador")
            ],
            legs.Select(m => (m.MovementType, m.MovementNumber, m.ReferenceNumber!, m.ExternalReference!, m.TotalAmount, m.Notes!)));
        // Not a purchase: the average cost of what moved stays.
        Assert.Equal([12m, 30m], await host.QueryAsync(async db =>
            (await db.SupplyItems.Where(s => s.Id == shop.Soda || s.Id == shop.Sugar).Select(s => s.AverageCost).ToListAsync()).Order().ToList()));
        var audit = await host.QueryAsync(db => db.AuditLogs.SingleAsync(l => l.Action == "InventoryTransferCreated"));
        Assert.Equal(transfer.Id.ToString(), audit.EntityId);
        Assert.Contains($"\"TransferNumber\":\"{TransferFolio}\"", audit.Metadata);

        // The list, the newest first, by either of its locations or its folio.
        Assert.Equal([transfer.Id], (await ListAsync(host, locationId: shop.Counter)).Select(t => t.Id));
        Assert.Equal([transfer.Id], (await ListAsync(host, search: "mostr")).Select(t => t.Id));
        Assert.Equal([transfer.Id], (await ListAsync(host, search: "0001")).Select(t => t.Id));
        Assert.Empty(await ListAsync(host, search: "cámara"));
        Assert.Equal(transfer.Lines.Count, (await DetailAsync(host, transfer.Id)).Lines.Count);
    }

    [Fact]
    public async Task Transfer_ShouldBeRefused_WhenTheOriginHasNotEnough_NamingEachItemWithWhatThereIs()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var shop = await SeedShopAsync(host);

        var tooMuch = await TransferAsync(host, shop.Warehouse, shop.Counter, null, (shop.Soda, 30m), (shop.Chips, 20m), (shop.Sugar, 1m));
        var none = await TransferAsync(host, shop.Counter, shop.Warehouse, null, (shop.Chips, 1m));

        Assert.Equal("No hay suficiente en Bodega: Papas 45 g (hay 15), Refresco de cola 600 ml (hay 24).", tooMuch.Error);
        Assert.Equal("No hay suficiente en Mostrador: Papas 45 g (hay 0).", none.Error);
        Assert.Equal(0, await host.QueryAsync(db => db.InventoryTransfers.CountAsync()));
        Assert.Equal(0, await host.QueryAsync(db => db.InventoryMovements.CountAsync(m => m.MovementReason == InventoryMovementReason.Transfer)));
        Assert.Equal([10m, 15m, 24m], await StocksAsync(host, shop.Warehouse));
    }

    [Fact]
    public async Task Transfer_ShouldBeRefused_ForInactiveOrMissingLocationsAndItems_AndForFractionsOfPieces()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var shop = await SeedShopAsync(host);
        var cold = await AddLocationAsync(host, "Cámara fría", "FRIO");
        await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            (await db.InventoryLocations.SingleAsync(l => l.Id == cold)).SetActive(false);
            await db.SaveChangesAsync();
        });

        Assert.Equal("Cámara fría está inactiva: actívala para transferir.", (await TransferAsync(host, shop.Warehouse, cold, null, (shop.Soda, 1m))).Error);
        Assert.Equal("La ubicación de destino no existe.", (await TransferAsync(host, shop.Warehouse, Guid.NewGuid(), null, (shop.Soda, 1m))).Error);
        Assert.Equal("La ubicación de origen no existe.", (await TransferAsync(host, Guid.NewGuid(), shop.Counter, null, (shop.Soda, 1m))).Error);
        Assert.Equal("Uno o más artículos no existen.", (await TransferAsync(host, shop.Warehouse, shop.Counter, null, (Guid.NewGuid(), 1m))).Error);
        Assert.Equal("Estos artículos van en piezas enteras: Refresco de cola 600 ml.",
            (await TransferAsync(host, shop.Warehouse, shop.Counter, null, (shop.Soda, 1.5m), (shop.Sugar, 1.5m))).Error);
        await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            (await db.SupplyItems.SingleAsync(s => s.Id == shop.Chips)).SetActive(false);
            await db.SaveChangesAsync();
        });
        Assert.Equal("Estos artículos están inactivos: Papas 45 g. Actívalos para transferirlos.",
            (await TransferAsync(host, shop.Warehouse, shop.Counter, null, (shop.Chips, 1m))).Error);
        Assert.Equal(0, await host.QueryAsync(db => db.InventoryTransfers.CountAsync()));
    }

    [Fact]
    public async Task TheSameKey_SentAgain_ShouldAnswerTheSameTransfer_WithoutMovingAnything()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var shop = await SeedShopAsync(host);
        var command = Command(shop.Warehouse, shop.Counter, null, (shop.Soda, 4m));

        var first = await SendAsync(host, command);
        var again = await SendAsync(host, command);
        // Even with other items: the key is the transfer.
        var changed = await SendAsync(host, command with { Lines = [new InventoryTransferLineRequest { SupplyItemId = shop.Chips, Quantity = 1m }] });

        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal((first.Value.Id, first.Value.Id), (again.Value.Id, changed.Value.Id));
        Assert.Equal(1, await host.QueryAsync(db => db.InventoryTransfers.CountAsync()));
        Assert.Equal(2, await host.QueryAsync(db => db.InventoryMovements.CountAsync(m => m.MovementReason == InventoryMovementReason.Transfer)));
        Assert.Equal([10m, 15m, 20m], await StocksAsync(host, shop.Warehouse));
        Assert.Equal([10m], await StocksAsync(host, shop.Counter));
    }

    [Fact]
    public async Task BothLegs_ShouldBeValuedAlike_WithTheLastOrReferenceCost_WhenTheItemHasNoAverage()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var shop = await SeedShopAsync(host);
        var (ice, napkins) = await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var tenantId = (await db.Tenants.SingleAsync()).Id;
            var withReference = new SupplyItem(tenantId, "Hielo en bolsa 5 kg", UnitType.Unit, defaultCost: 18m);
            var withoutCost = new SupplyItem(tenantId, "Servilletas", UnitType.Pack);
            db.SupplyItems.AddRange(withReference, withoutCost);
            await db.SaveChangesAsync();
            return (withReference.Id, withoutCost.Id);
        });
        // Found on the shelf, not bought: stock without an average cost.
        await MoveAsync(host, InventoryMovementType.AdjustmentIncrease, ice, shop.Warehouse, 5m);
        await MoveAsync(host, InventoryMovementType.AdjustmentIncrease, napkins, shop.Warehouse, 3m);

        var made = await TransferAsync(host, shop.Warehouse, shop.Counter, null, (ice, 2m), (napkins, 1m));

        Assert.True(made.IsSuccess, made.Error);
        Assert.Equal(((decimal?)18m, (decimal?)36m), (made.Value.Lines[0].UnitCost, made.Value.Lines[0].TotalCost));
        Assert.Equal(((decimal?)null, (decimal?)null), (made.Value.Lines[1].UnitCost, made.Value.Lines[1].TotalCost));
        Assert.Equal(36m, made.Value.TotalCost);
        var legs = await host.QueryAsync(db => db.InventoryMovementLines
            .Where(l => l.MovementId == made.Value.OutMovementId || l.MovementId == made.Value.InMovementId)
            .Select(l => new { l.MovementId, l.SupplyItemId, l.UnitCost, l.TotalCost })
            .ToListAsync());
        Assert.All(legs.GroupBy(l => l.SupplyItemId), item => Assert.Single(item.Select(l => (l.UnitCost, l.TotalCost)).Distinct()));
    }

    [Fact]
    public async Task ATransfer_DuringAnOpenCount_ShouldBeKeptWhenTheCountIsPosted()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var shop = await SeedShopAsync(host);
        var count = (await host.HandleAsync<StartInventoryCountCommandHandler, Result<InventoryCountDto>>(h => h.Handle(
            new StartInventoryCountCommand { InventoryLocationId = shop.Warehouse, SupplyItemIds = [shop.Soda] }, default))).Value;
        var line = (await host.HandleAsync<GetInventoryCountLinesQueryHandler, InventoryCountLinesPage>(h => h.Handle(
            new GetInventoryCountLinesQuery { CountId = count.Id }, default))).Items.Single();
        var recorded = await host.HandleAsync<RecordInventoryCountCommandHandler, Result<RecordedInventoryCountDto>>(h => h.Handle(
            new RecordInventoryCountCommand { CountId = count.Id, Lines = [new InventoryCountEntry { LineId = line.Id, CountedQuantity = 22m }] }, default));

        // Ten taken to the counter after counting 22 of the 24 the system had.
        var made = await TransferAsync(host, shop.Warehouse, shop.Counter, null, (shop.Soda, 10m));
        var posted = await host.HandleAsync<PostInventoryCountCommandHandler, Result<InventoryCountDto>>(h => h.Handle(
            new PostInventoryCountCommand { CountId = count.Id, ExpectedRevision = recorded.Value.Count.Revision }, default));

        Assert.True(made.IsSuccess, made.Error);
        Assert.True(posted.IsSuccess, posted.Error);
        // 24 - 10 transferred - 2 missing.
        Assert.Equal(12m, await host.QueryAsync(db => db.InventoryStocks
            .Where(s => s.InventoryLocationId == shop.Warehouse && s.SupplyItemId == shop.Soda).Select(s => s.OnHandQuantity).SingleAsync()));
    }

    [Fact]
    public void Validator_ShouldRefuse_ARequestThatIsNotATransfer()
    {
        var validator = new CreateInventoryTransferCommandValidator();
        var from = Guid.NewGuid();
        var supply = Guid.NewGuid();
        CreateInventoryTransferCommand Valid() => new()
        {
            IdempotencyKey = Guid.NewGuid(),
            FromLocationId = from,
            ToLocationId = Guid.NewGuid(),
            Lines = [new InventoryTransferLineRequest { SupplyItemId = supply, Quantity = 1.255m }]
        };

        Assert.True(validator.Validate(Valid()).IsValid);
        (CreateInventoryTransferCommand Command, string Message)[] refusals =
        [
            (Valid() with { IdempotencyKey = Guid.Empty }, "La transferencia necesita su llave: vuelve a abrirla."),
            (Valid() with { FromLocationId = Guid.Empty }, "Elige de qué ubicación salen los artículos."),
            (Valid() with { ToLocationId = from }, InventoryTransfer.SameLocationMessage),
            (Valid() with { Lines = [] }, "Agrega al menos un artículo."),
            (Valid() with { Lines = Enumerable.Range(0, 201).Select(_ => new InventoryTransferLineRequest { SupplyItemId = Guid.NewGuid(), Quantity = 1 }).ToList() },
                InventoryTransfer.TooManyLinesMessage),
            (Valid() with { Lines = [new InventoryTransferLineRequest { SupplyItemId = supply, Quantity = 1 }, new InventoryTransferLineRequest { SupplyItemId = supply, Quantity = 2 }] },
                "Un artículo va una sola vez."),
            (Valid() with { Lines = [null!] }, "Falta un artículo en la lista."),
            (Valid() with { Lines = [new InventoryTransferLineRequest { SupplyItemId = supply, Quantity = 0 }] }, "La cantidad va de más de 0 a 9,999,999, con máximo 3 decimales."),
            (Valid() with { Lines = [new InventoryTransferLineRequest { SupplyItemId = supply, Quantity = 1.0001m }] }, "La cantidad va de más de 0 a 9,999,999, con máximo 3 decimales."),
            (Valid() with { Lines = [new InventoryTransferLineRequest { SupplyItemId = supply, Quantity = 10_000_000m }] }, "La cantidad va de más de 0 a 9,999,999, con máximo 3 decimales."),
        ];
        foreach (var (command, message) in refusals)
            Assert.Contains(validator.Validate(command).Errors, e => e.ErrorMessage == message);
        Assert.Contains(validator.Validate(Valid() with { Notes = new string('x', 501) }).Errors, e => e.PropertyName == "Notes");
    }

    private sealed record Shop(Guid Warehouse, Guid Counter, Guid Soda, Guid Chips, Guid Sugar);

    private static (decimal Quantity, decimal? UnitCost, decimal? TotalCost, decimal FromBefore, decimal FromAfter, decimal ToBefore, decimal ToAfter) Row(
        InventoryTransferLineDto line)
        => (line.Quantity, line.UnitCost, line.TotalCost, line.FromQuantityBefore, line.FromQuantityAfter, line.ToQuantityBefore, line.ToQuantityAfter);

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
            var location = new InventoryLocation((await db.Tenants.SingleAsync()).Id, name, code);
            db.InventoryLocations.Add(location);
            await db.SaveChangesAsync();
            return location.Id;
        });

    private static async Task MoveAsync(
        SqliteTestHost host, InventoryMovementType type, Guid supplyId, Guid locationId, decimal quantity, decimal? unitCost = null)
    {
        var moved = await host.HandleAsync<CreateInventoryMovementCommandHandler, Result<Inventory.DTOs.InventoryMovementDto>>(h => h.Handle(
            new CreateInventoryMovementCommand
            {
                MovementType = type,
                MovementReason = type == InventoryMovementType.Entry ? InventoryMovementReason.Purchase : InventoryMovementReason.ManualAdjustment,
                Lines = [new CreateInventoryMovementLineRequest { SupplyItemId = supplyId, InventoryLocationId = locationId, Quantity = quantity, UnitCost = unitCost }]
            }, default));
        Assert.True(moved.IsSuccess, moved.Error);
    }

    private static CreateInventoryTransferCommand Command(Guid from, Guid to, string? notes, params (Guid SupplyId, decimal Quantity)[] lines)
        => new()
        {
            IdempotencyKey = Guid.NewGuid(),
            FromLocationId = from,
            ToLocationId = to,
            Notes = notes,
            Lines = lines.Select(l => new InventoryTransferLineRequest { SupplyItemId = l.SupplyId, Quantity = l.Quantity }).ToList()
        };

    private static Task<Result<InventoryTransferDto>> TransferAsync(
        SqliteTestHost host, Guid from, Guid to, string? notes, params (Guid SupplyId, decimal Quantity)[] lines)
        => SendAsync(host, Command(from, to, notes, lines));

    private static Task<Result<InventoryTransferDto>> SendAsync(SqliteTestHost host, CreateInventoryTransferCommand command)
        => host.HandleAsync<CreateInventoryTransferCommandHandler, Result<InventoryTransferDto>>(h => h.Handle(command, default));

    private static async Task<List<InventoryTransferListItemDto>> ListAsync(SqliteTestHost host, Guid? locationId = null, string? search = null)
        => (await host.HandleAsync<GetInventoryTransfersQueryHandler, PaginatedList<InventoryTransferListItemDto>>(h => h.Handle(
            new GetInventoryTransfersQuery { InventoryLocationId = locationId, Search = search }, default))).Items;

    private static Task<InventoryTransferDto> DetailAsync(SqliteTestHost host, Guid transferId)
        => host.HandleAsync<GetInventoryTransferQueryHandler, InventoryTransferDto>(h => h.Handle(new GetInventoryTransferQuery(transferId), default));

    private static Task<List<decimal>> StocksAsync(SqliteTestHost host, Guid locationId)
        => host.QueryAsync(async db => (await db.InventoryStocks.Where(s => s.InventoryLocationId == locationId).Select(s => s.OnHandQuantity).ToListAsync())
            .Order().ToList());
}
