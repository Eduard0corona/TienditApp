using System.Globalization;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Application.Orders;

/// <summary>
/// Spanish names of order states for messages sent to customers (WhatsApp templates). The web client
/// keeps the same catalog in client/src/utils/labels.ts.
/// </summary>
public static class OrderStatusLabels
{
    public static string ToSpanish(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "Pendiente",
        OrderStatus.Confirmed => "Confirmada",
        OrderStatus.InPreparation => "En preparación",
        OrderStatus.Ready => "Lista",
        OrderStatus.Accepted => "Aceptada",
        OrderStatus.InProgress => "En progreso",
        OrderStatus.Delivered => "Entregada",
        OrderStatus.Completed => "Completada",
        OrderStatus.Cancelled => "Cancelada",
        _ => status.ToString()
    };

    /// <summary>Amount for customer messages ($1,234.50), independent of the server culture.</summary>
    public static string FormatMoney(decimal amount)
        => string.Create(CultureInfo.InvariantCulture, $"${amount:N2}");
}
