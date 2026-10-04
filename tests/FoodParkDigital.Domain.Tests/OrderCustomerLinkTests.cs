using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

public class OrderCustomerLinkTests
{
    [Fact]
    public void CounterSale_ShouldBeAllowedWithoutCustomer()
    {
        var order = NewOrder(customerUserId: null, temporaryCustomerId: null, createdByBusiness: true);

        Assert.Null(order.CustomerUserId);
        Assert.Null(order.TemporaryCustomerId);
        Assert.Null(order.CustomerPhone);
    }

    [Fact]
    public void CustomerOrder_ShouldRequireACustomer()
    {
        Assert.Throws<DomainException>(() =>
            NewOrder(customerUserId: null, temporaryCustomerId: null, createdByBusiness: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Order_ShouldRejectBothCustomerLinks(bool createdByBusiness)
    {
        Assert.Throws<DomainException>(() =>
            NewOrder(customerUserId: Guid.NewGuid(), temporaryCustomerId: Guid.NewGuid(), createdByBusiness));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CustomerOrder_ShouldAcceptOneCustomerLink(bool registered, bool temporary)
    {
        var order = NewOrder(
            customerUserId: registered ? Guid.NewGuid() : null,
            temporaryCustomerId: temporary ? Guid.NewGuid() : null,
            createdByBusiness: false);

        Assert.Equal(registered, order.CustomerUserId.HasValue);
        Assert.Equal(temporary, order.TemporaryCustomerId.HasValue);
    }

    private static Order NewOrder(Guid? customerUserId, Guid? temporaryCustomerId, bool createdByBusiness) =>
        new(
            tenantId: Guid.NewGuid(),
            customerUserId: customerUserId,
            temporaryCustomerId: temporaryCustomerId,
            createdByBusiness: createdByBusiness,
            orderNumber: "ORD-000001",
            businessType: BusinessType.Food,
            orderType: OrderType.Retail,
            customerName: "Público en general",
            customerPhone: null,
            customerNotes: null,
            tableNumber: null);
}
