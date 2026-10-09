using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Application.Inventory.Commands;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Each round of a table's tab leaves the stock as it goes to the kitchen, with the business's stock policy as in a
/// sale: refused naming what is missing when the business blocks, sent with a warning when it warns. Closing the tab
/// takes only what no round took, and cancelling it or giving items back returns what the rounds took.
/// </summary>
public class TabRoundStockTests
{
    private const TenantCapability Restaurant = TenantCapability.Food | TenantCapability.Kitchen | TenantCapability.Inventory;

    [Fact]
    public async Task Rounds_ShouldLeaveTheStockAsTheyGoToTheKitchen_AndClosingShouldNotTakeThemAgain()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos, beer) = await RestaurantAsync(fixture, tacos: 10, beer: 6);

        var opened = await OpenTabAsync(fixture, Inventory(fixture), tacos, 3);
        Assert.Empty(opened.StockWarnings);
        Assert.Equal(7m, await OnHandAsync(fixture, tacos));
        var round = await AddRoundAsync(fixture, opened.Id, beer, 2);
        Assert.True(round.IsSuccess, round.Error);
        Assert.Empty(round.Value.StockWarnings);
        Assert.Equal(4m, await OnHandAsync(fixture, beer));

        await PayAndCloseAsync(fixture, tenant, opened.Id);

        Assert.Equal(7m, await OnHandAsync(fixture, tacos));
        Assert.Equal(4m, await OnHandAsync(fixture, beer));
        // One exit per round, named by the tab's folio, and none for the whole tab.
        var exits = await SaleExitsAsync(fixture);
        Assert.Equal(2, exits.Count);
        Assert.All(exits, exit => Assert.Contains(":ROUND:", exit.ExternalReference));
        Assert.All(exits, exit => Assert.Equal(opened.OrderNumber, exit.ReferenceNumber));
        Assert.Equal([2m, 3m], exits.Select(exit => exit.Lines.Single().Quantity).Order());
    }

    [Fact]
    public async Task ShortRound_ShouldBeRefusedNamingWhatIsMissing_WhenTheBusinessBlocks()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos, _) = await RestaurantAsync(fixture, tacos: 4, beer: 6);
        var opened = await OpenTabAsync(fixture, Inventory(fixture), tacos, 3);

        var refused = await Assert.ThrowsAsync<DomainException>(() => AddRoundAsync(fixture, opened.Id, tacos, 2));

        Assert.Contains("No alcanza Tacos", refused.Message);
        fixture.Context.ChangeTracker.Clear();
        var tab = await fixture.Context.Orders.Include(o => o.KitchenTickets).Include(o => o.Items).SingleAsync();
        Assert.Single(tab.KitchenTickets);
        Assert.Equal(3m, tab.Items.Sum(i => i.Quantity));
        Assert.Equal(1m, await OnHandAsync(fixture, tacos));
        Assert.Single(await SaleExitsAsync(fixture));
    }

    [Fact]
    public async Task ShortOpening_ShouldBeRefused_WhenTheBusinessBlocks()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos, _) = await RestaurantAsync(fixture, tacos: 2, beer: 6);

        await Assert.ThrowsAsync<DomainException>(() => fixture.CreateOrderHandler(Inventory(fixture)).Handle(
            new CreateOrderCommand { OpenTab = true, TableNumber = "Mesa 1", Items = [new CreateOrderItemDto { ProductId = tacos.Id, Quantity = 3 }] },
            CancellationToken.None));

        Assert.Equal(2m, await OnHandAsync(fixture, tacos));
        Assert.Empty(await SaleExitsAsync(fixture));
    }

    [Fact]
    public async Task ShortRound_ShouldGoWithAWarning_WhenTheBusinessWarns()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos, _) = await RestaurantAsync(fixture, tacos: 2, beer: 6);
        var settings = await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id);
        settings.SetInsufficientStockPolicy(InsufficientStockPolicy.Warn);
        await fixture.Context.SaveChangesAsync();
        var opened = await OpenTabAsync(fixture, Inventory(fixture), tacos, 1);

        var round = await AddRoundAsync(fixture, opened.Id, tacos, 3);

        Assert.True(round.IsSuccess, round.Error);
        Assert.Equal(["Tacos quedó en -2"], round.Value.StockWarnings);
        Assert.Equal(-2m, await OnHandAsync(fixture, tacos));
    }

    [Fact]
    public async Task TabOpenedByAnEarlierVersion_ShouldTakeItsOldRoundsWhenClosing_AndOnlyThose()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos, beer) = await RestaurantAsync(fixture, tacos: 10, beer: 6);
        // Opened before rounds left the stock one by one: the first round took nothing.
        var opened = await OpenTabAsync(fixture, NoOpOrderInventoryService.Instance, tacos, 3);
        Assert.Equal(10m, await OnHandAsync(fixture, tacos));
        var round = await AddRoundAsync(fixture, opened.Id, beer, 2);
        Assert.True(round.IsSuccess, round.Error);

        await PayAndCloseAsync(fixture, tenant, opened.Id);

        Assert.Equal(7m, await OnHandAsync(fixture, tacos));
        Assert.Equal(4m, await OnHandAsync(fixture, beer));
        var exits = await SaleExitsAsync(fixture);
        Assert.Equal(2, exits.Count);
        var closing = Assert.Single(exits, exit => exit.ExternalReference!.EndsWith($"{opened.Id:N}:CONSUMPTION", StringComparison.Ordinal));
        Assert.Equal(3m, Assert.Single(closing.Lines).Quantity);
    }

    [Fact]
    public async Task CancellingAnOpenTab_ShouldReturnWhatItsRoundsTook()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos, beer) = await RestaurantAsync(fixture, tacos: 10, beer: 6);
        var opened = await OpenTabAsync(fixture, Inventory(fixture), tacos, 3);
        Assert.True((await AddRoundAsync(fixture, opened.Id, beer, 2)).IsSuccess);

        var cancelled = await new CancelOrderCommandHandler(fixture.Context, fixture.CurrentUser, Inventory(fixture))
            .Handle(new CancelOrderCommand { OrderId = opened.Id, Reason = "Se fueron sin pedir más" }, CancellationToken.None);

        Assert.True(cancelled.IsSuccess, cancelled.Error);
        Assert.Equal(10m, await OnHandAsync(fixture, tacos));
        Assert.Equal(6m, await OnHandAsync(fixture, beer));
        var reversal = await fixture.Context.InventoryMovements.Include(m => m.Lines)
            .SingleAsync(m => m.MovementReason == InventoryMovementReason.Return);
        Assert.Equal(2, reversal.Lines.Count);
    }

    [Fact]
    public async Task ItemsGivenBack_ShouldReturnWhatTheirRoundTook()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos, _) = await RestaurantAsync(fixture, tacos: 10, beer: 6);
        var opened = await OpenTabAsync(fixture, Inventory(fixture), tacos, 3);
        await PayAndCloseAsync(fixture, tenant, opened.Id);
        var line = (await fixture.Context.OrderItems.AsNoTracking().SingleAsync()).Id;

        var refund = await new RefundOrderCommandHandler(fixture.Context, fixture.CurrentUser, NoOpOrderCashService.Instance, fixture.Clock, Inventory(fixture))
            .Handle(new RefundOrderCommand
            {
                OrderId = opened.Id,
                Amount = 25m,
                Method = OrderPaymentMethod.Cash,
                Reason = "Uno salió frío",
                RestockItems = true,
                Items = [new RefundItemDto { OrderItemId = line, Quantity = 1 }]
            }, CancellationToken.None);

        Assert.True(refund.IsSuccess, refund.Error);
        Assert.Equal(8m, await OnHandAsync(fixture, tacos));
    }

    [Fact]
    public async Task ARound_ShouldLeaveTheStockOnce()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos, _) = await RestaurantAsync(fixture, tacos: 10, beer: 6);
        var opened = await OpenTabAsync(fixture, Inventory(fixture), tacos, 3);
        var tab = await fixture.Context.Orders.Include(o => o.Items).Include(o => o.KitchenTickets).SingleAsync(o => o.Id == opened.Id);

        var again = await Inventory(fixture).ApplyConsumptionForTabRoundAsync(tab, 1, CancellationToken.None);
        await fixture.Context.SaveChangesAsync();

        Assert.Empty(again);
        Assert.Equal(7m, await OnHandAsync(fixture, tacos));
        Assert.Single(await SaleExitsAsync(fixture));
    }

    /// <summary>A restaurant (prices with VAT included) whose tacos and beer keep their own stock.</summary>
    private static async Task<(Tenant Tenant, Product Tacos, Product Beer)> RestaurantAsync(SalesTestFixture fixture, decimal tacos, decimal beer)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, Restaurant, pricesIncludeTax: true);
        var tacosProduct = await fixture.AddProductAsync(tenant, "Tacos", 25m);
        var beerProduct = await fixture.AddProductAsync(tenant, "Cerveza", 45m);
        var ledger = new InventoryLedger(fixture.Context, new FolioGenerator(fixture.Context));
        foreach (var (product, quantity) in new[] { (tacosProduct, tacos), (beerProduct, beer) })
        {
            var tracked = await new TrackProductStockCommandHandler(fixture.Context, fixture.TenantService, fixture.Clock, ledger)
                .Handle(new TrackProductStockCommand { ProductId = product.Id, InitialQuantity = quantity, UnitCost = 10m }, CancellationToken.None);
            Assert.True(tracked.IsSuccess, tracked.Error);
        }

        return (tenant, tacosProduct, beerProduct);
    }

    private static OrderInventoryService Inventory(SalesTestFixture fixture)
        => new(fixture.Context, fixture.Clock, new FolioGenerator(fixture.Context), NullLogger<OrderInventoryService>.Instance);

    private static async Task<OrderDto> OpenTabAsync(
        SalesTestFixture fixture,
        Common.Interfaces.IOrderInventoryService inventory,
        Product product,
        decimal quantity)
    {
        var opened = await fixture.CreateOrderHandler(inventory).Handle(
            new CreateOrderCommand { OpenTab = true, TableNumber = "Mesa 1", Items = [new CreateOrderItemDto { ProductId = product.Id, Quantity = quantity }] },
            CancellationToken.None);
        Assert.True(opened.IsSuccess, opened.Error);
        Assert.True(opened.Value.IsOpenTab);
        return opened.Value;
    }

    private static Task<Result<OrderDto>> AddRoundAsync(SalesTestFixture fixture, Guid orderId, Product product, decimal quantity)
        => new AddTabRoundCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock, Inventory(fixture))
            .Handle(new AddTabRoundCommand { OrderId = orderId, Items = [new CreateOrderItemDto { ProductId = product.Id, Quantity = quantity }] },
                CancellationToken.None);

    private static async Task PayAndCloseAsync(SalesTestFixture fixture, Tenant tenant, Guid orderId)
    {
        await fixture.OpenCashSessionAsync(tenant);
        var balance = (await fixture.Context.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId)).Balance;
        var paid = await fixture.AddPaymentHandler().Handle(
            new AddOrderPaymentCommand { OrderId = orderId, Amount = balance, Method = OrderPaymentMethod.Cash },
            CancellationToken.None);
        Assert.True(paid.IsSuccess, paid.Error);
        var closed = await new CloseTabCommandHandler(fixture.Context, fixture.CurrentUser, Inventory(fixture))
            .Handle(new CloseTabCommand(orderId), CancellationToken.None);
        Assert.True(closed.IsSuccess, closed.Error);
    }

    private static async Task<decimal> OnHandAsync(SalesTestFixture fixture, Product product)
    {
        var item = await fixture.Context.SupplyItems.AsNoTracking().SingleAsync(s => s.ProductId == product.Id);
        return await fixture.Context.InventoryStocks.AsNoTracking()
            .Where(s => s.SupplyItemId == item.Id)
            .SumAsync(s => s.OnHandQuantity);
    }

    private static Task<List<Domain.Inventory.Entities.InventoryMovement>> SaleExitsAsync(SalesTestFixture fixture)
        => fixture.Context.InventoryMovements.AsNoTracking().Include(m => m.Lines)
            .Where(m => m.MovementType == InventoryMovementType.Exit && m.MovementReason == InventoryMovementReason.Sale)
            .ToListAsync();
}
