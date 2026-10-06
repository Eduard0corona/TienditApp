using FoodParkDigital.Application.Finance.Commands;
using FoodParkDigital.Application.Finance.DTOs;
using FoodParkDigital.Application.Finance.Queries;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders.Enums;
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

    private static CreateOrderCommand CashSale(Guid productId, int quantity, decimal amount) => new()
    {
        Items = [new CreateOrderItemDto { ProductId = productId, Quantity = quantity }],
        Payments = [new CreateOrderPaymentDto { Amount = amount, Method = OrderPaymentMethod.Cash }]
    };

    private static Task<List<Domain.Finance.Entities.CashTransaction>> MovementsAsync(SalesTestFixture fixture, Guid sessionId)
        => fixture.Context.CashTransactions.AsNoTracking().Where(t => t.CashSessionId == sessionId).ToListAsync();
}
