using FoodParkDigital.Application.Catalog.Commands;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Catalog.Queries;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Application.Inventory.Commands;
using FoodParkDigital.Application.Inventory.DTOs;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// One stock model: a product that controls its stock does it in the inventory ledger, through an item of its own,
/// with an opening balance, and every sale takes from it; the catalog and Vender read it from there.
/// </summary>
public class ProductStockTests
{
    private const TenantCapability RetailWithInventory = TenantCapability.Retail | TenantCapability.Inventory;

    [Fact]
    public async Task ProductThatControlsItsStock_ShouldStartWithItsOpeningBalance_AndLoseWhatItSells()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, RetailWithInventory);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);

        var started = await TrackAsync(fixture, new TrackProductStockCommand { ProductId = soda.Id, InitialQuantity = 10, UnitCost = 12m, ReorderPoint = 3 });
        await fixture.OpenCashSessionAsync(tenant);
        var sale = await fixture.CreateOrderHandler(Inventory(fixture)).Handle(Sale(soda.Id, 3), CancellationToken.None);

        Assert.True(started.IsSuccess, started.Error);
        Assert.True(started.Value.TracksStock);
        Assert.Equal(10, started.Value.OnHand);
        Assert.True(sale.IsSuccess, sale.Error);
        var opening = await fixture.Context.InventoryMovements.SingleAsync(m => m.MovementReason == InventoryMovementReason.InitialLoad);
        Assert.Equal(InventoryMovementType.Entry, opening.MovementType);
        Assert.StartsWith("INV-", opening.MovementNumber);
        Assert.Equal(12m, (await fixture.Context.SupplyItems.SingleAsync()).AverageCost);
        var listed = Assert.Single((await ListAsync(fixture)).Items);
        Assert.True(listed.TracksStock);
        Assert.Equal(7, listed.OnHand);
        Assert.Equal(3, listed.ReorderPoint);
        Assert.False(listed.IsLowStock);
    }

    [Fact]
    public async Task ProductAtItsReorderPoint_ShouldBeLowOnStock()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, RetailWithInventory);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        await TrackAsync(fixture, new TrackProductStockCommand { ProductId = soda.Id, InitialQuantity = 10, ReorderPoint = 3 });
        await fixture.OpenCashSessionAsync(tenant);

        await fixture.CreateOrderHandler(Inventory(fixture)).Handle(Sale(soda.Id, 8), CancellationToken.None);

        var listed = Assert.Single((await ListAsync(fixture)).Items);
        Assert.Equal(2, listed.OnHand);
        Assert.True(listed.IsLowStock);
    }

    [Fact]
    public async Task ProductThatStopsControllingItsStock_ShouldNoLongerTakeFromIt_AndLeaveItAsASupply()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, RetailWithInventory);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        await TrackAsync(fixture, new TrackProductStockCommand { ProductId = soda.Id, InitialQuantity = 10 });
        await fixture.OpenCashSessionAsync(tenant);

        var stopped = await new StopTrackingProductStockCommandHandler(fixture.Context)
            .Handle(new StopTrackingProductStockCommand(soda.Id), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        var sale = await fixture.CreateOrderHandler(Inventory(fixture)).Handle(Sale(soda.Id, 2), CancellationToken.None);

        Assert.True(sale.IsSuccess, sale.Error);
        Assert.True(stopped.IsSuccess, stopped.Error);
        Assert.False(stopped.Value.TracksStock);
        var item = await fixture.Context.SupplyItems.SingleAsync();
        Assert.Null(item.ProductId);
        Assert.Equal(10, (await fixture.Context.InventoryStocks.SingleAsync()).OnHandQuantity);
        var listed = Assert.Single((await ListAsync(fixture)).Items);
        Assert.False(listed.TracksStock);
        Assert.Null(listed.OnHand);
    }

    [Fact]
    public async Task ExistingOneToOneItem_ShouldBecomeTheProductsStock_WithTheStockItHas()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, RetailWithInventory);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        // How a business kept a product's stock before: a supply with the same unit and a 1 to 1 recipe.
        var supply = new SupplyItem(tenant.Id, "Refresco (insumo)", UnitType.Unit);
        fixture.Context.SupplyItems.Add(supply);
        fixture.Context.ProductSupplyRequirements.Add(new ProductSupplyRequirement(tenant.Id, soda.Id, supply.Id, 1));
        var location = await fixture.Context.InventoryLocations.SingleAsync();
        var stock = new InventoryStock(tenant.Id, supply.Id, location.Id);
        stock.ApplyMovement(5, null, SalesTestFixture.Now);
        fixture.Context.InventoryStocks.Add(stock);
        await fixture.Context.SaveChangesAsync();

        var linked = await TrackAsync(fixture, new TrackProductStockCommand { ProductId = soda.Id, SupplyItemId = supply.Id });

        Assert.True(linked.IsSuccess, linked.Error);
        Assert.Equal(supply.Id, linked.Value.StockItemId);
        Assert.Equal(5, linked.Value.OnHand);
        Assert.Single(await fixture.Context.ProductSupplyRequirements.ToListAsync());
        Assert.Empty(await fixture.Context.InventoryMovements.ToListAsync());
    }

    [Fact]
    public async Task Control_ShouldBeRefused_ForAServiceAnItemMeasuredOtherwiseOrAProductAlreadyControlled()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, RetailWithInventory | TenantCapability.Service);
        var haircut = await fixture.AddProductAsync(tenant, "Corte", 150m, unit: UnitType.Service);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        var flour = new SupplyItem(tenant.Id, "Harina", UnitType.Kg);
        fixture.Context.SupplyItems.Add(flour);
        await fixture.Context.SaveChangesAsync();

        var service = await TrackAsync(fixture, new TrackProductStockCommand { ProductId = haircut.Id });
        var otherUnit = await TrackAsync(fixture, new TrackProductStockCommand { ProductId = soda.Id, SupplyItemId = flour.Id });
        await TrackAsync(fixture, new TrackProductStockCommand { ProductId = soda.Id });
        var twice = await TrackAsync(fixture, new TrackProductStockCommand { ProductId = soda.Id });

        Assert.Equal("Un servicio no tiene existencias.", service.Error);
        Assert.Equal("El artículo y el producto se miden en unidades distintas.", otherUnit.Error);
        Assert.Equal("Este producto ya controla sus existencias.", twice.Error);
    }

    [Fact]
    public void OpeningBalance_ShouldOnlyBeGivenForANewItem()
    {
        var validator = new TrackProductStockCommandValidator();

        var linking = validator.Validate(new TrackProductStockCommand { ProductId = Guid.NewGuid(), SupplyItemId = Guid.NewGuid(), InitialQuantity = 4 });
        var negative = validator.Validate(new TrackProductStockCommand { ProductId = Guid.NewGuid(), InitialQuantity = -1 });

        Assert.False(linking.IsValid);
        Assert.False(negative.IsValid);
    }

    [Fact]
    public async Task ProductsStock_ShouldStayInItsRecipe_AndKeepItsUnit_WhileItIsControlled()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, RetailWithInventory);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        var started = await TrackAsync(fixture, new TrackProductStockCommand { ProductId = soda.Id });

        var withoutItsStock = await new UpsertProductRecipeCommandHandler(fixture.Context, fixture.TenantService)
            .Handle(new UpsertProductRecipeCommand { ProductId = soda.Id, Lines = [] }, CancellationToken.None);
        var otherUnit = await UpdateAsync(fixture, soda, name: "Refresco", unit: UnitType.Kg);
        var renamed = await UpdateAsync(fixture, soda, name: "Refresco de cola", unit: null);

        Assert.True(withoutItsStock.IsFailure);
        Assert.Contains("es la existencia del producto", withoutItsStock.Error);
        Assert.Equal("Este producto controla sus existencias: deja de controlarlas para cambiar su unidad.", otherUnit.Error);
        Assert.True(renamed.IsSuccess, renamed.Error);
        Assert.True(renamed.Value.TracksStock);
        Assert.Equal("Refresco de cola", (await fixture.Context.SupplyItems.SingleAsync(s => s.Id == started.Value.StockItemId)).Name);
    }

    private static Task<Result<ProductStockDto>> TrackAsync(SalesTestFixture fixture, TrackProductStockCommand command)
        => new TrackProductStockCommandHandler(
                fixture.Context,
                fixture.TenantService,
                fixture.Clock,
                new InventoryLedger(fixture.Context, new FolioGenerator(fixture.Context)))
            .Handle(command, CancellationToken.None);

    private static Task<PaginatedList<ProductDto>> ListAsync(SalesTestFixture fixture)
        => new GetProductsQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetProductsQuery(), CancellationToken.None);

    private static Task<Result<ProductDto>> UpdateAsync(SalesTestFixture fixture, Product product, string name, UnitType? unit)
        => new UpdateProductCommandHandler(fixture.Context).Handle(
            new UpdateProductCommand
            {
                Id = product.Id,
                CategoryId = product.CategoryId,
                Name = name,
                PriceAmount = product.PriceAmount,
                Unit = unit
            },
            CancellationToken.None);

    private static OrderInventoryService Inventory(SalesTestFixture fixture)
        => new(fixture.Context, fixture.Clock, new FolioGenerator(fixture.Context), NullLogger<OrderInventoryService>.Instance);

    // Price 20 plus 16 % VAT, paid in cash: a counter sale that closes at once and takes its stock.
    private static CreateOrderCommand Sale(Guid productId, int quantity) => new()
    {
        Items = [new CreateOrderItemDto { ProductId = productId, Quantity = quantity }],
        Payments = [new CreateOrderPaymentDto { Amount = 23.20m * quantity, Method = OrderPaymentMethod.Cash }]
    };
}
