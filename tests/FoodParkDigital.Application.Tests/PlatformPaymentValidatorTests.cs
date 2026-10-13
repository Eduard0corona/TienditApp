using FoodParkDigital.Application.Finance.Commands;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The «Plataforma» payment method exists in the contract, but no client records the platform's payment or puts it in a
/// drawer: the order of a delivery platform records it by itself (the rules of those orders: PlatformOrderFlowTests).
/// </summary>
public class PlatformPaymentValidatorTests
{
    [Fact]
    public void CreateOrder_ShouldRefuseAPlatformPaymentFromTheClient()
    {
        var result = new CreateOrderCommandValidator().Validate(Sale() with
        {
            Payments = [new CreateOrderPaymentDto { Amount = 50m, Method = OrderPaymentMethod.Platform }]
        });

        Assert.Contains(result.Errors, e => e.ErrorMessage == CreateOrderCommandValidator.PlatformPaymentMessage);
    }

    [Theory]
    [InlineData(OrderPaymentMethod.Cash)]
    [InlineData(OrderPaymentMethod.Card)]
    [InlineData(OrderPaymentMethod.Transfer)]
    [InlineData(OrderPaymentMethod.DigitalWallet)]
    [InlineData(OrderPaymentMethod.Other)]
    public void CreateOrder_ShouldKeepAcceptingTheMethodsTheBusinessCharges(OrderPaymentMethod method)
    {
        var result = new CreateOrderCommandValidator().Validate(Sale() with
        {
            Payments = [new CreateOrderPaymentDto { Amount = 50m, Method = method }]
        });

        Assert.True(result.IsValid, string.Join(" ", result.Errors));
    }

    [Theory]
    [InlineData(OrderChannel.Rappi)]
    [InlineData(OrderChannel.UberEats)]
    [InlineData(OrderChannel.DidiFood)]
    public void CreateOrder_ShouldTakeTheDeliveryPlatforms_WithTheFolioOfTheirApp(OrderChannel channel)
    {
        var validator = new CreateOrderCommandValidator();

        var withFolio = validator.Validate(Sale() with { Channel = channel, ExternalOrderNumber = "#bc 953" });
        var withoutFolio = validator.Validate(Sale() with { Channel = channel });

        Assert.True(withFolio.IsValid, string.Join(" ", withFolio.Errors));
        Assert.Equal(PlatformOrderNumber.RequiredMessage, Assert.Single(withoutFolio.Errors).ErrorMessage);
    }

    [Fact]
    public void CreateOrder_ShouldRefuseAPlatformPayment_EvenWhenTheBusinessCollectsAnOrderOfAPlatform()
    {
        var result = new CreateOrderCommandValidator().Validate(Sale() with
        {
            Channel = OrderChannel.DidiFood,
            ExternalOrderNumber = "12",
            CollectedBy = PlatformCollectedBy.Business,
            Payments = [new CreateOrderPaymentDto { Amount = 50m, Method = OrderPaymentMethod.Platform }]
        });

        Assert.Equal(CreateOrderCommandValidator.PlatformPaymentMessage, Assert.Single(result.Errors).ErrorMessage);
    }

    [Theory]
    [InlineData(OrderChannel.Counter, true)]
    [InlineData(OrderChannel.WhatsApp, true)]
    [InlineData(OrderChannel.Online, false)] // Online orders come from the public menu.
    public void CreateOrder_ShouldKeepTheRulesOfTheOtherChannels(OrderChannel channel, bool valid)
    {
        var result = new CreateOrderCommandValidator().Validate(Sale() with { Channel = channel });

        Assert.Equal(valid, result.IsValid);
        if (!valid)
            Assert.Equal(OrderChannelRules.InvalidChannelMessage, Assert.Single(result.Errors).ErrorMessage);
    }

    [Fact]
    public void CreateOrder_ShouldRefuseUnknownChannels()
    {
        var result = new CreateOrderCommandValidator().Validate(Sale() with { Channel = (OrderChannel)6 });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void AddOrderPayment_ShouldRefuseThePlatformMethod()
    {
        var validator = new AddOrderPaymentCommandValidator();

        var platform = validator.Validate(new AddOrderPaymentCommand { OrderId = Guid.NewGuid(), Amount = 50m, Method = OrderPaymentMethod.Platform });
        var cash = validator.Validate(new AddOrderPaymentCommand { OrderId = Guid.NewGuid(), Amount = 50m, Method = OrderPaymentMethod.Cash });

        Assert.Equal(OrderPaymentMethodRules.PlatformOnlyOnCreationMessage, Assert.Single(platform.Errors).ErrorMessage);
        Assert.True(cash.IsValid, string.Join(" ", cash.Errors));
    }

    [Theory]
    [InlineData(CashTransactionType.Deposit)]
    [InlineData(CashTransactionType.Expense)]
    [InlineData(CashTransactionType.Withdrawal)]
    [InlineData(CashTransactionType.Refund)]
    public void AddCashTransaction_ShouldRefuseThePlatformMethod(CashTransactionType type)
    {
        var validator = new AddCashTransactionCommandValidator();

        var platform = validator.Validate(Movement(type, OrderPaymentMethod.Platform));
        var card = validator.Validate(Movement(type, OrderPaymentMethod.Card));
        var withoutMethod = validator.Validate(Movement(type, null));

        Assert.Equal(OrderPaymentMethodRules.NeverEntersADrawerMessage, Assert.Single(platform.Errors).ErrorMessage);
        Assert.True(card.IsValid, string.Join(" ", card.Errors));
        Assert.True(withoutMethod.IsValid, string.Join(" ", withoutMethod.Errors));
    }

    private static AddCashTransactionCommand Movement(CashTransactionType type, OrderPaymentMethod? method) => new()
    {
        CashSessionId = Guid.NewGuid(),
        TransactionType = type,
        Amount = 120m,
        PaymentMethod = method
    };

    private static CreateOrderCommand Sale() => new()
    {
        CreatedByBusiness = true,
        OrderType = OrderType.Retail,
        Items = [new CreateOrderItemDto { ProductId = Guid.NewGuid(), Quantity = 1 }]
    };
}
