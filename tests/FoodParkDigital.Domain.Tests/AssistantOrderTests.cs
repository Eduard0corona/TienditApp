using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The order an AI assistant prepares for a customer (Fase 32B, ADR 0096): the lines of a cart and what they cost then,
/// with a link the customer opens to review and send it. It is sent once, within 24 hours, and forgets its lines then.
/// </summary>
public class AssistantOrderTests
{
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly Guid Latte = Guid.NewGuid();
    private static readonly Guid Grande = Guid.NewGuid();
    private static readonly Guid Avena = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 18, 0, 0, TimeSpan.Zero);
    private static readonly string Token = new('A', Order.TrackingTokenLength);

    private static AccountCartLine Line(Guid product, int quantity, string? notes = null, params Guid[] options)
        => new(product, options, quantity, notes);

    private static AssistantOrder Prepared(string? postalCode = null, string? source = "Claude")
        => AssistantOrder.Prepare(Business, Token, [Line(Latte, 2, null, Grande)], postalCode, 110m, source, Now);

    [Fact]
    public void Prepare_ShouldKeep_TheLinesAsACart_ThePostalCode_WhatItCost_AndWhoPreparedIt_ForADay()
    {
        var order = AssistantOrder.Prepare(Business, Token,
        [
            Line(Latte, 1, null, Avena, Grande),
            Line(Latte, 2, " ", Grande, Avena),
            Line(Latte, 1, " sin azúcar ")
        ], " 72000 ", 165.5m, "  Claude  ", Now);

        Assert.Equal(Business, order.TenantId);
        Assert.Equal(Token, order.Token);
        // The same product with the same options is one line, as in the store's cart; a note is its own.
        Assert.Equal(
            [
                new AccountCartLine(Latte, new[] { Avena, Grande }.Order().ToList(), 3, null),
                new AccountCartLine(Latte, [], 1, "sin azúcar")
            ],
            order.Lines);
        Assert.Equal("72000", order.PostalCode);
        Assert.Equal(165.5m, order.ItemsTotal);
        Assert.Equal("Claude", order.Source);
        Assert.Equal(AssistantOrderStatus.Pending, order.Status);
        Assert.Equal(Now, order.PreparedAt);
        Assert.Equal(Now.AddHours(24), order.ExpiresAt);
        Assert.Null(order.OrderId);
        Assert.True(order.IsOpen(Now.AddHours(24).AddTicks(-1)));
        Assert.False(order.IsOpen(Now.AddHours(24)));
    }

    [Fact]
    public void Prepare_ShouldLeaveOut_AnEmptyPostalCodeOrSource_AndShortenALongSource()
    {
        var order = AssistantOrder.Prepare(Business, Token, [Line(Latte, 1)], " ", 0m, new string('x', 150), Now);

        Assert.Null(order.PostalCode);
        Assert.Equal(new string('x', AssistantOrder.MaxSourceLength), order.Source);
        Assert.Null(Prepared(source: "  ").Source);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA!")]
    [InlineData("")]
    public void Prepare_ShouldRefuse_ALinkThatIsNotOne(string token)
    {
        var error = Assert.Throws<DomainException>(
            () => AssistantOrder.Prepare(Business, token, [Line(Latte, 1)], null, 55m, null, Now));
        Assert.Equal("El enlace del pedido no es válido.", error.Message);
    }

    [Fact]
    public void Prepare_ShouldRefuse_NoBusiness_NoLines_ABadLine_ABadPostalCode_OrABadTotal()
    {
        Assert.Equal("El pedido necesita un negocio.", Assert.Throws<DomainException>(
            () => AssistantOrder.Prepare(Guid.Empty, Token, [Line(Latte, 1)], null, 55m, null, Now)).Message);
        Assert.Equal("El pedido necesita al menos un producto.", Assert.Throws<DomainException>(
            () => AssistantOrder.Prepare(Business, Token, [], null, 0m, null, Now)).Message);
        Assert.Equal(AccountCart.BadLineMessage, Assert.Throws<DomainException>(
            () => AssistantOrder.Prepare(Business, Token, [Line(Latte, 100)], null, 55m, null, Now)).Message);
        Assert.Equal(AccountCart.TooManyLinesMessage, Assert.Throws<DomainException>(
            () => AssistantOrder.Prepare(Business, Token,
                Enumerable.Range(0, AccountCart.MaxLines + 1).Select(_ => Line(Guid.NewGuid(), 1)), null, 55m, null, Now)).Message);
        Assert.Equal("El código postal no es válido.", Assert.Throws<DomainException>(
            () => AssistantOrder.Prepare(Business, Token, [Line(Latte, 1)], "7200", 55m, null, Now)).Message);
        Assert.Equal("El total del pedido no es válido.", Assert.Throws<DomainException>(
            () => AssistantOrder.Prepare(Business, Token, [Line(Latte, 1)], null, -1m, null, Now)).Message);
        Assert.Equal("El total del pedido no es válido.", Assert.Throws<DomainException>(
            () => AssistantOrder.Prepare(Business, Token, [Line(Latte, 1)], null, 55.555m, null, Now)).Message);
    }

    [Fact]
    public void Confirm_ShouldTie_TheOrderTheCustomerSent_AndForgetTheLines()
    {
        var order = Prepared("72000");
        var sent = Guid.NewGuid();

        order.Confirm(sent, Now.AddHours(3));

        Assert.Equal(AssistantOrderStatus.Confirmed, order.Status);
        Assert.Equal(sent, order.OrderId);
        Assert.Equal(Now.AddHours(3), order.ConfirmedAt);
        Assert.Empty(order.Lines);
        Assert.False(order.IsOpen(Now.AddHours(3)));
    }

    [Fact]
    public void Confirm_ShouldBeOnce_InItsTime_AndWithAnOrder()
    {
        var once = Prepared();
        once.Confirm(Guid.NewGuid(), Now.AddHours(1));
        Assert.Equal(AssistantOrder.AlreadyConfirmedMessage,
            Assert.Throws<DomainException>(() => once.Confirm(Guid.NewGuid(), Now.AddHours(2))).Message);

        var late = Prepared();
        Assert.Equal(AssistantOrder.ExpiredMessage,
            Assert.Throws<DomainException>(() => late.Confirm(Guid.NewGuid(), Now.AddHours(24))).Message);
        Assert.Equal(AssistantOrderStatus.Pending, late.Status);
        Assert.NotEmpty(late.Lines);

        Assert.Equal("El pedido enviado no existe.",
            Assert.Throws<DomainException>(() => Prepared().Confirm(Guid.Empty, Now)).Message);
    }

    [Fact]
    public void ABusiness_ShouldReceiveOrdersFromAssistants_UntilItTurnsThemOff()
    {
        var settings = new TenantSettings(Business);

        Assert.True(settings.AssistantOrders);
        settings.SetAssistantOrders(false);
        Assert.False(settings.AssistantOrders);
    }
}
