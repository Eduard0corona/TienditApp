using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>The link a customer follows the order with (Fase 24C): once, and only with the shape of one.</summary>
public class OrderTrackingTokenTests
{
    private const string Token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE";

    [Fact]
    public void Order_ShouldStartWithoutALink_AndKeepTheOneItIsGiven()
    {
        var order = NewOrder();
        Assert.Null(order.TrackingToken);

        order.IssueTrackingToken(Token);

        Assert.Equal(Token, order.TrackingToken);
    }

    [Fact]
    public void Link_ShouldBeGivenOnce()
    {
        var order = NewOrder();
        order.IssueTrackingToken(Token);

        var error = Assert.Throws<DomainException>(() => order.IssueTrackingToken("ZyXwVuTsRqPoNmLkJiHgFeDcBa9876543210_-ZyXwV"));

        Assert.Equal("El pedido ya tiene su enlace de seguimiento.", error.Message);
        Assert.Equal(Token, order.TrackingToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("corto")]
    // One character short and one too many.
    [InlineData("AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCd")]
    [InlineData("AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdEf")]
    // Standard Base64 and padding are not URL-safe; neither are spaces or accents.
    [InlineData("AbCdEfGhIjKlMnOpQrStUvWxYz0123456789+/AbCdE")]
    [InlineData("AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCd=")]
    [InlineData("AbCdEfGhIjKlMnOpQrStUvWxYz0123456789 _AbCdE")]
    [InlineData("ÁbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE")]
    public void Link_ShouldHaveTheShapeOfOne(string token)
    {
        var order = NewOrder();

        var error = Assert.Throws<DomainException>(() => order.IssueTrackingToken(token));

        Assert.Equal("El enlace de seguimiento no es válido.", error.Message);
        Assert.Null(order.TrackingToken);
        Assert.False(Order.IsTrackingToken(token));
    }

    [Fact]
    public void IsTrackingToken_ShouldTakeOnlyTheShape()
    {
        Assert.True(Order.IsTrackingToken(Token));
        Assert.False(Order.IsTrackingToken(null));
    }

    private static Order NewOrder() =>
        new(
            tenantId: Guid.NewGuid(),
            customerUserId: null,
            temporaryCustomerId: Guid.NewGuid(),
            createdByBusiness: false,
            orderNumber: "ORD-000001",
            businessType: BusinessType.Food,
            orderType: OrderType.Retail,
            customerName: "Valeria Morales",
            customerPhone: "+525522107765",
            customerNotes: null,
            tableNumber: null,
            channel: OrderChannel.Online);
}
