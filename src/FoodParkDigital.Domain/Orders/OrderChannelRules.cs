using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders;

/// <summary>What each order channel means, in one place: the delivery platforms and the name shown for each one.</summary>
public static class OrderChannelRules
{
    public const string InvalidChannelMessage = "El canal del pedido no es válido.";

    /// <summary>The delivery platforms, in the order of the enum: their orders come in through the platform's app.</summary>
    public static readonly IReadOnlyList<OrderChannel> DeliveryPlatforms =
        [OrderChannel.Rappi, OrderChannel.UberEats, OrderChannel.DidiFood];

    /// <summary>Whether orders of the channel come from a delivery platform (Rappi, Uber Eats or DiDi Food).</summary>
    public static bool IsDeliveryPlatform(OrderChannel channel) => channel is
        OrderChannel.Rappi or OrderChannel.UberEats or OrderChannel.DidiFood;

    /// <summary>The name of the channel shown to the business, as the screens label it.</summary>
    public static string DisplayName(OrderChannel channel) => channel switch
    {
        OrderChannel.Counter => "Mostrador",
        OrderChannel.WhatsApp => "WhatsApp",
        OrderChannel.Online => "En línea",
        OrderChannel.Rappi => "Rappi",
        OrderChannel.UberEats => "Uber Eats",
        OrderChannel.DidiFood => "DiDi Food",
        _ => throw new DomainException(InvalidChannelMessage)
    };
}
