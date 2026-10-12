using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.DeliveryPlatforms;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The settlements of the delivery platforms' deposits (Fase 22G): which orders are pending and what each brings, opening
/// one with the orders chosen, replacing it with the revision seen, closing it only when it balances (and with the figures
/// the person saw), voiding it to free its orders, and what each platform still owes.
/// </summary>
public class PlatformSettlementFlowTests
{
    private static readonly TimeZoneInfo Mexico = TenantDateTimeResolver.Resolve(null);
    private static readonly DateOnly Monday = new(2026, 10, 5);
    private static readonly DateOnly Sunday = new(2026, 10, 11);
    private static readonly DateOnly Today = new(2026, 10, 14);

    // $1,000 with IVA included, 30 % of commission on the total: charged by the platform it deposits 561.48; charged by the
    // business it deducts the commission and its IVA, 348.00.
    private const decimal PlatformNet = 561.48m;
    private const decimal BusinessNet = -348m;

    [Fact]
    public async Task Pending_ShouldBeTheOrdersOfThePlatformUpToThePeriod_WithWhatEachBringsNow()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        var before = await SellAtAsync(fixture, At(Monday.AddDays(-7), 13), Rappi(shop, "R0"));
        var paid = await SellAtAsync(fixture, At(Monday, 0, 30), Rappi(shop, "R1"));
        var halfRefunded = await SellAtAsync(fixture, At(Monday.AddDays(3), 14), Rappi(shop, "R2"));
        var refunded = await SellAtAsync(fixture, At(Monday.AddDays(4), 14), Rappi(shop, "R3"));
        var cash = await SellAtAsync(fixture, At(Monday.AddDays(5), 14), Rappi(shop, "R4", PlatformCollectedBy.Business));
        await SellAtAsync(fixture, At(Monday.AddDays(5), 15), Rappi(shop, "R5", PlatformCollectedBy.Business));
        await SellAtAsync(fixture, At(Sunday, 23, 59), Uber(shop, "U1"));
        await SellAtAsync(fixture, At(Sunday.AddDays(1), 0, 1), Rappi(shop, "R6"));
        await RefundAsync(fixture, halfRefunded.Id, 500m);
        await RefundAsync(fixture, refunded.Id, 1000m);
        await PayInCashAsync(fixture, shop.Tenant, cash.Id);

        var pending = await PendingAsync(fixture, OrderChannel.Rappi, Monday, Sunday);

