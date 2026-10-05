using FoodParkDigital.Application.Audit.Queries;
using FoodParkDigital.Application.Inventory.Queries;
using FoodParkDigital.Application.Purchasing.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Purchasing.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Tests;

/// <summary>Inventory, purchases and activity search on the server, over every page, not only the one loaded.</summary>
public class ServerSearchTests
{
    [Fact]
    public async Task PurchaseOrders_ShouldBeFoundByFolioSupplierOrReference()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail | TenantCapability.Inventory);
        var dairy = new Supplier(tenant.Id, "Lácteos San Juan");
        var bakery = new Supplier(tenant.Id, "Panificadora La Espiga");
        fixture.Context.Suppliers.AddRange(dairy, bakery);
        fixture.Context.PurchaseOrders.AddRange(
            new PurchaseOrder(tenant.Id, dairy.Id, "PO-20261003-0002", SalesTestFixture.Now, referenceNumber: "FAC-A-88213"),
            new PurchaseOrder(tenant.Id, bakery.Id, "PO-20261003-0001", SalesTestFixture.Now));
        await fixture.Context.SaveChangesAsync();
        var handler = new GetPurchaseOrdersQueryHandler(fixture.Context);

        var bySupplier = await handler.Handle(new GetPurchaseOrdersQuery { Search = "espiga" }, CancellationToken.None);
        var byReference = await handler.Handle(new GetPurchaseOrdersQuery { Search = " fac-a " }, CancellationToken.None);
        var byFolio = await handler.Handle(new GetPurchaseOrdersQuery { Search = "0002" }, CancellationToken.None);

        Assert.Equal("PO-20261003-0001", Assert.Single(bySupplier.Items).OrderNumber);
        Assert.Equal("PO-20261003-0002", Assert.Single(byReference.Items).OrderNumber);
        Assert.Equal("Lácteos San Juan", Assert.Single(byFolio.Items).SupplierName);
    }

    [Fact]
    public async Task InventoryMovements_ShouldBeFoundByReferenceOrCounterparty()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail | TenantCapability.Inventory);
        fixture.Context.InventoryMovements.AddRange(
            new InventoryMovement(tenant.Id, "INV-1", InventoryMovementType.Entry, InventoryMovementReason.Purchase, SalesTestFixture.Now,
                referenceNumber: "PO-20261003-0002", counterpartyName: "Lácteos San Juan"),
            new InventoryMovement(tenant.Id, "INV-2", InventoryMovementType.Exit, InventoryMovementReason.Waste, SalesTestFixture.Now,
                notes: "Leche caducada"));
        await fixture.Context.SaveChangesAsync();
        var handler = new GetInventoryMovementsQueryHandler(fixture.Context);

        var found = await handler.Handle(new GetInventoryMovementsQuery { Search = "san juan" }, CancellationToken.None);
        var none = await handler.Handle(new GetInventoryMovementsQuery { Search = "proveedor inexistente" }, CancellationToken.None);

        Assert.Equal("INV-1", Assert.Single(found.Items).MovementNumber);
        Assert.Empty(none.Items);
    }

    [Fact]
    public async Task AuditLogs_ShouldBeFoundByWhoOrWhat()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        fixture.Context.AuditLogs.AddRange(
            new AuditLog(tenant.Id, "TenantSettings", tenant.Id.ToString(), "TenantCapabilitiesUpdated", "mariana@demo.com", timestamp: SalesTestFixture.Now),
            new AuditLog(tenant.Id, "Order", Guid.NewGuid().ToString(), "OrderCancelled", "carlos@demo.com", timestamp: SalesTestFixture.Now));
        await fixture.Context.SaveChangesAsync();
        var handler = new GetAuditLogsQueryHandler(fixture.Context);

        var byPerson = await handler.Handle(new GetAuditLogsQuery { Search = "carlos" }, CancellationToken.None);
        var byAction = await handler.Handle(new GetAuditLogsQuery { Search = "capabilities" }, CancellationToken.None);

        Assert.Equal("OrderCancelled", Assert.Single(byPerson.Items).Action);
        Assert.Equal("mariana@demo.com", Assert.Single(byAction.Items).PerformedBy);
    }
}
