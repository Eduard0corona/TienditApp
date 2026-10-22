using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Reports.ReadModels;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders;

/// <summary>Why registering an order of a delivery platform is refused.</summary>
public static class PlatformOrderErrors
{
    /// <summary>
    /// A constant, so the API tells it apart: it answers 409 <c>possible_duplicate_order</c> and Vender asks before
    /// sending the same order again, with the same key, confirmed.
    /// </summary>
    public const string PossibleDuplicate =
        "Hoy ya registraste un pedido de esta plataforma con el mismo folio. Si es otro pedido, confírmalo para registrarlo.";

    /// <summary>The same long id of the platform on another order that counts, of any date (Fase 22I): also 409.</summary>
    public const string PossibleDuplicateLongId =
        "Ya registraste un pedido de esta plataforma con el mismo ID largo. Si es otro pedido, confírmalo para registrarlo.";

    /// <summary>Writing a long id that another order that counts already has (Fase 22I): also 409, confirmable.</summary>
    public const string LongIdInUse =
        "Otro pedido de esta plataforma ya tiene este ID largo. Si es correcto, confírmalo para guardarlo.";

    /// <summary>Whether <paramref name="error"/> is a warning the business confirms (409 <c>possible_duplicate_order</c>).</summary>
    public static bool IsPossibleDuplicate(string? error)
        => error is PossibleDuplicate or PossibleDuplicateLongId or LongIdInUse;

    /// <summary>«Rappi no está activa en tu negocio: actívala en Configuración › Negocio › Plataformas de reparto.»</summary>
    public static string NotActive(OrderChannel channel)
        => $"{OrderChannelRules.DisplayName(channel)} no está activa en tu negocio: "
           + "actívala en Configuración › Negocio › Plataformas de reparto.";
}

/// <summary>
/// The checks an order of Rappi, Uber Eats or DiDi Food needs before it is registered (<see cref="Commands.CreateOrderCommand"/>):
/// the platform active in the business, and whether the same folio was already registered today or the same long id ever.
/// </summary>
internal static class PlatformOrderPlacement
{
    /// <summary>The settings of the platform in the business when it is active; null when it is off or was never set up.</summary>
    public static Task<DeliveryPlatformSetting?> LoadActiveSettingAsync(
        IApplicationDbContext context,
        Guid tenantId,
        OrderChannel channel,
        CancellationToken cancellationToken)
        => context.DeliveryPlatformSettings
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Channel == channel && x.IsActive, cancellationToken);

    /// <summary>
    /// Whether the business already registered, on the same day of its own calendar, an order of the platform with the
    /// same folio that still counts: neither cancelled nor refunded in full (a void that could not cancel a delivered
    /// order leaves it refunded). Short folios repeat (Uber Eats' 5 characters, DiDi's pickup number), so this is a
    /// warning the business confirms, not a unique index; two captures at the same instant both pass it.
    /// </summary>
    public static Task<bool> HasSameDayDuplicateAsync(
        IApplicationDbContext context,
        Guid tenantId,
        OrderChannel channel,
        PlatformOrderNumber number,
        DateTimeOffset now,
        TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        // The day of the reports (DailySalesReadModel), from midnight in the business's time zone.
        var startOfToday = DailySalesReadModel.StartUtc(DailySalesReadModel.LocalDate(now, zone), zone);
        var folio = number.Value;
        return (
                from detail in context.PlatformOrderDetails
                join order in context.Orders on detail.OrderId equals order.Id
                where detail.TenantId == tenantId
                      && detail.Channel == channel
                      && detail.ExternalOrderNumber == folio
                      && detail.RegisteredAt >= startOfToday
                      && order.Status != OrderStatus.Cancelled
                      && !(order.PaidAmount > 0 && order.RefundedAmount >= order.PaidAmount)
                select order.Id)
            .AnyAsync(cancellationToken);
    }

    /// <summary>
    /// Whether another order of the platform in the business that still counts (as for the folio) has the same long id, of
    /// any date (Fase 22I): long ids do not repeat, so the same one is the same order captured twice, or a typo. A warning
    /// the business confirms too, not a unique index.
    /// </summary>
    public static Task<bool> HasLongIdDuplicateAsync(
        IApplicationDbContext context,
        Guid tenantId,
        OrderChannel channel,
        PlatformOrderId longId,
        Guid? exceptOrderId,
        CancellationToken cancellationToken)
    {
        var value = longId.Value;
        return (
                from detail in context.PlatformOrderDetails
                join order in context.Orders on detail.OrderId equals order.Id
                where detail.TenantId == tenantId
                      && detail.Channel == channel
                      && detail.ExternalOrderId == value
                      && (exceptOrderId == null || order.Id != exceptOrderId)
                      && order.Status != OrderStatus.Cancelled
                      && !(order.PaidAmount > 0 && order.RefundedAmount >= order.PaidAmount)
                select order.Id)
            .AnyAsync(cancellationToken);
    }
}
