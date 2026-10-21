using FluentValidation;
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
/// Importar el estado de cuenta (Fase 22H, ADR 0040): each folio of a platform's statement against its orders, the lines of
/// a folio added up, the lines without one by concept, and what the settlement can take; nothing is saved.
/// </summary>
public class PlatformStatementMatchFlowTests
{
    private static readonly TimeZoneInfo Mexico = TenantDateTimeResolver.Resolve(null);
    private static readonly DateOnly Monday = new(2026, 10, 5);
    private static readonly DateOnly Sunday = new(2026, 10, 11);
    private static readonly DateOnly Today = new(2026, 10, 14);

    // $1,000 with IVA included, 30 % of commission on the total, charged by the platform.
    private const decimal PlatformNet = 561.48m;

    [Fact]
    public async Task Match_ShouldTellWhatEachFolioIs_AddingUpItsLines()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        var settledOrder = await SellAtAsync(fixture, At(Monday.AddDays(-7), 12), Rappi(shop, "R5"));
        var settled = (await CreateAsync(fixture, Command(Monday.AddDays(-7), Monday.AddDays(-1), PlatformNet, [settledOrder.Id]))).Value;
        Assert.True((await CloseAsync(fixture, settled.Settlement.Id, settled.Settlement.Revision)).IsSuccess);
        var inThis = await SellAtAsync(fixture, At(Monday, 10), Rappi(shop, "R6"));
        var pending = await SellAtAsync(fixture, At(Monday, 12), Rappi(shop, "R1"));
        var halfRefunded = await SellAtAsync(fixture, At(Monday.AddDays(1), 12), Rappi(shop, "R2"));
        var refunded = await SellAtAsync(fixture, At(Monday.AddDays(2), 12), Rappi(shop, "R3"));
        var after = await SellAtAsync(fixture, At(Sunday.AddDays(1), 12), Rappi(shop, "R4"));
        var twinA = await SellAtAsync(fixture, At(Monday.AddDays(1), 13), Rappi(shop, "R7"));
        var twinB = await SellAtAsync(fixture, At(Monday.AddDays(2), 13), Rappi(shop, "R7"));
        await SellAtAsync(fixture, At(Monday, 15), Uber(shop, "R8"));
        await RefundAsync(fixture, halfRefunded.Id, 500m);
        await RefundAsync(fixture, refunded.Id, 1000m);
        var open = (await CreateAsync(fixture, Command(Monday, Sunday, PlatformNet, [inThis.Id]))).Value;

        var match = await MatchAsync(fixture, OrderChannel.Rappi, open.Settlement.Id,
            Line(2, "#r1", 561.48m, "Pedido"),
            Line(3, "R6", 561.48m),
            Line(4, "R2", 250m),
            Line(5, "R1", -20m, "Promoción compartida"),
            Line(6, "R3", 0m),
            Line(7, "R4", 561.48m),
            Line(8, "R5", 561.48m),
            Line(9, "R7", 561.48m),
            Line(10, "R8", 561.48m),
            Line(11, "R/9", 100m, "Pedido"),
            Line(12, null, -50m, "Publicidad"),
            Line(13, "  ", -25m, " publicidad  "),
            Line(14, "-", 10m, null));

