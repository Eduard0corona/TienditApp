using FoodParkDigital.Application.Finance.Commands;
using FoodParkDigital.Application.Finance.DTOs;
using FoodParkDigital.Application.Finance.Queries;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>Drawers per cashier and branch: who takes the cash, what each one counts and the shift history.</summary>
public class CashShiftTests
{
    private static readonly string Ana = Guid.NewGuid().ToString();
    private static readonly string Beto = Guid.NewGuid().ToString();

    [Fact]
    public async Task EachCashier_ShouldOpenOneDrawerAtTheDefaultBranch()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);

        var anaDrawer = await OpenAsync(fixture, Ana, 500m);
        var anaAgain = await OpenAsync(fixture, Ana, 100m);
        var betoDrawer = await OpenAsync(fixture, Beto, 300m);

        Assert.True(anaDrawer.IsSuccess, anaDrawer.Error);
        Assert.True(anaDrawer.Value.IsMine);
        Assert.Equal("Mostrador", anaDrawer.Value.InventoryLocationName);
        Assert.False(anaAgain.IsSuccess);
        Assert.Equal(OpenCashSessionCommandHandler.AlreadyOpenMessage, anaAgain.Error);
        Assert.True(betoDrawer.IsSuccess, betoDrawer.Error);
    }

    [Fact]
    public async Task CashPayment_ShouldEnterTheDrawerOfTheCashierWhoTookIt()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        var anaDrawer = (await OpenAsync(fixture, Ana, 500m)).Value;
        var betoDrawer = (await OpenAsync(fixture, Beto, 300m)).Value;

        fixture.CurrentUser.UserId = Beto;
        var sale = await fixture.CreateOrderHandler().Handle(CashSale(soda.Id, 2, 46.40m), CancellationToken.None);

        Assert.True(sale.IsSuccess, sale.Error);
        Assert.Empty(await MovementsAsync(fixture, anaDrawer.Id));
        var movement = Assert.Single(await MovementsAsync(fixture, betoDrawer.Id));
        Assert.Equal(46.40m, movement.Amount);
    }

    [Fact]
    public async Task PaymentNoDrawerTakes_ShouldBeRefused_WhateverTheMethod()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        await OpenAsync(fixture, Ana, 500m);
        await OpenAsync(fixture, Beto, 300m);

        // A third cashier with no drawer: two are open, so the payment would land in someone else's count.
        fixture.CurrentUser.UserId = Guid.NewGuid().ToString();
        var cash = await fixture.CreateOrderHandler().Handle(CashSale(soda.Id, 1, 23.20m), CancellationToken.None);
        var card = await fixture.CreateOrderHandler().Handle(new CreateOrderCommand
        {
            Items = [new CreateOrderItemDto { ProductId = soda.Id, Quantity = 1 }],
            Payments = [new CreateOrderPaymentDto { Amount = 23.20m, Method = OrderPaymentMethod.Card }]
        }, CancellationToken.None);

        Assert.False(cash.IsSuccess);
        Assert.Equal(OrderPaymentRegistrar.NoOpenDrawerMessage, cash.Error);
        Assert.False(card.IsSuccess);
        Assert.Equal(OrderPaymentRegistrar.NoOpenDrawerMessage, card.Error);
        Assert.Empty(await fixture.Context.Orders.ToListAsync());
        Assert.Empty(await fixture.Context.CashTransactions.ToListAsync());
    }

    [Fact]
    public async Task PaymentWithoutAnyOpenDrawer_ShouldBeRefused_ButAnUnpaidOrderIsSaved()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        fixture.CurrentUser.UserId = Ana;

        var transfer = await fixture.CreateOrderHandler().Handle(new CreateOrderCommand
        {
            Items = [new CreateOrderItemDto { ProductId = soda.Id, Quantity = 1 }],
            Payments = [new CreateOrderPaymentDto { Amount = 23.20m, Method = OrderPaymentMethod.Transfer }]
        }, CancellationToken.None);
        var unpaid = await fixture.CreateOrderHandler().Handle(new CreateOrderCommand
        {
            Items = [new CreateOrderItemDto { ProductId = soda.Id, Quantity = 1 }]
        }, CancellationToken.None);
        var charge = await fixture.AddPaymentHandler().Handle(new AddOrderPaymentCommand
        {
            OrderId = unpaid.Value.Id,
            Amount = 23.20m,
            Method = OrderPaymentMethod.Cash
        }, CancellationToken.None);

        Assert.Equal(OrderPaymentRegistrar.NoOpenDrawerMessage, transfer.Error);
        // Saving an order without charging it needs no drawer; charging it later does.
        Assert.True(unpaid.IsSuccess, unpaid.Error);
        Assert.Equal(OrderPaymentRegistrar.NoOpenDrawerMessage, charge.Error);
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(23.20m, (await fixture.Context.Orders.SingleAsync()).Balance);
        Assert.Empty(await fixture.Context.OrderPayments.ToListAsync());
    }

    [Fact]
    public async Task UnpaidOrder_ShouldBeChargedIntoTheDrawerOpenWhenItIsCharged()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        fixture.CurrentUser.UserId = Ana;
        var unpaid = await fixture.CreateOrderHandler().Handle(new CreateOrderCommand
        {
            Items = [new CreateOrderItemDto { ProductId = soda.Id, Quantity = 1 }]
        }, CancellationToken.None);
        var drawer = (await OpenAsync(fixture, Ana, 500m)).Value;

        var charge = await fixture.AddPaymentHandler().Handle(new AddOrderPaymentCommand
        {
            OrderId = unpaid.Value.Id,
            Amount = 23.20m,
            Method = OrderPaymentMethod.Card
        }, CancellationToken.None);

        Assert.True(charge.IsSuccess, charge.Error);
        var movement = Assert.Single(await MovementsAsync(fixture, drawer.Id));
        Assert.Equal(23.20m, movement.Amount);
        Assert.Equal(OrderPaymentMethod.Card, movement.PaymentMethod);
    }

    [Fact]
    public async Task Close_ShouldKeepExpectedAndDifference_AndRequireANoteForADifference()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        var drawer = (await OpenAsync(fixture, Ana, 500m)).Value;
        await fixture.CreateOrderHandler().Handle(CashSale(soda.Id, 5, 116m), CancellationToken.None);

        var withoutNote = await CloseAsync(fixture, drawer.Id, 600m, notes: null);
        var withNote = await CloseAsync(fixture, drawer.Id, 600m, "Faltó un billete de 20; se revisó con el gerente.");

        Assert.False(withoutNote.IsSuccess);
        Assert.Equal("Explica el faltante en las notas del cierre.", withoutNote.Error);
        Assert.True(withNote.IsSuccess, withNote.Error);
        Assert.Equal(616m, withNote.Value.ExpectedClosingAmount);
        Assert.Equal(-16m, withNote.Value.ClosingDifference);
        Assert.Equal(CashSessionStatus.Closed, withNote.Value.Status);
    }

    [Fact]
    public async Task Close_ShouldKeepTheCountByDenomination_WhenItAddsUp()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var drawer = (await OpenAsync(fixture, Ana, 700m)).Value;

        var closed = await CloseAsync(fixture, drawer.Id, 700m, notes: null,
            [new CashCountLineDto { Denomination = 500m, Quantity = 1 }, new CashCountLineDto { Denomination = 100m, Quantity = 2 }]);

        Assert.True(closed.IsSuccess, closed.Error);
        Assert.Equal(0m, closed.Value.ClosingDifference);
        Assert.Equal(2, closed.Value.CashCount.Count);
        Assert.False(new CloseCashSessionCommandValidator().Validate(new CloseCashSessionCommand
        {
            CashSessionId = drawer.Id,
            ClosingAmount = 700m,
            CashCount = [new CashCountLineDto { Denomination = 500m, Quantity = 1 }]
        }).IsValid);
    }

    [Fact]
    public async Task OnlyTheCashierOrAnAdmin_ShouldCloseADrawer()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var anaDrawer = (await OpenAsync(fixture, Ana, 100m)).Value;

        fixture.CurrentUser.UserId = Beto;
        fixture.CurrentUser.Role = "STAFF";
        fixture.CurrentUser.TenantRole = "STAFF";
        var byBeto = await CloseAsync(fixture, anaDrawer.Id, 100m, notes: null, asUser: Beto);

        fixture.CurrentUser.Role = "TENANT_ADMIN";
        fixture.CurrentUser.TenantRole = "OWNER";
        var byOwner = await CloseAsync(fixture, anaDrawer.Id, 100m, notes: null, asUser: Beto);

        Assert.False(byBeto.IsSuccess);
        Assert.Equal(CloseCashSessionCommandHandler.NotYourDrawerMessage, byBeto.Error);
        Assert.True(byOwner.IsSuccess, byOwner.Error);
    }

    [Fact]
    public async Task History_ShouldListShiftsWithTheirDifferences()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var exact = (await OpenAsync(fixture, Ana, 100m)).Value;
        await CloseAsync(fixture, exact.Id, 100m, notes: null);
        var shortShift = (await OpenAsync(fixture, Ana, 200m)).Value;
        await CloseAsync(fixture, shortShift.Id, 150m, "Pago a proveedor sin registrar.");
        var open = (await OpenAsync(fixture, Beto, 50m)).Value;

        fixture.CurrentUser.UserId = Ana;
        var all = await new GetCashSessionsQueryHandler(fixture.Context, fixture.CurrentUser)
            .Handle(new GetCashSessionsQuery(), CancellationToken.None);
        var withDifference = await new GetCashSessionsQueryHandler(fixture.Context, fixture.CurrentUser)
            .Handle(new GetCashSessionsQuery { OnlyWithDifference = true }, CancellationToken.None);

        Assert.Equal(3, all.TotalCount);
        var openRow = Assert.Single(all.Items, s => s.Id == open.Id);
        Assert.Equal(50m, openRow.ExpectedBalance);
        Assert.False(openRow.IsMine);
        var shortRow = Assert.Single(withDifference.Items);
        Assert.Equal(shortShift.Id, shortRow.Id);
        Assert.Equal(-50m, shortRow.ClosingDifference);
        Assert.True(shortRow.IsMine);
    }

    [Fact]
    public async Task DailyCutNetCash_ShouldBeWhatTheShiftsCount_WhateverTheRefundMethod()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 50m);
        await OpenAsync(fixture, Ana, 500m);
        await OpenAsync(fixture, Beto, 300m);
        fixture.CurrentUser.UserId = Ana;
        var sale = await fixture.CreateOrderHandler().Handle(new CreateOrderCommand
        {
            Items = [new CreateOrderItemDto { ProductId = soda.Id, Quantity = 3 }],
            Payments =
            [
                new CreateOrderPaymentDto { Amount = 100m, Method = OrderPaymentMethod.Cash },
                new CreateOrderPaymentDto { Amount = 50m, Method = OrderPaymentMethod.Card }
            ]
        }, CancellationToken.None);
        Assert.True(sale.IsSuccess, sale.Error);

        // Ana gives 20 back by card and Beto 30 in cash, each through their own drawer.
        await RefundAsync(fixture, Ana, sale.Value.Id, 20m, OrderPaymentMethod.Card);
        await RefundAsync(fixture, Beto, sale.Value.Id, 30m, OrderPaymentMethod.Cash);
        var cut = await new GetDailyCashCutQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetDailyCashCutQuery(), CancellationToken.None);
        var shifts = (await new GetCashSessionsQueryHandler(fixture.Context, fixture.CurrentUser)
            .Handle(new GetCashSessionsQuery(), CancellationToken.None)).Items;

        // The card refund left no drawer: only the 30 in cash comes out of the net cash.
        Assert.Equal(30m, cut.CashManualExpenseTotal);
        Assert.Equal(70m, cut.NetCashOnlyFlow);
        Assert.Equal(shifts.Sum(s => s.Inflows - s.Outflows), cut.NetCashOnlyFlow);
        // All the money still drops by both refunds: the drawers' cash plus what stayed by card.
        Assert.Equal(50m, cut.ManualExpenseTotal);
        Assert.Equal(100m, cut.NetCashFlow);
        Assert.Equal(shifts.Sum(s => s.Inflows - s.Outflows + s.NonCashInflows), cut.NetCashFlow);
    }

    [Fact]
    public async Task DailyCutNetCash_ShouldLeaveOutManualMovementsOfOtherMethods()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var drawer = await fixture.OpenCashSessionAsync(tenant);
        drawer.RegisterTransaction(CashTransactionType.Deposit, 40m, SalesTestFixture.Now);
        drawer.RegisterTransaction(CashTransactionType.Deposit, 25m, SalesTestFixture.Now, OrderPaymentMethod.Transfer);
        drawer.RegisterTransaction(CashTransactionType.Expense, 15m, SalesTestFixture.Now, OrderPaymentMethod.Cash);
        await fixture.Context.SaveChangesAsync();

        var cut = await new GetDailyCashCutQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetDailyCashCutQuery(), CancellationToken.None);

        Assert.Equal(65m, cut.ManualIncomeTotal);
        Assert.Equal(40m, cut.CashManualIncomeTotal);
        Assert.Equal(15m, cut.CashManualExpenseTotal);
        Assert.Equal(drawer.Inflows - drawer.Outflows, cut.NetCashOnlyFlow);
        Assert.Equal(25m, cut.NetCashOnlyFlow);
        Assert.Equal(50m, cut.NetCashFlow);
    }

    [Fact]
    public async Task DailyCut_ShouldShowWhatThePlatformsCharged_OutsideTheDrawers()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack, tacos) = await FoodShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        var drawer = (await OpenAsync(fixture, Ana, 500m)).Value;
        fixture.CurrentUser.UserId = Ana;
        await SellAsync(fixture, CashSale(tacos.Id, 1, 100m));
        // Beto has no drawer: Rappi charged the order in its app.
        fixture.CurrentUser.UserId = Beto;
        await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "BC953", pack.Id));

        var cut = await CutAsync(fixture);
        var shifts = await ShiftsAsync(fixture);

        Assert.Equal((450m, 2), (cut.SalesPaymentsTotal, cut.SalesPaymentsCount));
        Assert.Equal((350m, 1, 0m), (cut.PlatformTotal, cut.PlatformPaymentsCount, cut.PlatformRefundsTotal));
        Assert.Equal((100m, 0m, 0m), (cut.CashTotal, cut.CardTotal, cut.OtherTotal));
        // The drawers only saw the cash sale: what Rappi charged has no movement in any of them.
        var movement = Assert.Single(await MovementsAsync(fixture, drawer.Id));
        Assert.Equal(100m, movement.Amount);
        Assert.Equal(100m, cut.NetCashOnlyFlow);
        Assert.Equal(shifts.Sum(s => s.Inflows - s.Outflows), cut.NetCashOnlyFlow);
        Assert.Equal(0m, shifts.Sum(s => s.NonCashInflows));
        // The branch and the location where it was sold still count it.
        Assert.Equal(450m, Assert.Single(cut.ByBranch).TotalAmount);
        Assert.Equal(450m, Assert.Single(cut.ByLocation).TotalAmount);
    }

    public static TheoryData<OrderPaymentMethod> EveryPaymentMethod => new(Enum.GetValues<OrderPaymentMethod>());

    /// <summary>I5: the buckets of the cut add up to what was charged, whatever the method, today's and any added later.</summary>
    [Theory, MemberData(nameof(EveryPaymentMethod))]
    public async Task DailyCut_MethodBuckets_ShouldAddUpToCharged_ForEveryMethod(OrderPaymentMethod method)
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true);
        await SeedPaidOrderAsync(fixture, tenant, method, 245m);
        await SeedPaidOrderAsync(fixture, tenant, OrderPaymentMethod.Card, 10m);

        var cut = await CutAsync(fixture);

        Assert.Equal(255m, cut.SalesPaymentsTotal);
        Assert.Equal(
            cut.SalesPaymentsTotal,
            cut.CashTotal + cut.CardTotal + cut.TransferTotal + cut.WalletTotal + cut.OtherTotal + cut.PlatformTotal + cut.OnlineTotal);
        var bucket = method switch
        {
            OrderPaymentMethod.Cash => cut.CashTotal,
            OrderPaymentMethod.Card => cut.CardTotal - 10m,
            OrderPaymentMethod.Transfer => cut.TransferTotal,
            OrderPaymentMethod.DigitalWallet => cut.WalletTotal,
            OrderPaymentMethod.Platform => cut.PlatformTotal,
            OrderPaymentMethod.Online => cut.OnlineTotal,
            _ => cut.OtherTotal
        };
        Assert.Equal(245m, bucket);
    }

    [Fact]
    public async Task DailyCut_ShouldShowWhatWasPaidOnlineApart_OutOfTheCashiersAndOfTheMoneyOfTheDay()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true);
        await SeedPaidOrderAsync(fixture, tenant, OrderPaymentMethod.Cash, 100m, takenBy: "ana");
        // Paid online into the business's Mercado Pago: nobody took it at a counter.
        await SeedPaidOrderAsync(fixture, tenant, OrderPaymentMethod.Online, 235.50m);

        var cut = await CutAsync(fixture);

        Assert.Equal((335.50m, 235.50m, 1), (cut.SalesPaymentsTotal, cut.OnlineTotal, cut.OnlinePaymentsCount));
        Assert.Equal([("ana", 1, 100m)], cut.ByUser.Select(u => (u.UserName, u.PaymentsCount, u.TotalAmount)));
        // Mercado Pago releases it later, minus its fee: it is not money of the day.
        Assert.Equal(100m, cut.NetCashFlow);
        Assert.Equal(100m, cut.NetCashOnlyFlow);
    }

    [Fact]
    public async Task DailyCut_ByUser_ShouldLeaveOutWhatThePlatformsCharged()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true);
        // Ana takes a cash sale and captures an order Rappi charged in its app; Beto takes a card sale.
        await SeedPaidOrderAsync(fixture, tenant, OrderPaymentMethod.Cash, 100m, takenBy: "ana");
        await SeedPaidOrderAsync(fixture, tenant, OrderPaymentMethod.Platform, 350m, takenBy: "ana");
        await SeedPaidOrderAsync(fixture, tenant, OrderPaymentMethod.Card, 50m, takenBy: "beto");

        var cut = await CutAsync(fixture);

        // Ana never received what Rappi charged: her row has only the cash sale.
        Assert.Equal([("ana", 1, 100m), ("beto", 1, 50m)], cut.ByUser.Select(u => (u.UserName, u.PaymentsCount, u.TotalAmount)));
        Assert.Equal((500m, 350m), (cut.SalesPaymentsTotal, cut.PlatformTotal));
        Assert.Equal(500m, Assert.Single(cut.ByBranch).TotalAmount);
        Assert.Equal(500m, Assert.Single(cut.ByLocation).TotalAmount);
    }

    [Fact]
    public async Task DailyCut_NetCashFlow_ShouldLeaveOutThePlatforms()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack, tacos) = await FoodShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.DidiFood);
        await OpenAsync(fixture, Ana, 500m);
        fixture.CurrentUser.UserId = Ana;
        await SellAsync(fixture, CashSale(tacos.Id, 1, 100m));
        await SellAsync(fixture, new CreateOrderCommand
        {
            Items = [new CreateOrderItemDto { ProductId = tacos.Id, Quantity = 1 }],
            Payments = [new CreateOrderPaymentDto { Amount = 100m, Method = OrderPaymentMethod.Card }]
        });
        // Charged by Rappi in its app, and by the business when DiDi's courier paid in cash.
        await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "BC953", pack.Id));
        await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "12", tacos.Id, collectedBy: PlatformCollectedBy.Business) with
        {
            Payments = [new CreateOrderPaymentDto { Amount = 100m, Method = OrderPaymentMethod.Cash }]
        });

        var cut = await CutAsync(fixture);
        var shifts = await ShiftsAsync(fixture);

        Assert.Equal((650m, 350m), (cut.SalesPaymentsTotal, cut.PlatformTotal));
        // The money the business received is what its drawers saw: the cash and the card, not what Rappi will deposit.
        Assert.Equal(300m, cut.NetCashFlow);
        Assert.Equal(shifts.Sum(s => s.Inflows - s.Outflows + s.NonCashInflows), cut.NetCashFlow);
        Assert.Equal(200m, cut.NetCashOnlyFlow);
        Assert.Equal(shifts.Sum(s => s.Inflows - s.Outflows), cut.NetCashOnlyFlow);
    }

    [Fact]
    public async Task DailyCut_PlatformRefunds_ShouldBeShownApart()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack, tacos) = await FoodShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        var main = await fixture.DefaultBranchAsync();
        var norte = await fixture.AddBranchAsync(tenant, "Norte", "NTE");
        await OpenAsync(fixture, Ana, 500m);
        fixture.CurrentUser.UserId = Ana;
        var counter = await SellAsync(fixture, CashSale(tacos.Id, 1, 100m));
        var rappi = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "BC953", pack.Id));

        // Rappi cancelled the order: the void gives back as «Plataforma» what it charged, from no drawer. Ana also gives
        // 30 back in cash from hers.
        var voided = await new VoidOrderCommandHandler(
                fixture.Context, fixture.CurrentUser, new Finance.OrderCashService(fixture.Context, fixture.CurrentUser),
                fixture.Clock, NoOpOrderInventoryService.Instance)
            .Handle(new VoidOrderCommand { OrderId = rappi.Value.Id, Reason = "Rappi lo canceló", RestockItems = false }, CancellationToken.None);
        Assert.True(voided.IsSuccess, voided.Error);
        await RefundAsync(fixture, Ana, counter.Value.Id, 30m, OrderPaymentMethod.Cash);

        var cut = await CutAsync(fixture);
        var shifts = await ShiftsAsync(fixture);

        Assert.Equal((350m, 350m), (cut.PlatformTotal, cut.PlatformRefundsTotal));
        // Only the cash refund left a drawer: the platform's refund is no movement and no expense.
        Assert.Equal((30m, 30m), (cut.ManualExpenseTotal, cut.CashManualExpenseTotal));
        Assert.Equal((70m, 70m), (cut.NetCashFlow, cut.NetCashOnlyFlow));
        Assert.Equal(shifts.Sum(s => s.Inflows - s.Outflows + s.NonCashInflows), cut.NetCashFlow);

        // The filters of the payments: who gave it back, the branch of the order and the location of its «Plataforma» payment.
        var location = (await fixture.Context.OrderPayments.AsNoTracking().SingleAsync(p => p.Method == OrderPaymentMethod.Platform)).InventoryLocationId;
        var norteLocation = await fixture.Context.InventoryLocations.Where(l => l.BranchId == norte.Id).Select(l => l.Id).SingleAsync();
        Assert.NotNull(location);
        Assert.Equal(350m, (await CutAsync(fixture, new GetDailyCashCutQuery { UserName = "cajero" })).PlatformRefundsTotal);
        Assert.Equal(0m, (await CutAsync(fixture, new GetDailyCashCutQuery { UserName = "otra" })).PlatformRefundsTotal);
        Assert.Equal(350m, (await CutAsync(fixture, new GetDailyCashCutQuery { BranchId = main.Id })).PlatformRefundsTotal);
        Assert.Equal(0m, (await CutAsync(fixture, new GetDailyCashCutQuery { BranchId = norte.Id })).PlatformRefundsTotal);
        Assert.Equal(350m, (await CutAsync(fixture, new GetDailyCashCutQuery { InventoryLocationId = location })).PlatformRefundsTotal);
        Assert.Equal(0m, (await CutAsync(fixture, new GetDailyCashCutQuery { InventoryLocationId = norteLocation })).PlatformRefundsTotal);
        // Another day has neither the payment nor its refund.
        var yesterday = await CutAsync(fixture, new GetDailyCashCutQuery { Date = new DateOnly(2026, 10, 3) });
        Assert.Equal((0m, 0m), (yesterday.PlatformTotal, yesterday.PlatformRefundsTotal));
    }

    [Fact]
    public async Task CurrentDrawer_ShouldBeTheCashiersOwn()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        await OpenAsync(fixture, Ana, 100m);
        var betoDrawer = (await OpenAsync(fixture, Beto, 200m)).Value;

        fixture.CurrentUser.UserId = Beto;
        var current = await new GetCurrentCashSessionQueryHandler(fixture.Context, fixture.CurrentUser)
            .Handle(new GetCurrentCashSessionQuery(), CancellationToken.None);
        fixture.CurrentUser.UserId = Guid.NewGuid().ToString();
        var none = await new GetCurrentCashSessionQueryHandler(fixture.Context, fixture.CurrentUser)
            .Handle(new GetCurrentCashSessionQuery(), CancellationToken.None);

        Assert.Equal(betoDrawer.Id, current?.Id);
        Assert.Null(none);
    }

    [Fact]
    public async Task CurrentDrawer_AtABranch_ShouldBeTheOneThatWouldTakeTheUsersPayments()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        var main = await fixture.DefaultBranchAsync();
        var norte = await fixture.AddBranchAsync(tenant, "Norte", "NTE");
        fixture.CurrentUser.UserId = Beto;
        var betoDrawer = (await new OpenCashSessionCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock)
            .Handle(new OpenCashSessionCommand { OpeningAmount = 300m, BranchId = norte.Id }, CancellationToken.None)).Value;

        // Ana has no drawer: at Matriz none takes her payments, at Norte Beto's does.
        fixture.CurrentUser.UserId = Ana;
        var atMain = await CurrentAsync(fixture, main.Id);
        var atNorte = await CurrentAsync(fixture, norte.Id);
        var anywhere = await CurrentAsync(fixture, branchId: null);
        var saleAtMain = await fixture.CreateOrderHandler().Handle(CashSale(soda.Id, 1, 23.20m) with { BranchId = main.Id }, CancellationToken.None);
        var saleAtNorte = await fixture.CreateOrderHandler().Handle(CashSale(soda.Id, 1, 23.20m) with { BranchId = norte.Id }, CancellationToken.None);

        Assert.Null(atMain);
        Assert.Equal(OrderPaymentRegistrar.NoOpenDrawerMessage, saleAtMain.Error);
        Assert.Equal(betoDrawer.Id, atNorte?.Id);
        Assert.False(atNorte!.IsMine);
        Assert.True(saleAtNorte.IsSuccess, saleAtNorte.Error);
        Assert.Single(await MovementsAsync(fixture, betoDrawer.Id));
        // Without a branch, the only open drawer of the business stands in for hers.
        Assert.Equal(betoDrawer.Id, anywhere?.Id);
    }

    /// <summary>A food business whose prices include IVA: a family pack of $350 and an order of tacos of $100.</summary>
    private static async Task<(Tenant Tenant, Product Pack, Product Tacos)> FoodShopAsync(SalesTestFixture fixture)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true);
        var pack = await fixture.AddProductAsync(tenant, "Paquete familiar", 350m);
        var tacos = await fixture.AddProductAsync(tenant, "Orden de tacos", 100m);
        return (tenant, pack, tacos);
    }

    /// <summary>
    /// A paid order of the tenant in scope at the default branch, written through the aggregate with its payment taken by
    /// <paramref name="takenBy"/> (the in-memory fixture stamps no author). «Plataforma» is only made by registering an
    /// order of Rappi charged by the platform.
    /// </summary>
    private static async Task SeedPaidOrderAsync(
        SalesTestFixture fixture, Tenant tenant, OrderPaymentMethod method, decimal amount, string? takenBy = null)
    {
        var branch = await fixture.DefaultBranchAsync();
        var location = await fixture.Context.InventoryLocations.Where(l => l.BranchId == branch.Id).Select(l => l.Id).SingleAsync();
        var isPlatform = method == OrderPaymentMethod.Platform;
        var isOnline = method == OrderPaymentMethod.Online;
        var number = await fixture.Context.Orders.CountAsync() + 1;
        var channel = isPlatform ? OrderChannel.Rappi : isOnline ? OrderChannel.Online : OrderChannel.Counter;
        var order = new Order(
            tenant.Id, null, null, true, $"ORD-{number:000000}", BusinessType.Food, OrderType.Retail, "Público en general",
            null, null, null, pricesIncludeTax: true, channel: channel, branchId: branch.Id);
        order.AddItem(Guid.NewGuid(), "Paquete", 1, amount);
        if (isPlatform)
        {
            var setting = await fixture.Context.DeliveryPlatformSettings.FirstOrDefaultAsync()
                ?? await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
            order.RegisterPlatformOrder(setting, PlatformOrderNumber.From($"R{number}"), PlatformCollectedBy.Platform, SalesTestFixture.Now, location);
        }
        else if (isOnline)
        {
            // Paid online through the business's Mercado Pago before the order existed (Fase 24I).
            order.RegisterOnlinePayment($"{number}", SalesTestFixture.Now, location);
        }
        else
        {
            order.RegisterPayment(amount, method, SalesTestFixture.Now, location, null, null, null);
        }

        foreach (var payment in order.Payments)
            payment.CreatedBy = takenBy;
        fixture.Context.Orders.Add(order);
        await fixture.Context.SaveChangesAsync();
    }

    /// <summary>Each request with a context of its own, as in the app.</summary>
    private static async Task<Common.Models.Result<Application.Orders.DTOs.OrderDto>> SellAsync(SalesTestFixture fixture, CreateOrderCommand command)
    {
        var sale = await fixture.CreateOrderHandler().Handle(command, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        Assert.True(sale.IsSuccess, sale.Error);
        return sale;
    }

    private static Task<DailyCashCutDto> CutAsync(SalesTestFixture fixture, GetDailyCashCutQuery? query = null)
    {
        fixture.Context.ChangeTracker.Clear();
        return new GetDailyCashCutQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(query ?? new GetDailyCashCutQuery(), CancellationToken.None);
    }

    private static async Task<List<CashSessionDto>> ShiftsAsync(SalesTestFixture fixture)
        => (await new GetCashSessionsQueryHandler(fixture.Context, fixture.CurrentUser)
            .Handle(new GetCashSessionsQuery(), CancellationToken.None)).Items;

    private static Task<CashSessionDto?> CurrentAsync(SalesTestFixture fixture, Guid? branchId)
        => new GetCurrentCashSessionQueryHandler(fixture.Context, fixture.CurrentUser)
            .Handle(new GetCurrentCashSessionQuery { BranchId = branchId }, CancellationToken.None);

    private static async Task<Common.Models.Result<CashSessionDto>> OpenAsync(SalesTestFixture fixture, string userId, decimal amount)
    {
        fixture.CurrentUser.UserId = userId;
        return await new OpenCashSessionCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock)
            .Handle(new OpenCashSessionCommand { OpeningAmount = amount }, CancellationToken.None);
    }

    private static async Task<Common.Models.Result<CashSessionDto>> CloseAsync(
        SalesTestFixture fixture,
        Guid sessionId,
        decimal counted,
        string? notes,
        List<CashCountLineDto>? count = null,
        string? asUser = null)
    {
        if (asUser is null)
            fixture.CurrentUser.UserId = Ana;
        fixture.Context.ChangeTracker.Clear();
        return await new CloseCashSessionCommandHandler(fixture.Context, fixture.CurrentUser, TestAccess.For(fixture.CurrentUser, fixture.TenantService), fixture.Clock)
            .Handle(new CloseCashSessionCommand
            {
                CashSessionId = sessionId,
                ClosingAmount = counted,
                Notes = notes,
                CashCount = count
            }, CancellationToken.None);
    }

    private static async Task RefundAsync(SalesTestFixture fixture, string userId, Guid orderId, decimal amount, OrderPaymentMethod method)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.Context.ChangeTracker.Clear();
        var refund = await new RefundOrderCommandHandler(
                fixture.Context, fixture.CurrentUser, new Finance.OrderCashService(fixture.Context, fixture.CurrentUser),
                fixture.Clock, NoOpOrderInventoryService.Instance)
            .Handle(new RefundOrderCommand { OrderId = orderId, Amount = amount, Method = method, Reason = "Ajuste de precio" }, CancellationToken.None);
        Assert.True(refund.IsSuccess, refund.Error);
    }

    private static CreateOrderCommand CashSale(Guid productId, int quantity, decimal amount) => new()
    {
        Items = [new CreateOrderItemDto { ProductId = productId, Quantity = quantity }],
        Payments = [new CreateOrderPaymentDto { Amount = amount, Method = OrderPaymentMethod.Cash }]
    };

    private static Task<List<Domain.Finance.Entities.CashTransaction>> MovementsAsync(SalesTestFixture fixture, Guid sessionId)
        => fixture.Context.CashTransactions.AsNoTracking().Where(t => t.CashSessionId == sessionId).ToListAsync();
}