        // The one of before still enters (a statement may bring it late); the refunded one and the one the business has
        // not collected bring nothing; Uber Eats and the one after Sunday are left out.
        Assert.Equal(
            [("R0", false, PlatformNet), ("R1", true, PlatformNet), ("R2", true, 280.74m), ("R4", true, BusinessNet)],
            pending.Orders.Select(order => (order.ExternalOrderNumber, order.InPeriod, order.ExpectedNet)));
        Assert.Equal([before.Id, paid.Id, halfRefunded.Id, cash.Id], pending.Orders.Select(order => order.OrderId));
        Assert.All(pending.Orders, order => Assert.Equal(order.ExpectedNet, order.CurrentExpectedNet));
        Assert.Equal(Monday, pending.Orders[1].RegisteredOn);
        Assert.False(pending.Truncated);
        Assert.Null(pending.OpenSettlementId);
    }

    [Fact]
    public async Task Create_ShouldCountEachOrder_AndKeepTheOthersAsCandidates()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        var first = await SellAtAsync(fixture, At(Monday, 12), Rappi(shop, "R1"));
        var second = await SellAtAsync(fixture, At(Monday.AddDays(1), 12), Rappi(shop, "R2"));
        var notInStatement = await SellAtAsync(fixture, At(Monday.AddDays(2), 12), Rappi(shop, "R3"));

        var result = await CreateAsync(fixture, Command(OrderChannel.Rappi, 1102.96m, [first.Id, second.Id],
            [new(PlatformSettlementAdjustmentKind.Claim, "Faltó un refresco", -20m)]));

        Assert.True(result.IsSuccess, result.Error);
        var detail = result.Value;
        Assert.Equal(PlatformSettlementStatus.Open, detail.Settlement.Status);
        Assert.Equal((1122.96m, -20m, 1102.96m, 0m, 2, 1),
            (detail.Settlement.OrdersTotal, detail.Settlement.AdjustmentsTotal, detail.Settlement.ExpectedDeposit,
             detail.Settlement.Difference, detail.Settlement.OrderCount, detail.Settlement.Revision));
        Assert.Equal([first.Id, second.Id], detail.Orders.Select(order => order.OrderId));
        Assert.Equal(("R3", notInStatement.Id), (Assert.Single(detail.Candidates).ExternalOrderNumber, detail.Candidates[0].OrderId));
        Assert.Equal("Faltó un refresco", Assert.Single(detail.Adjustments).Description);

        // Its orders are no longer pending for another one.
        Assert.Equal([notInStatement.Id], (await PendingAsync(fixture, OrderChannel.Rappi, Monday, Sunday)).Orders.Select(o => o.OrderId));
    }

    [Fact]
    public async Task Create_ShouldRefuse_ASecondOpenOne_AndOrdersOfAnotherPlatformOrBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        var uber = await SellAtAsync(fixture, At(Monday, 12), Uber(shop, "U1"));
        var rappi = await SellAtAsync(fixture, At(Monday, 13), Rappi(shop, "R1"));
        var otherShop = await ShopAsync(fixture, "otro");
        var foreign = await SellAtAsync(fixture, At(Monday, 12), Rappi(otherShop, "R1"));
        fixture.TenantService.SetTenant(shop.Tenant.Id);

        var ofUber = await CreateAsync(fixture, Command(OrderChannel.Rappi, PlatformNet, [rappi.Id, uber.Id]));
        var ofAnotherBusiness = await CreateAsync(fixture, Command(OrderChannel.Rappi, PlatformNet, [foreign.Id]));

        Assert.Equal(PlatformSettlementErrors.OrderNotOfThePlatform, ofUber.Error);
        Assert.Equal(PlatformSettlementErrors.OrderNotOfThePlatform, ofAnotherBusiness.Error);
        Assert.True((await CreateAsync(fixture, Command(OrderChannel.Rappi, PlatformNet, [rappi.Id]))).IsSuccess);
        var second = await CreateAsync(fixture, Command(OrderChannel.Rappi, 0m, []));
        Assert.Equal("Ya hay una liquidación abierta de Rappi: termínala o anúlala antes de abrir otra.", second.Error);
        Assert.True((await CreateAsync(fixture, Command(OrderChannel.UberEats, PlatformNet, [uber.Id]))).IsSuccess);
    }

    [Fact]
    public async Task Create_ShouldRefuse_AnOrderOfASettlementInForce_UntilItIsVoided()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        var order = await SellAtAsync(fixture, At(Monday, 12), Rappi(shop, "R1"));
        var first = (await CreateAsync(fixture, Command(OrderChannel.Rappi, PlatformNet, [order.Id]))).Value;
        var closed = await CloseAsync(fixture, first.Settlement.Id, first.Settlement.Revision);
        Assert.True(closed.IsSuccess, closed.Error);

        var taken = await CreateAsync(fixture, Command(OrderChannel.Rappi, PlatformNet, [order.Id]));
        Assert.Equal(PlatformSettlementErrors.OrderTaken, taken.Error);

        var voided = await VoidAsync(fixture, first.Settlement.Id, closed.Value.Settlement.Revision, "Era el depósito de otra semana");
        Assert.True(voided.IsSuccess, voided.Error);
        Assert.True((await CreateAsync(fixture, Command(OrderChannel.Rappi, PlatformNet, [order.Id]))).IsSuccess);
    }

    [Fact]
    public async Task Create_ShouldAnswerTheRulesOfTheDomain_AsBusinessRules()
    {
        await using var fixture = SalesTestFixture.Create();
        await ShopAsync(fixture);

        var future = await CreateAsync(fixture, Command(OrderChannel.Rappi, 0m, []) with { PeriodTo = Today.AddDays(1), DepositedOn = Today });
        var other = await CreateAsync(fixture, Command(OrderChannel.Rappi, 10m, [], [new(PlatformSettlementAdjustmentKind.Other, null, 10m)]));

        Assert.Equal(PlatformSettlement.PeriodInTheFutureMessage, future.Error);
        Assert.Equal(PlatformSettlement.AdjustmentDescriptionRequiredMessage, other.Error);
        Assert.Empty(await fixture.Context.PlatformSettlements.ToListAsync());
    }

    [Fact]
    public async Task Update_ShouldReplaceIt_WithTheRevisionSeen()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        var first = await SellAtAsync(fixture, At(Monday, 12), Rappi(shop, "R1"));
        var second = await SellAtAsync(fixture, At(Monday.AddDays(1), 12), Rappi(shop, "R2"));
        var created = (await CreateAsync(fixture, Command(OrderChannel.Rappi, PlatformNet, [first.Id]))).Value;

        var stale = await UpdateAsync(fixture, Update(created.Settlement.Id, revision: 0, 2 * PlatformNet, [first.Id, second.Id]));
        Assert.Equal(PlatformSettlementErrors.Stale, stale.Error);

        var updated = await UpdateAsync(fixture, Update(created.Settlement.Id, revision: 1, 2 * PlatformNet - 15m, [first.Id, second.Id],
            [new(PlatformSettlementAdjustmentKind.Fee, "Renta de la tableta", -15m)]));

        Assert.True(updated.IsSuccess, updated.Error);
        Assert.Equal((2, 2 * PlatformNet, -15m, 0m),
            (updated.Value.Settlement.Revision, updated.Value.Settlement.OrdersTotal, updated.Value.Settlement.AdjustmentsTotal, updated.Value.Settlement.Difference));
        Assert.Equal([first.Id, second.Id], updated.Value.Orders.Select(order => order.OrderId));
        Assert.Empty(updated.Value.Candidates);
    }

    [Fact]
    public async Task Close_ShouldNeedADifferenceOfZero_AndRecordWhoClosedItWithWhichFigures()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        var order = await SellAtAsync(fixture, At(Monday, 12), Rappi(shop, "R1"));
        var created = (await CreateAsync(fixture, Command(OrderChannel.Rappi, 540m, [order.Id]))).Value;

        var unbalanced = await CloseAsync(fixture, created.Settlement.Id, 1);
        Assert.Equal(PlatformSettlement.DoesNotBalanceMessage, unbalanced.Error);

        var explained = (await UpdateAsync(fixture, Update(created.Settlement.Id, 1, 540m, [order.Id],
            [new(PlatformSettlementAdjustmentKind.Promotion, "2x1 del jueves", -21.48m)]))).Value;
        var closed = await CloseAsync(fixture, created.Settlement.Id, explained.Settlement.Revision);

        Assert.True(closed.IsSuccess, closed.Error);
        Assert.Equal(PlatformSettlementStatus.Closed, closed.Value.Settlement.Status);
        Assert.Equal(At(Today, 12), closed.Value.Settlement.ClosedAt);
        Assert.Empty(closed.Value.Candidates);
        var entry = await fixture.Context.AuditLogs.SingleAsync();
        Assert.Equal(("PlatformSettlement", created.Settlement.Id.ToString(), "PlatformSettlementClosed", "cajero"),
            (entry.EntityName, entry.EntityId, entry.Action, entry.PerformedBy));
        Assert.Contains("\"DepositedAmount\":540", entry.Metadata);
        Assert.Contains("2x1 del jueves", entry.Metadata);

        // A closed settlement no longer changes.
        var edit = await UpdateAsync(fixture, Update(created.Settlement.Id, closed.Value.Settlement.Revision, 540m, [order.Id]));
        Assert.Equal(PlatformSettlement.ClosedMessage, edit.Error);
    }

    [Fact]
    public async Task Close_AfterARefundSinceTheLastSave_ShouldSaveTheNewFigures_ForThePersonToReview()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        var order = await SellAtAsync(fixture, At(Monday, 12), Rappi(shop, "R1"));
        var created = (await CreateAsync(fixture, Command(OrderChannel.Rappi, PlatformNet, [order.Id]))).Value;
        // The platform cancelled half of it after the settlement was saved.
        await RefundAsync(fixture, order.Id, 500m);

        var refused = await CloseAsync(fixture, created.Settlement.Id, 1);

        Assert.Equal(PlatformSettlementErrors.ExpectedNetsChanged, refused.Error);
        Assert.True(PlatformSettlementErrors.IsConflict(refused.Error));
        var detail = await DetailAsync(fixture, created.Settlement.Id);
        Assert.Equal((2, PlatformSettlementStatus.Open, 280.74m), (detail.Settlement.Revision, detail.Settlement.Status, detail.Settlement.OrdersTotal));
        Assert.Equal((280.74m, 280.74m), (detail.Orders[0].ExpectedNet, detail.Orders[0].CurrentExpectedNet));
        Assert.Empty(await fixture.Context.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Detail_ShouldTellAnOrderThatChangedSinceTheLastSave()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        var order = await SellAtAsync(fixture, At(Monday, 12), Rappi(shop, "R1"));
        var created = (await CreateAsync(fixture, Command(OrderChannel.Rappi, PlatformNet, [order.Id]))).Value;
        await RefundAsync(fixture, order.Id, 1000m);

        var detail = await DetailAsync(fixture, created.Settlement.Id);

        Assert.Equal((PlatformNet, 0m), (detail.Orders[0].ExpectedNet, detail.Orders[0].CurrentExpectedNet));
        Assert.Equal(1000m, detail.Orders[0].RefundedAmount);
    }

    [Fact]
    public async Task Void_ShouldNeedAReason_FreeItsOrders_AndRecordIt()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        var order = await SellAtAsync(fixture, At(Monday, 12), Rappi(shop, "R1"));
        var created = (await CreateAsync(fixture, Command(OrderChannel.Rappi, PlatformNet, [order.Id]))).Value;
        var closed = (await CloseAsync(fixture, created.Settlement.Id, 1)).Value;

        var stale = await VoidAsync(fixture, created.Settlement.Id, 1, "Duplicada");
        Assert.Equal(PlatformSettlementErrors.Stale, stale.Error);
        var voided = await VoidAsync(fixture, created.Settlement.Id, closed.Settlement.Revision, "  Se capturó con el monto de Uber Eats ");

        Assert.True(voided.IsSuccess, voided.Error);
        Assert.Equal((PlatformSettlementStatus.Voided, "Se capturó con el monto de Uber Eats"),
            (voided.Value.Settlement.Status, voided.Value.Settlement.VoidReason));
        Assert.Equal([order.Id], voided.Value.Orders.Select(o => o.OrderId));
        Assert.Equal([order.Id], (await PendingAsync(fixture, OrderChannel.Rappi, Monday, Sunday)).Orders.Select(o => o.OrderId));
        var entry = await fixture.Context.AuditLogs.OrderByDescending(log => log.Timestamp).FirstAsync(log => log.Action == "PlatformSettlementVoided");
        using var metadata = System.Text.Json.JsonDocument.Parse(entry.Metadata!);
        Assert.Equal("Se capturó con el monto de Uber Eats", metadata.RootElement.GetProperty("Reason").GetString());
        Assert.Equal((int)PlatformSettlementStatus.Closed, metadata.RootElement.GetProperty("PreviousStatus").GetInt32());
    }

    [Fact]
    public async Task Receivables_ShouldSayWhatEachPlatformOwes_AndUntilWhenItSettled()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        var settled = await SellAtAsync(fixture, At(Monday, 12), Rappi(shop, "R1"));
        await SellAtAsync(fixture, At(Monday.AddDays(8), 12), Rappi(shop, "R2"));
        await SellAtAsync(fixture, At(Monday.AddDays(9), 12), Rappi(shop, "R3"));
        var cash = await SellAtAsync(fixture, At(Monday.AddDays(9), 13), Rappi(shop, "R4", PlatformCollectedBy.Business));
        await PayInCashAsync(fixture, shop.Tenant, cash.Id);
        var closing = (await CreateAsync(fixture, Command(OrderChannel.Rappi, PlatformNet, [settled.Id]))).Value;
        await CloseAsync(fixture, closing.Settlement.Id, 1);
        var open = (await CreateAsync(fixture, Command(OrderChannel.UberEats, 0m, [], [new(PlatformSettlementAdjustmentKind.Fee, null, -10m)]) with { DepositedAmount = -10m })).Value;

        var receivables = await ReceivablesAsync(fixture);

        // DiDi Food is off and owes nothing: it is not listed.
        Assert.Equal([OrderChannel.Rappi, OrderChannel.UberEats], receivables.Select(r => r.Channel));
        var rappi = receivables[0];
        Assert.Equal((3, 2 * PlatformNet + BusinessNet, Monday.AddDays(8), (Guid?)null, (DateOnly?)Sunday),
            (rappi.PendingOrders, rappi.PendingAmount, rappi.OldestPendingOn, rappi.OpenSettlementId, rappi.SettledThrough));
        var uber = receivables[1];
        Assert.Equal((0, 0m, (DateOnly?)null, (Guid?)open.Settlement.Id, (DateOnly?)null),
            (uber.PendingOrders, uber.PendingAmount, uber.OldestPendingOn, uber.OpenSettlementId, uber.SettledThrough));
    }

    [Fact]
    public async Task List_ShouldBeTheNewestPeriodFirst_ByPlatformAndStatus()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        var weekOne = await SellAtAsync(fixture, At(Monday.AddDays(-7), 12), Rappi(shop, "R1"));
        var older = (await CreateAsync(fixture, Command(OrderChannel.Rappi, PlatformNet, [weekOne.Id]) with
        {
            PeriodFrom = Monday.AddDays(-7),
            PeriodTo = Sunday.AddDays(-7),
            DepositedOn = Sunday.AddDays(-5)
        })).Value;
        await CloseAsync(fixture, older.Settlement.Id, 1);
        var newer = (await CreateAsync(fixture, Command(OrderChannel.Rappi, 0m, [], [new(PlatformSettlementAdjustmentKind.Fee, null, -5m)]) with { DepositedAmount = -5m })).Value;
        var uber = (await CreateAsync(fixture, Command(OrderChannel.UberEats, 0m, []) with { PeriodTo = Sunday.AddDays(-1) })).Value;

        var all = await ListAsync(fixture, new GetPlatformSettlementsQuery());
        var rappi = await ListAsync(fixture, new GetPlatformSettlementsQuery { Channel = OrderChannel.Rappi });
        var closed = await ListAsync(fixture, new GetPlatformSettlementsQuery { Status = PlatformSettlementStatus.Closed });

        Assert.Equal([newer.Settlement.Id, uber.Settlement.Id, older.Settlement.Id], all.Items.Select(s => s.Id));
        Assert.Equal([newer.Settlement.Id, older.Settlement.Id], rappi.Items.Select(s => s.Id));
        var onlyClosed = Assert.Single(closed.Items);
        Assert.Equal((older.Settlement.Id, 1, PlatformNet, 0m), (onlyClosed.Id, onlyClosed.OrderCount, onlyClosed.OrdersTotal, onlyClosed.Difference));
        Assert.Equal((-5m, 0m), (all.Items[0].ExpectedDeposit, all.Items[0].Difference));
    }

    [Fact]
    public async Task Detail_OfAnotherBusiness_ShouldNotBeFound()
    {
        await using var fixture = SalesTestFixture.Create();
        await ShopAsync(fixture);
        var created = (await CreateAsync(fixture, Command(OrderChannel.Rappi, 0m, [], [new(PlatformSettlementAdjustmentKind.Fee, null, -5m)]) with { DepositedAmount = -5m })).Value;
        var other = await ShopAsync(fixture, "otro");
        fixture.TenantService.SetTenant(other.Tenant.Id);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => DetailAsync(fixture, created.Settlement.Id));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => CloseAsync(fixture, created.Settlement.Id, 1));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => VoidAsync(fixture, created.Settlement.Id, 1, "No es mía"));
        Assert.Empty((await ListAsync(fixture, new GetPlatformSettlementsQuery())).Items);
    }

    [Fact]
    public void Validators_ShouldRefuseWhatNeedsNoReading()
    {
        var create = new CreatePlatformSettlementCommandValidator();
        Assert.False(create.Validate(Command(OrderChannel.Counter, 0m, [])).IsValid);
        var repeated = Guid.NewGuid();
        Assert.Contains(create.Validate(Command(OrderChannel.Rappi, 0m, [repeated, repeated])).Errors,
            error => error.ErrorMessage == PlatformSettlement.RepeatedOrderMessage);
        Assert.Contains(create.Validate(Command(OrderChannel.Rappi, 0m, Enumerable.Range(0, 2_001).Select(_ => Guid.NewGuid()).ToList())).Errors,
            error => error.ErrorMessage == PlatformSettlement.TooManyOrdersMessage);
        Assert.Contains(create.Validate(Command(OrderChannel.Rappi, 0m, [Guid.Empty])).Errors,
            error => error.ErrorMessage == PlatformSettlementErrors.OrderNotOfThePlatform);
        Assert.Contains(create.Validate(Command(OrderChannel.Rappi, 0m, [], [null!])).Errors,
            error => error.ErrorMessage == PlatformSettlementErrors.EmptyAdjustment);
        Assert.True(create.Validate(Command(OrderChannel.DidiFood, 0m, [Guid.NewGuid()])).IsValid);

        var close = new ClosePlatformSettlementCommandValidator();
        Assert.False(close.Validate(new ClosePlatformSettlementCommand(Guid.NewGuid(), 0)).IsValid);
        var cancel = new VoidPlatformSettlementCommandValidator();
        Assert.Contains(cancel.Validate(new VoidPlatformSettlementCommand(Guid.NewGuid(), 1, "  ")).Errors,
            error => error.ErrorMessage == PlatformSettlement.VoidReasonRequiredMessage);
        Assert.Contains(cancel.Validate(new VoidPlatformSettlementCommand(Guid.NewGuid(), 1, new string('m', 501))).Errors,
            error => error.ErrorMessage == PlatformSettlement.VoidReasonTooLongMessage);

        var pending = new GetPendingPlatformOrdersQueryValidator();
        Assert.False(pending.Validate(new GetPendingPlatformOrdersQuery { Channel = OrderChannel.Rappi, From = Monday }).IsValid);
        Assert.Contains(pending.Validate(new GetPendingPlatformOrdersQuery { Channel = OrderChannel.Rappi, From = Sunday, To = Monday }).Errors,
            error => error.ErrorMessage == PlatformSettlement.PeriodOrderMessage);
        Assert.Contains(pending.Validate(new GetPendingPlatformOrdersQuery { Channel = OrderChannel.Rappi, From = Monday.AddDays(-31), To = Sunday }).Errors,
            error => error.ErrorMessage == PlatformSettlement.PeriodTooLongMessage);
        Assert.True(pending.Validate(new GetPendingPlatformOrdersQuery { Channel = OrderChannel.UberEats, From = Monday, To = Sunday }).IsValid);
    }

    private sealed record Shop(Tenant Tenant, Product Pack);

    private static async Task<Shop> ShopAsync(SalesTestFixture fixture, string slug = "demo")
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true, slug: slug);
        var pack = await fixture.AddProductAsync(tenant, "Paquete familiar", 1000m);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.UberEats);
        return new Shop(tenant, pack);
    }

    private static CreateOrderCommand Rappi(Shop shop, string folio, PlatformCollectedBy? collectedBy = null)
        => SalesTestFixture.PlatformOrder(OrderChannel.Rappi, folio, shop.Pack.Id, collectedBy: collectedBy);

    private static CreateOrderCommand Uber(Shop shop, string folio)
        => SalesTestFixture.PlatformOrder(OrderChannel.UberEats, folio, shop.Pack.Id);

    /// <summary>A local time of Mexico City.</summary>
    private static DateTimeOffset At(DateOnly date, int hour, int minute = 0)
        => new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(hour, minute)), Mexico), TimeSpan.Zero);

    private static async Task<OrderDto> SellAtAsync(SalesTestFixture fixture, DateTimeOffset at, CreateOrderCommand command)
    {
        fixture.Clock.UtcNow = at;
        var sale = await fixture.CreateOrderHandler().Handle(command, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        Assert.True(sale.IsSuccess, sale.Error);
        return sale.Value;
    }

    /// <summary>What the platform gave back of an order, as Anular registers it.</summary>
    private static async Task RefundAsync(SalesTestFixture fixture, Guid orderId, decimal amount)
    {
        var order = await fixture.Context.Orders.Include(o => o.Payments).Include(o => o.Refunds).SingleAsync(o => o.Id == orderId);
        order.RegisterRefund(amount, OrderPaymentMethod.Platform, "La plataforma lo canceló", At(Today, 9), "duena", null, null, false, []);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }

    /// <summary>The cash a DiDi or Rappi courier handed over for an order the business charged.</summary>
    private static async Task PayInCashAsync(SalesTestFixture fixture, Tenant tenant, Guid orderId)
    {
        if (!await fixture.Context.CashSessions.AnyAsync())
            await fixture.OpenCashSessionAsync(tenant);
        var payment = await fixture.AddPaymentHandler().Handle(
            new AddOrderPaymentCommand { OrderId = orderId, Amount = 1000m, Method = OrderPaymentMethod.Cash }, CancellationToken.None);
        Assert.True(payment.IsSuccess, payment.Error);
        fixture.Context.ChangeTracker.Clear();
    }

    private static CreatePlatformSettlementCommand Command(
        OrderChannel channel, decimal depositedAmount, IReadOnlyList<Guid> orderIds, IReadOnlyList<PlatformSettlementAdjustmentInput>? adjustments = null)
        => new(channel, Monday, Sunday, Sunday.AddDays(2), depositedAmount, "SPEI 4471", null, orderIds, adjustments ?? []);

    private static UpdatePlatformSettlementCommand Update(
        Guid id, int revision, decimal depositedAmount, IReadOnlyList<Guid> orderIds, IReadOnlyList<PlatformSettlementAdjustmentInput>? adjustments = null)
        => new(id, revision, Monday, Sunday, Sunday.AddDays(2), depositedAmount, "SPEI 4471", null, orderIds, adjustments ?? []);

    private static async Task<T> AtTodayAsync<T>(SalesTestFixture fixture, Func<Task<T>> action)
    {
        fixture.Clock.UtcNow = At(Today, 12);
        var result = await action();
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static Task<FoodParkDigital.Application.Common.Models.Result<PlatformSettlementDetailDto>> CreateAsync(
        SalesTestFixture fixture, CreatePlatformSettlementCommand command)
        => AtTodayAsync(fixture, () => new CreatePlatformSettlementCommandHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(command, CancellationToken.None));

    private static Task<FoodParkDigital.Application.Common.Models.Result<PlatformSettlementDetailDto>> UpdateAsync(
        SalesTestFixture fixture, UpdatePlatformSettlementCommand command)
        => AtTodayAsync(fixture, () => new UpdatePlatformSettlementCommandHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(command, CancellationToken.None));

    private static Task<FoodParkDigital.Application.Common.Models.Result<PlatformSettlementDetailDto>> CloseAsync(
        SalesTestFixture fixture, Guid id, int revision)
        => AtTodayAsync(fixture, () => new ClosePlatformSettlementCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock)
            .Handle(new ClosePlatformSettlementCommand(id, revision), CancellationToken.None));

    private static Task<FoodParkDigital.Application.Common.Models.Result<PlatformSettlementDetailDto>> VoidAsync(
        SalesTestFixture fixture, Guid id, int revision, string reason)
        => AtTodayAsync(fixture, () => new VoidPlatformSettlementCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock)
            .Handle(new VoidPlatformSettlementCommand(id, revision, reason), CancellationToken.None));

    private static Task<PlatformSettlementDetailDto> DetailAsync(SalesTestFixture fixture, Guid id)
        => AtTodayAsync(fixture, () => new GetPlatformSettlementQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetPlatformSettlementQuery(id), CancellationToken.None));

    private static Task<PendingPlatformOrdersDto> PendingAsync(SalesTestFixture fixture, OrderChannel channel, DateOnly from, DateOnly to)
        => AtTodayAsync(fixture, () => new GetPendingPlatformOrdersQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetPendingPlatformOrdersQuery { Channel = channel, From = from, To = to }, CancellationToken.None));

    private static Task<List<PlatformReceivableDto>> ReceivablesAsync(SalesTestFixture fixture)
        => AtTodayAsync(fixture, () => new GetPlatformReceivablesQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetPlatformReceivablesQuery(), CancellationToken.None));

    private static Task<FoodParkDigital.Application.Common.Models.PaginatedList<PlatformSettlementDto>> ListAsync(
        SalesTestFixture fixture, GetPlatformSettlementsQuery query)
        => AtTodayAsync(fixture, () => new GetPlatformSettlementsQueryHandler(fixture.Context).Handle(query, CancellationToken.None));
}
