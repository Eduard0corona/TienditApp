using FoodParkDigital.Application.Finance.Queries;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Tips and the cash handed over are stored with each payment: the sale and its balance never include the tip, the
/// drawer gets it as its own movement and the daily cut reports it apart.
/// </summary>
public class PaymentTipTests
{
    [Fact]
    public async Task CashSale_WithTip_ShouldStoreTheChange_AndPutTheTipInTheDrawerApart()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true);
        var meal = await fixture.AddProductAsync(tenant, "Comida corrida", 29m);
        var session = await fixture.OpenCashSessionAsync(tenant);

        var result = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand
            {
                Items = [new CreateOrderItemDto { ProductId = meal.Id, Quantity = 2 }],
                ExpectedTotal = 58m,
                Payments = [new CreateOrderPaymentDto { Amount = 58m, Method = OrderPaymentMethod.Cash, Tip = 8.70m, CashReceived = 100m }]
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(58m, result.Value.Total);
        Assert.Equal(0m, result.Value.Balance);
        Assert.Equal(8.70m, result.Value.TipTotal);
        Assert.Equal(OrderStatus.Delivered, result.Value.Status);
        var payment = Assert.Single(result.Value.Payments);
        Assert.Equal(8.70m, payment.Tip);
        Assert.Equal(100m, payment.CashReceived);
        Assert.Equal(33.30m, payment.Change);

        var drawer = await fixture.Context.CashSessions.Include(s => s.Transactions).SingleAsync(s => s.Id == session.Id);
        Assert.Equal(
            [(CashTransactionType.SalePayment, 58m), (CashTransactionType.Tip, 8.70m)],
            drawer.Transactions.OrderBy(t => t.TransactionType).Select(t => (t.TransactionType, t.Amount)));
        Assert.Equal(500m + 58m + 8.70m, drawer.ExpectedBalance);
    }

    [Fact]
    public async Task CardTip_ShouldBeRecorded_WithoutChangingTheExpectedCash()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true);
        var meal = await fixture.AddProductAsync(tenant, "Comida corrida", 29m);
        var session = await fixture.OpenCashSessionAsync(tenant);
        var created = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand { Items = [new CreateOrderItemDto { ProductId = meal.Id, Quantity = 2 }] },
            CancellationToken.None);

        var paid = await fixture.AddPaymentHandler().Handle(
            new AddOrderPaymentCommand { OrderId = created.Value.Id, Amount = 58m, Method = OrderPaymentMethod.Card, Tip = 5m },
            CancellationToken.None);

        Assert.True(paid.IsSuccess, paid.Error);
        Assert.Equal(0m, paid.Value.Balance);
        Assert.Equal(5m, paid.Value.TipTotal);
        var drawer = await fixture.Context.CashSessions.Include(s => s.Transactions).SingleAsync(s => s.Id == session.Id);
        Assert.Equal(500m, drawer.ExpectedBalance);
        Assert.Equal(63m, drawer.NonCashInflows);
    }

    [Fact]
    public async Task Tip_LargerThanTheBill_ShouldBeRefused_WithoutStoringThePayment()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true);
        var meal = await fixture.AddProductAsync(tenant, "Comida corrida", 29m);
        await fixture.OpenCashSessionAsync(tenant);
        var created = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand { Items = [new CreateOrderItemDto { ProductId = meal.Id, Quantity = 2 }] },
            CancellationToken.None);

        // A mixed charge may end with a small payment and a larger tip; more tip than bill is a typo.
        var card = await fixture.AddPaymentHandler().Handle(
            new AddOrderPaymentCommand { OrderId = created.Value.Id, Amount = 50m, Method = OrderPaymentMethod.Card },
            CancellationToken.None);
        var typo = await fixture.AddPaymentHandler().Handle(
            new AddOrderPaymentCommand { OrderId = created.Value.Id, Amount = 8m, Method = OrderPaymentMethod.Cash, Tip = 870m },
            CancellationToken.None);
        var cashWithTip = await fixture.AddPaymentHandler().Handle(
            new AddOrderPaymentCommand { OrderId = created.Value.Id, Amount = 8m, Method = OrderPaymentMethod.Cash, Tip = 12m, CashReceived = 20m },
            CancellationToken.None);

        Assert.True(card.IsSuccess, card.Error);
        Assert.Equal(Order.TipAboveTotalMessage, typo.Error);
        Assert.True(cashWithTip.IsSuccess, cashWithTip.Error);
        Assert.Equal(12m, cashWithTip.Value.TipTotal);
        Assert.Equal(2, await fixture.Context.OrderPayments.CountAsync());
    }

    [Fact]
    public void CashReceived_ShouldComeOnlyWithCash_AndCoverThePaymentAndTheTip()
    {
        var validator = new AddOrderPaymentCommandValidator();
        var orderId = Guid.NewGuid();

        var onCard = validator.Validate(new AddOrderPaymentCommand { OrderId = orderId, Amount = 58m, Method = OrderPaymentMethod.Card, CashReceived = 100m });
        var short_ = validator.Validate(new AddOrderPaymentCommand { OrderId = orderId, Amount = 58m, Method = OrderPaymentMethod.Cash, Tip = 5m, CashReceived = 60m });
        var negativeTip = validator.Validate(new AddOrderPaymentCommand { OrderId = orderId, Amount = 58m, Method = OrderPaymentMethod.Cash, Tip = -1m });
        var exact = validator.Validate(new AddOrderPaymentCommand { OrderId = orderId, Amount = 58m, Method = OrderPaymentMethod.Cash, Tip = 5m, CashReceived = 63m });
        var sale = new CreateOrderCommandValidator().Validate(new CreateOrderCommand
        {
            Items = [new CreateOrderItemDto { ProductId = Guid.NewGuid(), Quantity = 1 }],
            Payments = [new CreateOrderPaymentDto { Amount = 58m, Method = OrderPaymentMethod.Transfer, CashReceived = 58m }]
        });

        Assert.Contains(onCard.Errors, e => e.ErrorMessage == OrderPayment.CashReceivedOnlyForCashMessage);
        Assert.Contains(short_.Errors, e => e.ErrorMessage == OrderPayment.CashReceivedShortMessage);
        Assert.False(negativeTip.IsValid);
        Assert.True(exact.IsValid, string.Join(" ", exact.Errors));
        Assert.Contains(sale.Errors, e => e.ErrorMessage == OrderPayment.CashReceivedOnlyForCashMessage);
    }

    [Fact]
    public async Task DailyCut_ShouldReportTipsApartFromTheSales()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true);
        var meal = await fixture.AddProductAsync(tenant, "Comida corrida", 29m);
        await fixture.OpenCashSessionAsync(tenant);
        var cashSale = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand
            {
                Items = [new CreateOrderItemDto { ProductId = meal.Id, Quantity = 2 }],
                Payments = [new CreateOrderPaymentDto { Amount = 58m, Method = OrderPaymentMethod.Cash, Tip = 5m, CashReceived = 100m }]
            },
            CancellationToken.None);
        var cardSale = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand
            {
                Items = [new CreateOrderItemDto { ProductId = meal.Id, Quantity = 1 }],
                Payments = [new CreateOrderPaymentDto { Amount = 29m, Method = OrderPaymentMethod.Card, Tip = 3m }]
            },
            CancellationToken.None);
        Assert.True(cashSale.IsSuccess, cashSale.Error);
        Assert.True(cardSale.IsSuccess, cardSale.Error);

        var cut = await new GetDailyCashCutQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetDailyCashCutQuery(), CancellationToken.None);

        Assert.Equal(87m, cut.SalesPaymentsTotal);
        Assert.Equal(58m, cut.CashTotal);
        Assert.Equal(29m, cut.CardTotal);
        Assert.Equal(8m, cut.TipsTotal);
        Assert.Equal(5m, cut.CashTipsTotal);
        // Tips are not manual movements: they come with the sales.
        Assert.Equal(0m, cut.ManualIncomeTotal);
        Assert.Equal(63m, cut.NetCashOnlyFlow);
        Assert.Equal(95m, cut.NetCashFlow);
    }
}
