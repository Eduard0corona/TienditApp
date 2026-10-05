using FoodParkDigital.Application.Catalog.Commands;
using FoodParkDigital.Application.Dining;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>Selling with variants and extras, and serving a table with an open tab from opening to closing.</summary>
public class OptionsAndTabsFlowTests
{
    private const TenantCapability Restaurant = TenantCapability.Food | TenantCapability.Kitchen;

    [Fact]
    public async Task Sale_WithOptions_ShouldChargeThemAndKeepThemOnTheLine()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, Restaurant, pricesIncludeTax: true);
        var latte = await AddLatteAsync(fixture, tenant);
        var grande = Option(latte, "Grande");
        var almond = Option(latte, "Almendra");

        var result = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand
            {
                Items = [new CreateOrderItemDto { ProductId = latte.Id, Quantity = 2, OptionIds = [almond.Id, grande.Id] }],
                ExpectedTotal = 150m
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var line = Assert.Single(result.Value.Items);
        Assert.Equal(75m, line.UnitPrice);
        Assert.Equal(150m, line.TotalPrice);
        Assert.Equal(["Almendra", "Grande"], line.Options.Select(o => o.OptionName).Order());
        var stored = await fixture.Context.OrderItemOptions.Where(o => o.OrderItemId == line.Id).ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.All(stored, option => Assert.Equal(tenant.Id, option.TenantId));
    }

    [Fact]
    public async Task Sale_WithoutARequiredOption_ShouldBeRejected()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, Restaurant, pricesIncludeTax: true);
        var latte = await AddLatteAsync(fixture, tenant);

        var error = await Assert.ThrowsAsync<DomainException>(() => fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand { Items = [new CreateOrderItemDto { ProductId = latte.Id, Quantity = 1 }] },
            CancellationToken.None));

        Assert.Contains("Tamaño", error.Message);
        Assert.Empty(await fixture.Context.Orders.ToListAsync());
    }

    [Fact]
    public async Task OptionGroups_ShouldBeSavedAndKeepTheirIdsWhenEdited()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, Restaurant);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 29m);
        var handler = new SetProductOptionGroupsCommandHandler(fixture.Context, fixture.Mapper);

        var created = await handler.Handle(new SetProductOptionGroupsCommand
        {
            ProductId = soda.Id,
            Groups =
            [
                new ProductOptionGroupInput
                {
                    Name = "Tamaño", MinSelections = 1, MaxSelections = 1,
                    Options = [new ProductOptionInput { Name = "600 ml" }, new ProductOptionInput { Name = "2 L", PriceDelta = 21m }]
                }
            ]
        }, CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error);
        var group = Assert.Single(created.Value.OptionGroups);

        var edited = await handler.Handle(new SetProductOptionGroupsCommand
        {
            ProductId = soda.Id,
            Groups =
            [
                new ProductOptionGroupInput
                {
                    Id = group.Id, Name = "Tamaño", MinSelections = 1, MaxSelections = 1,
                    Options = [new ProductOptionInput { Id = group.Options[1].Id, Name = "2 L", PriceDelta = 23m }]
                }
            ]
        }, CancellationToken.None);

        Assert.True(edited.IsSuccess, edited.Error);
        var editedGroup = Assert.Single(edited.Value.OptionGroups);
        Assert.Equal(group.Id, editedGroup.Id);
        var option = Assert.Single(editedGroup.Options);
        Assert.Equal(group.Options[1].Id, option.Id);
        Assert.Equal(23m, option.PriceDelta);
        Assert.Single(await fixture.Context.ProductOptions.ToListAsync());
    }

    [Fact]
    public void OptionGroupsValidator_ShouldRejectAMinimumAboveTheMaximum()
    {
        var result = new SetProductOptionGroupsCommandValidator().Validate(new SetProductOptionGroupsCommand
        {
            ProductId = Guid.NewGuid(),
            Groups = [new ProductOptionGroupInput { Name = "Salsas", MinSelections = 3, MaxSelections = 1, Options = [new ProductOptionInput { Name = "Verde" }] }]
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Table_ShouldBeServedFromOpeningToClosing()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, Restaurant, pricesIncludeTax: true);
        var tacos = await fixture.AddProductAsync(tenant, "Tacos al pastor", 25m);
        var beer = await fixture.AddProductAsync(tenant, "Cerveza", 45m);
        var table = await CreateTableAsync(fixture, "Mesa 4");
        var inventory = new RecordingInventoryService();

        // Opening: the first round goes to the kitchen and nothing is consumed yet.
        var opened = await fixture.CreateOrderHandler(inventory).Handle(
            new CreateOrderCommand
            {
                OpenTab = true,
                DiningTableId = table.Id,
                Items = [new CreateOrderItemDto { ProductId = tacos.Id, Quantity = 3, Notes = "sin cebolla" }]
            },
            CancellationToken.None);
        Assert.True(opened.IsSuccess, opened.Error);
        Assert.True(opened.Value.IsOpenTab);
        Assert.Equal(table.Id, opened.Value.DiningTableId);
        Assert.Equal("Mesa 4", opened.Value.TableNumber);
        Assert.Empty(opened.Value.AllowedTransitions);
        Assert.Single(opened.Value.KitchenTickets);
        Assert.Equal(0, inventory.Consumed);

        var secondTab = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand { OpenTab = true, DiningTableId = table.Id, Items = [new CreateOrderItemDto { ProductId = beer.Id, Quantity = 1 }] },
            CancellationToken.None);
        Assert.True(secondTab.IsFailure);
        Assert.Contains("ya tiene una cuenta abierta", secondTab.Error);

        // A second round.
        var round = await new AddTabRoundCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock, fixture.Mapper)
            .Handle(new AddTabRoundCommand { OrderId = opened.Value.Id, Items = [new CreateOrderItemDto { ProductId = beer.Id, Quantity = 2 }] }, CancellationToken.None);
        Assert.True(round.IsSuccess, round.Error);
        Assert.Equal(165m, round.Value.Total);
        Assert.Equal([1, 2], round.Value.KitchenTickets.Select(t => t.Round));

        // The kitchen sees each round with its own lines.
        var tickets = await new GetKitchenTicketsQueryHandler(fixture.Context, fixture.TenantService, fixture.Mapper)
            .Handle(new GetKitchenTicketsQuery(), CancellationToken.None);
        Assert.Equal(2, tickets.Count);
        Assert.Equal("Tacos al pastor", Assert.Single(tickets[0].Items).ProductName);
        Assert.Equal("Cerveza", Assert.Single(tickets[1].Items).ProductName);
        var kitchenOrders = await new GetKitchenOrdersQueryHandler(fixture.Context, fixture.TenantService, fixture.Mapper)
            .Handle(new GetKitchenOrdersQuery(), CancellationToken.None);
        Assert.Empty(kitchenOrders);

        var ticketHandler = new UpdateKitchenTicketStatusCommandHandler(fixture.Context, fixture.Clock);
        Assert.True((await ticketHandler.Handle(new UpdateKitchenTicketStatusCommand(tickets[0].Id, KitchenTicketStatus.Ready), CancellationToken.None)).IsSuccess);
        Assert.True((await ticketHandler.Handle(new UpdateKitchenTicketStatusCommand(tickets[0].Id, KitchenTicketStatus.Served), CancellationToken.None)).IsSuccess);
        var remaining = await new GetKitchenTicketsQueryHandler(fixture.Context, fixture.TenantService, fixture.Mapper)
            .Handle(new GetKitchenTicketsQuery(), CancellationToken.None);
        Assert.Equal(2, Assert.Single(remaining).Round);

        // The dining room shows the table busy with its bill.
        var room = await new GetDiningRoomQueryHandler(fixture.Context).Handle(new GetDiningRoomQuery(), CancellationToken.None);
        var busy = Assert.Single(room.Tables);
        Assert.Equal(165m, busy.OpenTab!.Balance);
        Assert.Equal(1, busy.OpenTab.RoundsInKitchen);

        // Closing needs the bill paid; then the table is free and the inventory consumed once.
        var closeHandler = new CloseTabCommandHandler(fixture.Context, fixture.CurrentUser, fixture.Mapper, inventory);
        var unpaid = await closeHandler.Handle(new CloseTabCommand(opened.Value.Id), CancellationToken.None);
        Assert.True(unpaid.IsFailure);

        var payment = await fixture.AddPaymentHandler().Handle(
            new AddOrderPaymentCommand { OrderId = opened.Value.Id, Amount = 165m, Method = OrderPaymentMethod.Cash },
            CancellationToken.None);
        Assert.True(payment.IsSuccess, payment.Error);
        Assert.True(payment.Value.IsOpenTab);

        var closed = await closeHandler.Handle(new CloseTabCommand(opened.Value.Id), CancellationToken.None);
        Assert.True(closed.IsSuccess, closed.Error);
        Assert.Equal(OrderStatus.Delivered, closed.Value.Status);
        Assert.False(closed.Value.IsOpenTab);
        Assert.Equal(1, inventory.Consumed);
        var freeRoom = await new GetDiningRoomQueryHandler(fixture.Context).Handle(new GetDiningRoomQuery(), CancellationToken.None);
        Assert.Null(Assert.Single(freeRoom.Tables).OpenTab);
    }

    [Fact]
    public async Task OpenTab_ShouldNotBeDeliveredByHand()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, Restaurant);
        var tacos = await fixture.AddProductAsync(tenant, "Tacos", 25m);
        var opened = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand { OpenTab = true, TableNumber = "Barra", Items = [new CreateOrderItemDto { ProductId = tacos.Id, Quantity = 1 }] },
            CancellationToken.None);
        Assert.True(opened.IsSuccess, opened.Error);

        var result = await new UpdateOrderStatusCommandHandler(fixture.Context, fixture.CurrentUser, NoOpOrderInventoryService.Instance)
            .Handle(new UpdateOrderStatusCommand { OrderId = opened.Value.Id, NewStatus = OrderStatus.Delivered }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("cuenta abierta", result.Error);
    }

    [Fact]
    public async Task OpenTab_ShouldNeedTheFoodModule()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var soap = await fixture.AddProductAsync(tenant, "Jabón", 20m);

        var result = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand { OpenTab = true, TableNumber = "Caja 1", Items = [new CreateOrderItemDto { ProductId = soap.Id, Quantity = 1 }] },
            CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task MoveTab_ShouldTakeTheTabToAFreeTable()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, Restaurant);
        var tacos = await fixture.AddProductAsync(tenant, "Tacos", 25m);
        var inside = await CreateTableAsync(fixture, "Mesa 1");
        var terrace = await CreateTableAsync(fixture, "Terraza 1");
        var opened = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand { OpenTab = true, DiningTableId = inside.Id, Items = [new CreateOrderItemDto { ProductId = tacos.Id, Quantity = 1 }] },
            CancellationToken.None);

        var moved = await new MoveTabCommandHandler(fixture.Context, fixture.Mapper)
            .Handle(new MoveTabCommand(opened.Value.Id, terrace.Id), CancellationToken.None);

        Assert.True(moved.IsSuccess, moved.Error);
        Assert.Equal(terrace.Id, moved.Value.DiningTableId);
        Assert.Equal("Terraza 1", moved.Value.TableNumber);
        var deactivated = await new UpdateDiningTableCommandHandler(fixture.Context)
            .Handle(new UpdateDiningTableCommand { Id = terrace.Id, Name = "Terraza 1", IsActive = false }, CancellationToken.None);
        Assert.True(deactivated.IsFailure);
    }

    [Fact]
    public async Task Tables_ShouldHaveUniqueNames()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Food, Restaurant);
        await CreateTableAsync(fixture, "Mesa 1");

        var duplicated = await new CreateDiningTableCommandHandler(fixture.Context, fixture.TenantService)
            .Handle(new CreateDiningTableCommand { Name = " Mesa 1 " }, CancellationToken.None);

        Assert.True(duplicated.IsFailure);
    }

    private static async Task<DiningTableDto> CreateTableAsync(SalesTestFixture fixture, string name)
    {
        var result = await new CreateDiningTableCommandHandler(fixture.Context, fixture.TenantService)
            .Handle(new CreateDiningTableCommand { Name = name, Seats = 4 }, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value;
    }

    private static async Task<Product> AddLatteAsync(SalesTestFixture fixture, Tenant tenant)
    {
        var latte = await fixture.AddProductAsync(tenant, "Latte", 55m);
        latte.SetOptionGroups(
        [
            new ProductOptionGroupDefinition(null, "Tamaño", 1, 1,
            [
                new ProductOptionDefinition(null, "Chico", -5m),
                new ProductOptionDefinition(null, "Grande", 12m)
            ]),
            new ProductOptionGroupDefinition(null, "Leche", 0, 1,
            [
                new ProductOptionDefinition(null, "Deslactosada", 0m),
                new ProductOptionDefinition(null, "Almendra", 8m)
            ])
        ]);
        await fixture.Context.SaveChangesAsync();
        return latte;
    }

    private static ProductOption Option(Product product, string name)
        => product.OptionGroups.SelectMany(g => g.Options).Single(o => o.Name == name);

    private sealed class RecordingInventoryService : FoodParkDigital.Application.Common.Interfaces.IOrderInventoryService
    {
        public int Consumed { get; private set; }

        public Task<IReadOnlyList<FoodParkDigital.Application.Common.Models.StockShortage>> ApplyConsumptionForOrderAsync(
            FoodParkDigital.Domain.Orders.Entities.Order order,
            CancellationToken cancellationToken,
            FoodParkDigital.Application.Common.Models.StockShortageHandling shortageHandling)
        {
            // Mirrors OrderInventoryService: open tabs consume when they close.
            if (!order.IsOpenTab)
                Consumed++;
            return Task.FromResult<IReadOnlyList<FoodParkDigital.Application.Common.Models.StockShortage>>([]);
        }

        public Task<IReadOnlyList<FoodParkDigital.Application.Common.Models.StockShortage>> ApplyConsumptionForServicePieceAsync(
            FoodParkDigital.Domain.Orders.Entities.Order order,
            FoodParkDigital.Domain.Orders.Entities.ServiceOrderPiece piece,
            CancellationToken cancellationToken,
            FoodParkDigital.Application.Common.Models.StockShortageHandling shortageHandling)
            => Task.FromResult<IReadOnlyList<FoodParkDigital.Application.Common.Models.StockShortage>>([]);

        public Task ReverseConsumptionForOrderAsync(FoodParkDigital.Domain.Orders.Entities.Order order, string? reason, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task ReturnItemsToInventoryAsync(
            FoodParkDigital.Domain.Orders.Entities.Order order,
            FoodParkDigital.Domain.Orders.Entities.OrderRefund refund,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
