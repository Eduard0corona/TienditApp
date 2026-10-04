using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

public class OrderClientRequestTests
{
    [Fact]
    public void SaleCapturedWithoutConnection_ShouldKeepItsKeyAndCaptureTime()
    {
        var order = NewOrder();
        var key = Guid.NewGuid();
        var capturedAt = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.FromHours(-6));

        order.IdentifyClientRequest(key, capturedAt);

        Assert.Equal(key, order.ClientRequestId);
        Assert.Equal(capturedAt, order.CapturedOfflineAt);
    }

    [Fact]
    public void OnlineSale_ShouldKeepItsKeyWithoutCaptureTime()
    {
        var order = NewOrder();

        order.IdentifyClientRequest(Guid.NewGuid(), capturedOfflineAt: null);

        Assert.NotNull(order.ClientRequestId);
        Assert.Null(order.CapturedOfflineAt);
    }

    [Fact]
    public void Key_ShouldNotBeEmptyNorChangeOnceSet()
    {
        var order = NewOrder();
        var key = Guid.NewGuid();

        Assert.Throws<DomainException>(() => order.IdentifyClientRequest(Guid.Empty, null));
        order.IdentifyClientRequest(key, null);
        order.IdentifyClientRequest(key, null);
        Assert.Throws<DomainException>(() => order.IdentifyClientRequest(Guid.NewGuid(), null));
        Assert.Equal(key, order.ClientRequestId);
    }

    private static Order NewOrder() =>
        new(
            tenantId: Guid.NewGuid(),
            customerUserId: null,
            temporaryCustomerId: null,
            createdByBusiness: true,
            orderNumber: "ORD-000001",
            businessType: BusinessType.Retail,
            orderType: OrderType.Retail,
            customerName: "Público en general",
            customerPhone: null,
            customerNotes: null,
            tableNumber: null);
}
