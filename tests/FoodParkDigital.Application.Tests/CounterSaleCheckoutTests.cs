using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>Selling and charging in one step: totals by IVA policy, payments, cash and closing.</summary>
public class CounterSaleCheckoutTests
{
    [Fact]
    public async Task Sale_WithPricesIncludingTax_ShouldChargeTheListedPrice()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 29m);

        var result = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand { Items = [new CreateOrderItemDto { ProductId = soda.Id, Quantity = 2 }] },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value.PricesIncludeTax);
        Assert.Equal(58m, result.Value.Total);
        Assert.Equal(8m, result.Value.Tax);
        Assert.Equal(50m, result.Value.SubTotal);
        var line = Assert.Single(result.Value.TaxBreakdown);
        Assert.Equal(16m, line.RatePercent);
        Assert.Equal(8m, line.TaxAmount);
        Assert.All(result.Value.Items, item => Assert.Equal(16m, item.TaxRate));
    }

    [Fact]
    public async Task Sale_ShouldApplyTheRateOfEachProduct()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: false);
        var tortilla = await fixture.AddProductAsync(tenant, "Tortilla 1 kg", 25m, taxRate: 0m);
        var soap = await fixture.AddProductAsync(tenant, "Jabón", 20m);

        var result = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand
            {
                Items =
                [
                    new CreateOrderItemDto { ProductId = tortilla.Id, Quantity = 2 },
                    new CreateOrderItemDto { ProductId = soap.Id, Quantity = 1 }
                ]
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(70m, result.Value.SubTotal);
        Assert.Equal(3.20m, result.Value.Tax);
        Assert.Equal(73.20m, result.Value.Total);
        Assert.Equal(2, result.Value.TaxBreakdown.Count);
    }

    [Fact]
    public async Task Sale_WhenTheTotalChangedSinceItWasShown_ShouldBeRejectedWithoutSaving()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 29m);
        await fixture.OpenCashSessionAsync(tenant);

        var result = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand
            {
                Items = [new CreateOrderItemDto { ProductId = soda.Id, Quantity = 1 }],
                ExpectedTotal = 25m,
                Payments = [new CreateOrderPaymentDto { Amount = 25m, Method = OrderPaymentMethod.Cash }]
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("$29.00", result.Error);
        Assert.Empty(await fixture.Context.Orders.ToListAsync());
        Assert.Empty(await fixture.Context.CashTransactions.ToListAsync());
    }

    [Fact]
    public async Task Sale_PaidInFullWithMixedPayments_ShouldBeDeliveredAndEnterTheCashSession()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 29m);
        var session = await fixture.OpenCashSessionAsync(tenant);

        var result = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand
            {
                Items = [new CreateOrderItemDto { ProductId = soda.Id, Quantity = 2, Notes = "  sin hielo  " }],
                ExpectedTotal = 58m,
                Payments =
                [
                    new CreateOrderPaymentDto { Amount = 20m, Method = OrderPaymentMethod.Card, Reference = "4321" },
                    new CreateOrderPaymentDto { Amount = 38m, Method = OrderPaymentMethod.Cash }
                ]
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(OrderStatus.Delivered, result.Value.Status);
        Assert.Equal(58m, result.Value.PaidAmount);
        Assert.Equal(0m, result.Value.Balance);
        Assert.Equal(2, result.Value.Payments.Count);
        Assert.Equal("sin hielo", Assert.Single(result.Value.Items).Notes);

        var transactions = await fixture.Context.CashTransactions
            .Where(t => t.CashSessionId == session.Id)
            .ToListAsync();
        Assert.Equal(2, transactions.Count);
        Assert.All(transactions, t => Assert.Equal(CashTransactionType.SalePayment, t.TransactionType));
        Assert.All(transactions, t => Assert.Equal(result.Value.Id, t.OrderId));
        Assert.Equal(58m, transactions.Sum(t => t.Amount));
    }

    [Fact]
    public async Task Sale_WithPaymentsAboveTheTotal_ShouldBeRejected()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 29m);

        var result = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand
            {
                Items = [new CreateOrderItemDto { ProductId = soda.Id, Quantity = 1 }],
                Payments =
                [
                    new CreateOrderPaymentDto { Amount = 20m, Method = OrderPaymentMethod.Card },
                    new CreateOrderPaymentDto { Amount = 20m, Method = OrderPaymentMethod.Cash }
                ]
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(await fixture.Context.Orders.ToListAsync());
    }

    [Fact]
    public async Task Sale_PartiallyPaid_ShouldStayOpenWithItsBalance()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 29m);

        var result = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand
            {
                Items = [new CreateOrderItemDto { ProductId = soda.Id, Quantity = 2 }],
                Payments = [new CreateOrderPaymentDto { Amount = 20m, Method = OrderPaymentMethod.Transfer }]
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(OrderStatus.Confirmed, result.Value.Status);
        Assert.Equal(38m, result.Value.Balance);
    }

    [Fact]
    public async Task Sale_WhenTheBusinessDoesNotCloseSalesByItself_ShouldStayConfirmed()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(
            BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true, autoCloseCounterSales: false);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 29m);

        var result = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand
            {
                Items = [new CreateOrderItemDto { ProductId = soda.Id, Quantity = 1 }],
                Payments = [new CreateOrderPaymentDto { Amount = 29m, Method = OrderPaymentMethod.Cash }]
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(OrderStatus.Confirmed, result.Value.Status);
        Assert.Equal(0m, result.Value.Balance);
    }

    [Fact]
    public async Task FoodSale_WithKitchen_ShouldWaitForTheKitchenEvenWhenPaid()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(
            BusinessType.Food, TenantCapability.Food | TenantCapability.Kitchen, pricesIncludeTax: true);
        var taco = await fixture.AddProductAsync(tenant, "Taco de pastor", 22m);

        var result = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand
            {
                Items = [new CreateOrderItemDto { ProductId = taco.Id, Quantity = 3 }],
                Payments = [new CreateOrderPaymentDto { Amount = 66m, Method = OrderPaymentMethod.Cash }]
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(OrderStatus.Confirmed, result.Value.Status);

        // The kitchen marks it ready: the paid sale is handed over and closed without a second message.
        var ready = await new UpdateOrderStatusCommandHandler(fixture.Context, fixture.CurrentUser).Handle(
            new UpdateOrderStatusCommand { OrderId = result.Value.Id, NewStatus = OrderStatus.Ready },
            CancellationToken.None);

        Assert.True(ready.IsSuccess, ready.Error);
        var order = await fixture.Context.Orders.Include(o => o.StatusHistory).SingleAsync(o => o.Id == result.Value.Id);
        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Contains(order.StatusHistory, h => h.NewStatus == OrderStatus.Ready);
        Assert.Contains(order.StatusHistory, h => h.PreviousStatus == OrderStatus.Ready && h.NewStatus == OrderStatus.Delivered);
    }

    [Fact]
    public async Task FoodSale_WithKitchen_ReadyButUnpaid_ShouldWaitForThePayment()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(
            BusinessType.Food, TenantCapability.Food | TenantCapability.Kitchen, pricesIncludeTax: true);
        var taco = await fixture.AddProductAsync(tenant, "Taco de pastor", 22m);
        var created = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand { Items = [new CreateOrderItemDto { ProductId = taco.Id, Quantity = 2 }] },
            CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error);

        var ready = await new UpdateOrderStatusCommandHandler(fixture.Context, fixture.CurrentUser).Handle(
            new UpdateOrderStatusCommand { OrderId = created.Value.Id, NewStatus = OrderStatus.Ready },
            CancellationToken.None);
        Assert.True(ready.IsSuccess, ready.Error);
        Assert.Equal(OrderStatus.Ready, (await fixture.Context.Orders.SingleAsync(o => o.Id == created.Value.Id)).Status);

        // The payment taken at pickup closes it.
        var paid = await fixture.AddPaymentHandler().Handle(
            new AddOrderPaymentCommand { OrderId = created.Value.Id, Amount = 44m, Method = OrderPaymentMethod.Cash },
            CancellationToken.None);
        Assert.True(paid.IsSuccess, paid.Error);
        Assert.Equal(OrderStatus.Delivered, paid.Value.Status);
    }

    [Fact]
    public async Task FoodSale_WithoutKitchen_ShouldCloseWhenPaid()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true);
        var coffee = await fixture.AddProductAsync(tenant, "Americano", 35m);

        var result = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand
            {
                Items = [new CreateOrderItemDto { ProductId = coffee.Id, Quantity = 1 }],
                Payments = [new CreateOrderPaymentDto { Amount = 35m, Method = OrderPaymentMethod.Cash }]
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(OrderStatus.Delivered, result.Value.Status);
    }

    [Fact]
    public async Task ServiceSale_PaidAtTheCounter_ShouldCloseUnlessItsPiecesAreInTheWorkshop()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service, pricesIncludeTax: true);
        var category = new Category(tenant.Id, "Servicios", null);
        var haircut = new Product(tenant.Id, category.Id, "Corte", null, 150m, "MXN", unit: UnitType.Service);
        var laundry = new Product(tenant.Id, category.Id, "Lavado", null, 90m, "MXN", unit: UnitType.Service, isTrackable: true);
        fixture.Context.Categories.Add(category);
        fixture.Context.Products.AddRange(haircut, laundry);
        await fixture.Context.SaveChangesAsync();

        var haircutSale = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand
            {
                OrderType = OrderType.Service,
                Items = [new CreateOrderItemDto { ProductId = haircut.Id, Quantity = 1 }],
                Payments = [new CreateOrderPaymentDto { Amount = 150m, Method = OrderPaymentMethod.Cash }]
            },
            CancellationToken.None);
        var laundrySale = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand
            {
                OrderType = OrderType.Service,
                Items = [new CreateOrderItemDto { ProductId = laundry.Id, Quantity = 1 }],
                Payments = [new CreateOrderPaymentDto { Amount = 90m, Method = OrderPaymentMethod.Cash }]
            },
            CancellationToken.None);

        Assert.True(haircutSale.IsSuccess, haircutSale.Error);
        Assert.Equal(OrderStatus.Completed, haircutSale.Value.Status);
        Assert.True(laundrySale.IsSuccess, laundrySale.Error);
        Assert.Equal(OrderStatus.Accepted, laundrySale.Value.Status);
        Assert.Single(laundrySale.Value.ServicePieces);
    }

    [Fact]
    public async Task AddPayment_ThatSettlesTheSale_ShouldCloseIt()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 29m);
        var created = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand { Items = [new CreateOrderItemDto { ProductId = soda.Id, Quantity = 2 }] },
            CancellationToken.None);
        Assert.Equal(OrderStatus.Confirmed, created.Value.Status);

        var partial = await fixture.AddPaymentHandler().Handle(
            new AddOrderPaymentCommand { OrderId = created.Value.Id, Amount = 30m, Method = OrderPaymentMethod.Cash },
            CancellationToken.None);
        var settled = await fixture.AddPaymentHandler().Handle(
            new AddOrderPaymentCommand { OrderId = created.Value.Id, Amount = 28m, Method = OrderPaymentMethod.Card },
            CancellationToken.None);

        Assert.Equal(OrderStatus.Confirmed, partial.Value.Status);
        Assert.True(settled.IsSuccess, settled.Error);
        Assert.Equal(OrderStatus.Delivered, settled.Value.Status);
    }

    [Fact]
    public async Task AddPayment_AboveTheBalance_ShouldBeRejected()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 29m);
        var created = await fixture.CreateOrderHandler().Handle(
            new CreateOrderCommand { Items = [new CreateOrderItemDto { ProductId = soda.Id, Quantity = 1 }] },
            CancellationToken.None);

        var result = await fixture.AddPaymentHandler().Handle(
            new AddOrderPaymentCommand { OrderId = created.Value.Id, Amount = 30m, Method = OrderPaymentMethod.Cash },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(await fixture.Context.OrderPayments.ToListAsync());
    }

    [Fact]
    public void Validator_ShouldRejectPaymentsOnOrdersPlacedByCustomers()
    {
        var validator = new CreateOrderCommandValidator();
        var item = new CreateOrderItemDto { ProductId = Guid.NewGuid(), Quantity = 1 };
        var payment = new CreateOrderPaymentDto { Amount = 10m, Method = OrderPaymentMethod.Cash };

        var customerOrder = validator.Validate(new CreateOrderCommand
        {
            CreatedByBusiness = false,
            CustomerPhone = "5511112222",
            Items = [item],
            Payments = [payment]
        });
        var tooManyPayments = validator.Validate(new CreateOrderCommand
        {
            Items = [item],
            Payments = Enumerable.Repeat(payment, 11).ToList()
        });
        var tooPrecise = validator.Validate(new CreateOrderCommand
        {
            Items = [item],
            Payments = [new CreateOrderPaymentDto { Amount = 10.005m, Method = OrderPaymentMethod.Cash }]
        });
        var counterSale = validator.Validate(new CreateOrderCommand { Items = [item], Payments = [payment] });

        Assert.False(customerOrder.IsValid);
        Assert.False(tooManyPayments.IsValid);
        Assert.False(tooPrecise.IsValid);
        Assert.True(counterSale.IsValid);
    }
}
