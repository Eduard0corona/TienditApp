using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>Sales sent again with the same key (retries and sales captured without connection).</summary>
public class OfflineSalesTests
{
    [Fact]
    public async Task SameKeyTwice_ShouldCreateOneSaleAndChargeItOnce()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        var drawer = await fixture.OpenCashSessionAsync(tenant);
        var key = Guid.NewGuid();

        var first = await fixture.CreateOrderHandler().Handle(Sale(soda.Id, key), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        var retry = await fixture.CreateOrderHandler().Handle(Sale(soda.Id, key), CancellationToken.None);

        Assert.True(first.IsSuccess, first.Error);
        Assert.True(retry.IsSuccess, retry.Error);
        Assert.Equal(first.Value.Id, retry.Value.Id);
        Assert.Equal(first.Value.OrderNumber, retry.Value.OrderNumber);
        Assert.Single(await fixture.Context.Orders.ToListAsync());
        Assert.Single(await fixture.Context.OrderPayments.ToListAsync());
        Assert.Single(await fixture.Context.CashTransactions.Where(t => t.CashSessionId == drawer.Id).ToListAsync());
    }

    [Fact]
    public async Task SameKeyWithThePaymentTypedAgain_ShouldReturnTheRecordedSaleAndItsPayment()
    {
        // Vender keeps the key with the cart: after a reload the cashier charges again and may type another amount.
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        await fixture.OpenCashSessionAsync(tenant);
        var key = Guid.NewGuid();

        var first = await fixture.CreateOrderHandler().Handle(Sale(soda.Id, key, cashReceived: 50m), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        var retry = await fixture.CreateOrderHandler().Handle(Sale(soda.Id, key, cashReceived: 100m), CancellationToken.None);

        Assert.True(first.IsSuccess, first.Error);
        Assert.True(retry.IsSuccess, retry.Error);
        Assert.Equal(first.Value.Id, retry.Value.Id);
        Assert.Equal(50m, Assert.Single(retry.Value.Payments).CashReceived);
        Assert.Equal(50m, Assert.Single(await fixture.Context.OrderPayments.ToListAsync()).CashReceived);
    }

    [Fact]
    public async Task SameKeyInAnotherBusiness_ShouldBeASaleOfItsOwn_WithoutAnsweringTheOtherOne()
    {
        await using var fixture = SalesTestFixture.Create();
        var shop = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "tienda");
        var soda = await fixture.AddProductAsync(shop, "Refresco", 20m);
        await fixture.OpenCashSessionAsync(shop);
        var key = Guid.NewGuid();
        var shopSale = await fixture.CreateOrderHandler().Handle(Sale(soda.Id, key), CancellationToken.None);

        var other = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "otra");
        var water = await fixture.AddProductAsync(other, "Agua", 20m);
        await fixture.OpenCashSessionAsync(other);
        fixture.Context.ChangeTracker.Clear();
        var otherSale = await fixture.CreateOrderHandler().Handle(Sale(water.Id, key), CancellationToken.None);

        Assert.True(shopSale.IsSuccess, shopSale.Error);
        Assert.True(otherSale.IsSuccess, otherSale.Error);
        Assert.NotEqual(shopSale.Value.Id, otherSale.Value.Id);
        Assert.Equal("Agua", Assert.Single(otherSale.Value.Items).ProductName);
        Assert.Equal(otherSale.Value.Id, (await fixture.Context.Orders.SingleAsync()).Id);
        fixture.TenantService.SetTenant(shop.Id);
        Assert.Equal(shopSale.Value.Id, (await fixture.Context.Orders.SingleAsync()).Id);
    }

    [Fact]
    public async Task DifferentKeys_ShouldBeDifferentSales()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        await fixture.OpenCashSessionAsync(tenant);

        await fixture.CreateOrderHandler().Handle(Sale(soda.Id, Guid.NewGuid()), CancellationToken.None);
        await fixture.CreateOrderHandler().Handle(Sale(soda.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(2, await fixture.Context.Orders.CountAsync());
    }

    [Fact]
    public async Task SaleCapturedWithoutConnection_ShouldKeepWhenItWasCaptured_AndThePaymentTheServerTime()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        await fixture.OpenCashSessionAsync(tenant);
        var capturedAt = SalesTestFixture.Now.AddHours(-3);

        var sale = await fixture.CreateOrderHandler().Handle(Sale(soda.Id, Guid.NewGuid()) with { CapturedOfflineAt = capturedAt }, CancellationToken.None);

        Assert.True(sale.IsSuccess, sale.Error);
        Assert.Equal(capturedAt, sale.Value.CapturedOfflineAt);
        Assert.Equal(SalesTestFixture.Now, (await fixture.Context.OrderPayments.SingleAsync()).PaidAt);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(-60 * 24 * 8)]
    public async Task CaptureTime_ShouldNotBeInTheFutureNorOlderThanAWeek(int minutesFromNow)
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);

        var sale = await fixture.CreateOrderHandler().Handle(
            Sale(soda.Id, Guid.NewGuid()) with { CapturedOfflineAt = SalesTestFixture.Now.AddMinutes(minutesFromNow) },
            CancellationToken.None);

        Assert.True(sale.IsFailure);
        Assert.Empty(await fixture.Context.Orders.ToListAsync());
    }

    [Fact]
    public void CaptureTime_ShouldComeWithTheSaleKey()
    {
        var result = new CreateOrderCommandValidator().Validate(new CreateOrderCommand
        {
            Items = [new CreateOrderItemDto { ProductId = Guid.NewGuid(), Quantity = 1 }],
            CapturedOfflineAt = SalesTestFixture.Now
        });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateOrderCommand.CapturedOfflineAt));
    }

    private static CreateOrderCommand Sale(Guid productId, Guid key, decimal? cashReceived = null) => new()
    {
        IdempotencyKey = key,
        Items = [new CreateOrderItemDto { ProductId = productId, Quantity = 1 }],
        Payments = [new CreateOrderPaymentDto { Amount = 23.20m, Method = OrderPaymentMethod.Cash, CashReceived = cashReceived }]
    };
}
