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

    private static CreateOrderCommand Sale(Guid productId, Guid key) => new()
    {
        IdempotencyKey = key,
        Items = [new CreateOrderItemDto { ProductId = productId, Quantity = 1 }],
        Payments = [new CreateOrderPaymentDto { Amount = 23.20m, Method = OrderPaymentMethod.Cash }]
    };
}