        Assert.Equal(
            [
                ("R1", new List<int> { 2, 5 }, 541.48m, PlatformStatementMatchStatus.Pending, (decimal?)PlatformNet, (decimal?)-20m),
                ("R6", [3], 561.48m, PlatformStatementMatchStatus.InThisSettlement, PlatformNet, 0m),
                ("R2", [4], 250m, PlatformStatementMatchStatus.Pending, 280.74m, -30.74m),
                ("R3", [6], 0m, PlatformStatementMatchStatus.NothingExpected, null, null),
                ("R4", [7], 561.48m, PlatformStatementMatchStatus.AfterPeriod, null, null),
                ("R5", [8], 561.48m, PlatformStatementMatchStatus.Settled, null, null),
                ("R7", [9], 561.48m, PlatformStatementMatchStatus.Ambiguous, null, null),
                ("R8", [10], 561.48m, PlatformStatementMatchStatus.Unknown, null, null)
            ],
            match.Folios.Select(f => (f.Folio, f.Rows, f.StatementAmount, f.Status, f.ExpectedNet, f.Difference)));
        Assert.Equal([pending.Id], match.Folios[0].Orders.Select(o => o.OrderId));
        Assert.Equal([refunded.Id], match.Folios[3].Orders.Select(o => o.OrderId));
        Assert.Equal([after.Id], match.Folios[4].Orders.Select(o => o.OrderId));
        Assert.Equal(settled.Settlement.Id, match.Folios[5].SettlementId);
        Assert.Equal([twinB.Id, twinA.Id], match.Folios[6].Orders.Select(o => o.OrderId));
        Assert.Empty(match.Folios[7].Orders);
        Assert.Equal(
            [("Publicidad", new List<int> { 12, 13 }, -75m), ("", [14], 10m)],
            match.Concepts.Select(c => (c.Concept, c.Rows, c.Amount)));
        Assert.Equal([(11, "R/9", "Pedido", 100m)], match.Unreadable.Select(u => (u.Row, u.Folio, u.Concept, u.Amount)));
        Assert.Equal(
            (3633.88m, 1352.96m, 1403.70m, -65m, 2345.92m),
            (match.StatementTotal, match.MatchedTotal, match.ExpectedTotal, match.ConceptsTotal, match.UnmatchedTotal));
        Assert.Equal(match.StatementTotal, match.MatchedTotal + match.ConceptsTotal + match.UnmatchedTotal);
        // Nothing was saved: the settlement still has its one order.
        Assert.Equal([inThis.Id], (await DetailAsync(fixture, open.Settlement.Id)).Orders.Select(o => o.OrderId));
    }

    [Fact]
    public async Task Match_ForANewSettlement_ShouldTakeTheOrdersOfNoSettlement()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        var order = await SellAtAsync(fixture, At(Monday, 12), Rappi(shop, "R1"));

        var match = await MatchAsync(fixture, OrderChannel.Rappi, null, Line(1, "R1", 561.48m));

        var folio = Assert.Single(match.Folios);
        Assert.Equal((PlatformStatementMatchStatus.Pending, order.Id, 0m), (folio.Status, folio.Orders.Single().OrderId, folio.Difference));
    }

    [Fact]
    public async Task Match_ShouldRefuseASettlementOfAnotherPlatform_ClosedOrOfAnotherBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await ShopAsync(fixture);
        var order = await SellAtAsync(fixture, At(Monday, 12), Rappi(shop, "R1"));
        var created = (await CreateAsync(fixture, Command(Monday, Sunday, PlatformNet, [order.Id]))).Value;

        var otherPlatform = await Assert.ThrowsAsync<ValidationException>(
            () => MatchAsync(fixture, OrderChannel.UberEats, created.Settlement.Id, Line(1, "R1", 1m)));
        Assert.Contains(otherPlatform.Errors, e => e.ErrorMessage == MatchPlatformStatementQueryHandler.AnotherPlatformMessage);

        Assert.True((await CloseAsync(fixture, created.Settlement.Id, created.Settlement.Revision)).IsSuccess);
        var closed = await Assert.ThrowsAsync<ValidationException>(
            () => MatchAsync(fixture, OrderChannel.Rappi, created.Settlement.Id, Line(1, "R1", 1m)));
        Assert.Contains(closed.Errors, e => e.ErrorMessage == MatchPlatformStatementQueryHandler.NotOpenMessage);

        var other = await ShopAsync(fixture, "otro");
        fixture.TenantService.SetTenant(other.Tenant.Id);
        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => MatchAsync(fixture, OrderChannel.Rappi, created.Settlement.Id, Line(1, "R1", 1m)));
        // The folio of another business is no order of this one.
        var mine = await MatchAsync(fixture, OrderChannel.Rappi, null, Line(1, "R1", 561.48m));
        Assert.Equal(PlatformStatementMatchStatus.Unknown, mine.Folios.Single().Status);
    }

    [Fact]
    public void Validator_ShouldRefuseWhatCannotBeCompared()
    {
        var validator = new MatchPlatformStatementQueryValidator();
        MatchPlatformStatementQuery Valid() => new()
        {
            Channel = OrderChannel.Rappi,
            PeriodFrom = Monday,
            PeriodTo = Sunday,
            Lines = [Line(1, "R1", 1m)]
        };

        Assert.True(validator.Validate(Valid()).IsValid);
        var refusals = new (MatchPlatformStatementQuery Query, string Message)[]
        {
            (Valid() with { Channel = OrderChannel.Counter }, DeliveryPlatformSetting.NotADeliveryPlatformMessage),
            (Valid() with { PeriodTo = null }, GetPendingPlatformOrdersQueryValidator.ChooseThePeriodMessage),
            (Valid() with { PeriodFrom = Sunday, PeriodTo = Monday }, PlatformSettlement.PeriodOrderMessage),
            (Valid() with { PeriodFrom = Monday.AddDays(-31) }, PlatformSettlement.PeriodTooLongMessage),
            (Valid() with { Lines = [] }, MatchPlatformStatementQueryValidator.NoLinesMessage),
            (Valid() with { Lines = Enumerable.Range(1, 5_001).Select(row => Line(row, "R1", 1m)).ToList() }, MatchPlatformStatementQueryValidator.TooManyLinesMessage),
            (Valid() with { Lines = [null!] }, MatchPlatformStatementQueryValidator.MissingLineMessage),
            (Valid() with { Lines = [Line(0, "R1", 1m)] }, "Cada renglón dice en qué línea del archivo venía."),
            (Valid() with { Lines = [Line(7, "R1", 1.005m)] }, "El importe de la línea 7 no es válido."),
            (Valid() with { Lines = [Line(7, "R1", 10_000_000.01m)] }, "El importe de la línea 7 no es válido."),
            (Valid() with { Lines = [Line(7, new string('9', 61), 1m)] }, "El folio de la línea 7 es demasiado largo."),
            (Valid() with { Lines = [Line(7, "R1", 1m, new string('c', 201))] }, "El concepto de la línea 7 es demasiado largo.")
        };
        foreach (var (query, message) in refusals)
            Assert.Contains(validator.Validate(query).Errors, error => error.ErrorMessage == message);
        Assert.True(validator.Validate(Valid() with { Lines = [Line(1, null, -10_000_000m, new string('c', 200))] }).IsValid);
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

    private static CreateOrderCommand Rappi(Shop shop, string folio)
        => SalesTestFixture.PlatformOrder(OrderChannel.Rappi, folio, shop.Pack.Id);

    private static CreateOrderCommand Uber(Shop shop, string folio)
        => SalesTestFixture.PlatformOrder(OrderChannel.UberEats, folio, shop.Pack.Id);

    private static PlatformStatementLineRequest Line(int row, string? folio, decimal amount, string? concept = null)
        => new() { Row = row, Folio = folio, Amount = amount, Concept = concept };

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

    private static CreatePlatformSettlementCommand Command(DateOnly from, DateOnly to, decimal depositedAmount, IReadOnlyList<Guid> orderIds)
        => new(OrderChannel.Rappi, from, to, to.AddDays(2), depositedAmount, "SPEI 4471", null, orderIds, []);

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

    private static Task<FoodParkDigital.Application.Common.Models.Result<PlatformSettlementDetailDto>> CloseAsync(
        SalesTestFixture fixture, Guid id, int revision)
        => AtTodayAsync(fixture, () => new ClosePlatformSettlementCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock)
            .Handle(new ClosePlatformSettlementCommand(id, revision), CancellationToken.None));

    private static Task<PlatformSettlementDetailDto> DetailAsync(SalesTestFixture fixture, Guid id)
        => AtTodayAsync(fixture, () => new GetPlatformSettlementQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetPlatformSettlementQuery(id), CancellationToken.None));

    private static Task<PlatformStatementMatchDto> MatchAsync(
        SalesTestFixture fixture, OrderChannel channel, Guid? settlementId, params PlatformStatementLineRequest[] lines)
        => AtTodayAsync(fixture, () => new MatchPlatformStatementQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new MatchPlatformStatementQuery
            {
                Channel = channel,
                SettlementId = settlementId,
                PeriodFrom = Monday,
                PeriodTo = Sunday,
                Lines = lines.ToList()
            }, CancellationToken.None));
}
