namespace FoodParkDigital.Domain.Tenants;

/// <summary>
/// The folio formats of a business. Orders count without end (ORD-000123); inventory movements, purchase orders,
/// physical counts and transfers start again every day (INV-20261005-0007, PO-20261005-0002, CNT-20261021-0001,
/// TRF-20261021-0001, by the UTC day).
/// </summary>
public static class Folios
{
    public const string OrderScope = "ORD";
    public const string OrderPrefix = OrderScope + "-";
    private const string InventoryMovementPrefix = "INV";
    private const string PurchaseOrderPrefix = "PO";
    private const string InventoryCountPrefix = "CNT";
    private const string InventoryTransferPrefix = "TRF";

    public static string OrderNumber(long number) => $"{OrderPrefix}{number:D6}";

    public static string InventoryMovementScope(DateTimeOffset movementDate) => DailyScope(InventoryMovementPrefix, movementDate);

    public static string PurchaseOrderScope(DateTimeOffset orderedAt) => DailyScope(PurchaseOrderPrefix, orderedAt);

    /// <summary>CNT-20261021: the physical counts started that UTC day (Fase 27A).</summary>
    public static string InventoryCountScope(DateTimeOffset startedAt) => DailyScope(InventoryCountPrefix, startedAt);

    /// <summary>TRF-20261021: the transfers between locations made that UTC day (Fase 27B).</summary>
    public static string InventoryTransferScope(DateTimeOffset transferredAt) => DailyScope(InventoryTransferPrefix, transferredAt);

    /// <summary>The folio of a daily counter: its scope and the number of the day.</summary>
    public static string DailyNumber(string scope, long number) => $"{scope}-{number:0000}";

    private static string DailyScope(string prefix, DateTimeOffset date) => $"{prefix}-{date.UtcDateTime:yyyyMMdd}";
}
