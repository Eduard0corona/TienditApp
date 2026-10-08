using System.Text.Json;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Application.Inventory.Commands;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.EventHandlers;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// A dish cancelled from a round already sent to the kitchen: the tab stops charging it, the supplies its round took go
/// back to stock (unless the kitchen prepared it), the board stops showing it and the activity log records why.
/// </summary>
public class TabItemCancellationFlowTests
{
    private const TenantCapability Restaurant = TenantCapability.Food | TenantCapability.Kitchen | TenantCapability.Inventory;

    [Fact]
    public async Task CancellingADish_ShouldLowerTheTabAndReturnWhatItsRoundTook()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos, beer) = await RestaurantAsync(fixture, tacos: 10, beer: 6);
        var opened = await OpenTabAsync(fixture, Inventory(fixture), tacos, 3);
        Assert.True((await AddRoundAsync(fixture, opened.Id, beer, 2)).IsSuccess);
        var line = await LineAsync(fixture, tacos);

        var cancelled = await CancelAsync(fixture, opened.Id, line, 1, "Se equivocó el mesero");

        Assert.True(cancelled.IsSuccess, cancelled.Error);
        Assert.Equal(50m + 90m, cancelled.Value.Total);
        Assert.Equal(140m, cancelled.Value.Balance);
        Assert.Equal(140m, cancelled.Value.TaxBreakdown.Sum(line => line.Total));
        var item = cancelled.Value.Items.Single(i => i.Id == line);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(1, item.CancelledQuantity);
        Assert.Equal(75m, item.TotalPrice);
        Assert.Equal(50m, item.ChargedPrice);
        var record = Assert.Single(cancelled.Value.ItemCancellations);
        Assert.Equal((line, 1, 1m, 25m, "Se equivocó el mesero", true, "cajero"),
            (record.OrderItemId, record.Round, record.Quantity, record.Amount, record.Reason, record.ReturnedToStock, record.CancelledBy));

        // The taco went back to stock; the beer of the other round did not move.
        Assert.Equal(8m, await OnHandAsync(fixture, tacos));
        Assert.Equal(4m, await OnHandAsync(fixture, beer));
        var back = await fixture.Context.InventoryMovements.AsNoTracking().Include(m => m.Lines)
            .SingleAsync(m => m.MovementReason == InventoryMovementReason.Return);
        Assert.Equal(InventoryMovementType.Entry, back.MovementType);
        Assert.Equal($"ORDER:{opened.Id:N}:RETURN:{record.Id:N}", back.ExternalReference);
        Assert.Equal(opened.OrderNumber, back.ReferenceNumber);
        Assert.Contains("Se equivocó el mesero", back.Notes);
        Assert.Equal(1m, Assert.Single(back.Lines).Quantity);
        await AssertFoliosInARowAsync(fixture);

        // Saved as it answered.
        fixture.Context.ChangeTracker.Clear();
        var stored = await new GetOrderByIdQueryHandler(fixture.Context).Handle(new GetOrderByIdQuery(opened.Id), CancellationToken.None);
        Assert.Equal(140m, stored.Total);
        Assert.Equal(1, stored.Items.Single(i => i.Id == line).CancelledQuantity);
        Assert.Single(stored.ItemCancellations);
        Assert.Equal([1, 2], stored.KitchenTickets.Select(t => t.Round));
    }

    [Fact]
    public async Task PreparedDish_ShouldKeepItsSuppliesOut()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos, _) = await RestaurantAsync(fixture, tacos: 10, beer: 6);
        var opened = await OpenTabAsync(fixture, Inventory(fixture), tacos, 3);

        var cancelled = await CancelAsync(fixture, opened.Id, await LineAsync(fixture, tacos), 2, "Se quemaron", returnToStock: false);

        Assert.True(cancelled.IsSuccess, cancelled.Error);
        Assert.Equal(25m, cancelled.Value.Total);
        Assert.False(Assert.Single(cancelled.Value.ItemCancellations).ReturnedToStock);
        Assert.Equal(7m, await OnHandAsync(fixture, tacos));
        Assert.DoesNotContain(await fixture.Context.InventoryMovements.AsNoTracking().ToListAsync(),
            m => m.MovementReason == InventoryMovementReason.Return);
    }

    [Fact]
    public async Task RecipeChangedAfterTheRound_ShouldNotGiveBackMoreThanTheRoundTook()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos, _) = await RestaurantAsync(fixture, tacos: 10, beer: 6);
        var opened = await OpenTabAsync(fixture, Inventory(fixture), tacos, 3);
        Assert.True((await AddRoundAsync(fixture, opened.Id, tacos, 2)).IsSuccess);
        Assert.Equal(5m, await OnHandAsync(fixture, tacos));
        // The recipe now takes five per taco; each round took one per taco.
        var recipe = await fixture.Context.ProductSupplyRequirements.SingleAsync(r => r.ProductId == tacos.Id);
        recipe.Update(5m, recipe.InventoryLocationId, recipe.WastePercent, recipe.Notes);
        await fixture.Context.SaveChangesAsync();
        var lines = await fixture.Context.OrderItems.AsNoTracking().OrderBy(i => i.Round).Select(i => i.Id).ToListAsync();

        // A taco of the second round gives back at most the two that round took, not what the first one took.
        Assert.True((await CancelAsync(fixture, opened.Id, lines[1], 1, "Se equivocó el mesero")).IsSuccess);
        Assert.Equal(7m, await OnHandAsync(fixture, tacos));

        // A taco of the first round gives back at most what the tab took and has not got back.
        Assert.True((await CancelAsync(fixture, opened.Id, lines[0], 1, "Otro se equivocó")).IsSuccess);
        Assert.Equal(10m, await OnHandAsync(fixture, tacos));

        // Everything came back already: another cancellation returns nothing and takes no folio.
        Assert.True((await CancelAsync(fixture, opened.Id, lines[0], 1, "Uno más")).IsSuccess);
        Assert.Equal(10m, await OnHandAsync(fixture, tacos));
        Assert.Equal(2, await fixture.Context.InventoryMovements.AsNoTracking()
            .CountAsync(m => m.MovementReason == InventoryMovementReason.Return));
        await AssertFoliosInARowAsync(fixture);
    }

    [Fact]
    public async Task CancellingTheTabAfterADish_ShouldReturnOnlyWhatIsLeft()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos, _) = await RestaurantAsync(fixture, tacos: 10, beer: 6);
        var opened = await OpenTabAsync(fixture, Inventory(fixture), tacos, 3);
        Assert.True((await CancelAsync(fixture, opened.Id, await LineAsync(fixture, tacos), 1, "Se equivocó el mesero")).IsSuccess);
        Assert.Equal(8m, await OnHandAsync(fixture, tacos));

        var cancelled = await new CancelOrderCommandHandler(fixture.Context, fixture.CurrentUser, Inventory(fixture))
            .Handle(new CancelOrderCommand { OrderId = opened.Id, Reason = "Se fueron" }, CancellationToken.None);

        Assert.True(cancelled.IsSuccess, cancelled.Error);
        Assert.Equal(10m, await OnHandAsync(fixture, tacos));
        var reversal = await fixture.Context.InventoryMovements.AsNoTracking().Include(m => m.Lines)
            .SingleAsync(m => m.ExternalReference == $"ORDER:{opened.Id:N}:REVERSAL");
        Assert.Equal(2m, Assert.Single(reversal.Lines).Quantity);
    }

    [Fact]
    public async Task TabOpenedByAnEarlierVersion_ShouldTakeOnlyWhatWasNotCancelledWhenClosing()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos, _) = await RestaurantAsync(fixture, tacos: 10, beer: 6);
        // Its first round took nothing when it went to the kitchen: there is nothing to give back, and no folio is used.
        var opened = await OpenTabAsync(fixture, NoOpOrderInventoryService.Instance, tacos, 3);
        var movements = await fixture.Context.InventoryMovements.CountAsync();

        Assert.True((await CancelAsync(fixture, opened.Id, await LineAsync(fixture, tacos), 1, "Se equivocó el mesero")).IsSuccess);
        Assert.Equal(10m, await OnHandAsync(fixture, tacos));
        Assert.Equal(movements, await fixture.Context.InventoryMovements.CountAsync());

        await PayAndCloseAsync(fixture, tenant, opened.Id);

        Assert.Equal(8m, await OnHandAsync(fixture, tacos));
        await AssertFoliosInARowAsync(fixture);
    }

    [Fact]
    public async Task ADishAlreadyPaid_ShouldBeRefundedInstead()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos, _) = await RestaurantAsync(fixture, tacos: 10, beer: 6);
        var opened = await OpenTabAsync(fixture, Inventory(fixture), tacos, 3);
        await fixture.OpenCashSessionAsync(tenant);
        Assert.True((await fixture.AddPaymentHandler().Handle(
            new AddOrderPaymentCommand { OrderId = opened.Id, Amount = 75m, Method = OrderPaymentMethod.Cash },
            CancellationToken.None)).IsSuccess);

        var line = await LineAsync(fixture, tacos);

        var refused = await Assert.ThrowsAsync<DomainException>(() => CancelAsync(fixture, opened.Id, line, 1, "Uno salió frío"));

        Assert.Contains("reembólsalo", refused.Message);
        fixture.Context.ChangeTracker.Clear();
        var tab = await fixture.Context.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.ItemCancellations).SingleAsync();
        Assert.Equal(75m, tab.Total);
        Assert.Empty(tab.ItemCancellations);
        Assert.Equal(7m, await OnHandAsync(fixture, tacos));
    }

    [Fact]
    public async Task ClosedTab_ShouldNotCancelItsDishes()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos, _) = await RestaurantAsync(fixture, tacos: 10, beer: 6);
        var opened = await OpenTabAsync(fixture, Inventory(fixture), tacos, 3);
        await PayAndCloseAsync(fixture, tenant, opened.Id);

        var refused = await CancelAsync(fixture, opened.Id, await LineAsync(fixture, tacos), 1, "Se equivocó el mesero");

        Assert.True(refused.IsFailure);
        Assert.Equal("La cuenta ya está cerrada.", refused.Error);
        await Assert.ThrowsAsync<EntityNotFoundException>(() => CancelAsync(fixture, Guid.NewGuid(), Guid.NewGuid(), 1, "No existe"));
    }

    [Fact]
    public async Task KitchenBoard_ShouldShowWhatWasCancelled_AndDropARoundWithNothingLeft()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos, beer) = await RestaurantAsync(fixture, tacos: 10, beer: 6);
        var opened = await OpenTabAsync(fixture, Inventory(fixture), tacos, 3);
        Assert.True((await AddRoundAsync(fixture, opened.Id, beer, 2)).IsSuccess);

        Assert.True((await CancelAsync(fixture, opened.Id, await LineAsync(fixture, tacos), 1, "Se equivocó el mesero")).IsSuccess);
        // Another request: the tab is loaded again, with the cancellation it already has.
        fixture.Context.ChangeTracker.Clear();
        var second = await CancelAsync(fixture, opened.Id, await LineAsync(fixture, beer), 2, "Ya no las quisieron");
        Assert.True(second.IsSuccess, second.Error);
        Assert.Equal(2, second.Value.ItemCancellations.Count);

        var board = await new GetKitchenTicketsQueryHandler(fixture.Context).Handle(new GetKitchenTicketsQuery(), CancellationToken.None);
        var round = Assert.Single(board);
        Assert.Equal(1, round.Round);
        var dish = Assert.Single(round.Items);
        Assert.Equal((3m, 1m), (dish.Quantity, dish.CancelledQuantity));
    }

    [Fact]
    public async Task Invoice_ShouldBillOnlyWhatTheTabCharged()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos, beer) = await RestaurantAsync(fixture, tacos: 10, beer: 6);
        var opened = await OpenTabAsync(fixture, Inventory(fixture), tacos, 3);
        Assert.True((await AddRoundAsync(fixture, opened.Id, beer, 2)).IsSuccess);
        Assert.True((await CancelAsync(fixture, opened.Id, await LineAsync(fixture, tacos), 1, "Se equivocó el mesero")).IsSuccess);
        Assert.True((await CancelAsync(fixture, opened.Id, await LineAsync(fixture, beer), 2, "Ya no las quisieron")).IsSuccess);
        await PayAndCloseAsync(fixture, tenant, opened.Id);

        var draft = await new GetOrderCfdiDraftQueryHandler(fixture.Context)
            .Handle(new GetOrderCfdiDraftQuery { OrderId = opened.Id }, CancellationToken.None);

        Assert.True(draft.IsSuccess, draft.Error);
        var concept = Assert.Single(draft.Value.Concepts);
        Assert.Equal(2m, concept.Quantity);
        Assert.Equal(50m, draft.Value.OrderTotal);
        Assert.Equal(50m, draft.Value.Total);
        Assert.Equal(0m, draft.Value.Difference);
    }

    [Fact]
    public async Task CustomerCard_ShouldLeaveOutWhatWasCancelled()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos, _) = await RestaurantAsync(fixture, tacos: 10, beer: 6);
        var opened = await fixture.CreateOrderHandler(Inventory(fixture)).Handle(
            new CreateOrderCommand
            {
                OpenTab = true,
                TableNumber = "Mesa 1",
                CustomerName = "Ana",
                CustomerPhone = "5512345678",
                Items = [new CreateOrderItemDto { ProductId = tacos.Id, Quantity = 3 }]
            },
            CancellationToken.None);
        Assert.True(opened.IsSuccess, opened.Error);
        Assert.True((await CancelAsync(fixture, opened.Value.Id, await LineAsync(fixture, tacos), 2, "Se equivocó el mesero")).IsSuccess);

        var card = await new Customers.Queries.GetCustomerCardQueryHandler(
                fixture.Context, new PhoneNormalizationService(), fixture.TenantService, fixture.Clock)
            .Handle(new Customers.Queries.GetCustomerCardQuery { Phone = "5512345678" }, CancellationToken.None);

        Assert.True(card.IsSuccess, card.Error);
        Assert.Equal(1m, Assert.Single(card.Value.FavoriteProducts).Quantity);
    }

    [Fact]
    public async Task ActivityLog_ShouldRecordWhatWasCancelledAndWhy()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, Restaurant, pricesIncludeTax: true);
        var orderId = Guid.NewGuid();
        var cancelled = new TabItemCancelledEvent(orderId, tenant.Id, Guid.NewGuid(), Guid.NewGuid(), 2, "Tacos (Sin cebolla)", 1, 25m, "Se equivocó el mesero", false);

        await new OrderAuditHandlers(fixture.Context).HandleAsync(
            cancelled,
            new OutboxEventContext(cancelled.EventId, tenant.Id, null, "Ana", SalesTestFixture.Now, 1),
            CancellationToken.None);
        await fixture.Context.SaveChangesAsync();

        var entry = await fixture.Context.AuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal(("Order", orderId.ToString(), "OrderItemCancelled", "Ana", SalesTestFixture.Now),
            (entry.EntityName, entry.EntityId, entry.Action, entry.PerformedBy, entry.Timestamp));
        var metadata = JsonDocument.Parse(entry.Metadata!).RootElement;
        Assert.Equal("Tacos (Sin cebolla)", metadata.GetProperty("ProductName").GetString());
        Assert.Equal(2, metadata.GetProperty("Round").GetInt32());
        Assert.Equal(1m, metadata.GetProperty("Quantity").GetDecimal());
        Assert.Equal(25m, metadata.GetProperty("Amount").GetDecimal());
        Assert.Equal("Se equivocó el mesero", metadata.GetProperty("Reason").GetString());
        Assert.False(metadata.GetProperty("ReturnedToStock").GetBoolean());
    }

    [Theory]
    [InlineData(0, "Se equivocó")]
    [InlineData(10_001, "Se equivocó")]
    [InlineData(1.0005, "Se equivocó")]
    [InlineData(1, "")]
    public void Validator_ShouldRefuseImpossibleRequests(decimal quantity, string reason)
    {
        var result = new CancelTabItemCommandValidator().Validate(new CancelTabItemCommand
        {
            OrderId = Guid.NewGuid(),
            OrderItemId = Guid.NewGuid(),
            Quantity = quantity,
            Reason = reason
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validator_ShouldAcceptAWeighedDishWithItsReason()
    {
        var result = new CancelTabItemCommandValidator().Validate(new CancelTabItemCommand
        {
            OrderId = Guid.NewGuid(),
            OrderItemId = Guid.NewGuid(),
            Quantity = 0.255m,
            Reason = "Se pasó de cocción"
        });

        Assert.True(result.IsValid);
        Assert.True(new CancelTabItemCommand().ReturnToStock);
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
        return opened.Value;
    }

    private static Task<Result<OrderDto>> AddRoundAsync(SalesTestFixture fixture, Guid orderId, Product product, decimal quantity)
        => new AddTabRoundCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock, Inventory(fixture))
            .Handle(new AddTabRoundCommand { OrderId = orderId, Items = [new CreateOrderItemDto { ProductId = product.Id, Quantity = quantity }] },
                CancellationToken.None);

    private static Task<Result<OrderDto>> CancelAsync(
        SalesTestFixture fixture,
        Guid orderId,
        Guid orderItemId,
        decimal quantity,
        string reason,
        bool returnToStock = true)
        => new CancelTabItemCommandHandler(fixture.Context, fixture.CurrentUser, fixture.Clock, Inventory(fixture))
            .Handle(new CancelTabItemCommand
            {
                OrderId = orderId,
                OrderItemId = orderItemId,
                Quantity = quantity,
                Reason = reason,
                ReturnToStock = returnToStock
            }, CancellationToken.None);

    private static Task<Guid> LineAsync(SalesTestFixture fixture, Product product)
        => fixture.Context.OrderItems.AsNoTracking().Where(i => i.ProductId == product.Id).Select(i => i.Id).SingleAsync();

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

    /// <summary>The business's movements are numbered without gaps: a cancellation that returns nothing takes no folio.</summary>
    private static async Task AssertFoliosInARowAsync(SalesTestFixture fixture)
    {
        var numbers = await fixture.Context.InventoryMovements.AsNoTracking()
            .Select(m => m.MovementNumber)
            .ToListAsync();
        Assert.Equal(
            Enumerable.Range(1, numbers.Count),
            numbers.Select(n => int.Parse(n[(n.LastIndexOf('-') + 1)..])).Order());
    }
}
