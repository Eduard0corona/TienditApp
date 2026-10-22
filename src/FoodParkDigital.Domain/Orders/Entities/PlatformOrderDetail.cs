using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;

namespace FoodParkDigital.Domain.Orders.Entities;

/// <summary>
/// What an order of a delivery platform was registered with: the folio of the platform's app, who charged it, the
/// terms of the platform then (and their revision) and the amounts calculated from them. It is created once, with the
/// order (<see cref="Order.RegisterPlatformOrder"/>), and its amounts never change: changing the terms of the platform
/// later does not rewrite it, as an order keeps the tax policy it was sold with. Only the platform's long id of the order,
/// a reference to find it in a statement, may be added or corrected later (<see cref="Order.ChangePlatformOrderId"/>).
/// </summary>
public class PlatformOrderDetail : BaseEntity, ITenantScoped
{
    private PlatformOrderDetail() { }

    internal PlatformOrderDetail(
        Guid tenantId,
        Guid orderId,
        OrderChannel channel,
        PlatformOrderNumber externalOrderNumber,
        PlatformCollectedBy collectedBy,
        int settingsRevision,
        PlatformTerms terms,
        decimal orderTotal,
        PlatformOrderAmounts amounts,
        DateTimeOffset registeredAt,
        PlatformOrderId? externalOrderId)
    {
        TenantId = tenantId;
        OrderId = orderId;
        Channel = channel;
        ExternalOrderNumber = externalOrderNumber.Value;
        ExternalOrderId = externalOrderId?.Value;
        CollectedBy = collectedBy;
        SettingsRevision = settingsRevision;
        CommissionPercent = terms.CommissionPercent;
        CommissionBase = terms.CommissionBase;
        CommissionVatPercent = terms.CommissionVatPercent;
        IsrWithholdingPercent = terms.IsrWithholdingPercent;
        VatWithholdingPercent = terms.VatWithholdingPercent;
        OrderTotal = orderTotal;
        Commission = amounts.Commission;
        CommissionTax = amounts.CommissionTax;
        IsrWithheld = amounts.IsrWithheld;
        VatWithheld = amounts.VatWithheld;
        ExpectedSettlement = amounts.ExpectedSettlement;
        RegisteredAt = registeredAt;
    }

    public Guid TenantId { get; private set; }
    public Guid OrderId { get; private set; }
    /// <summary>The platform, the same as the order's channel.</summary>
    public OrderChannel Channel { get; private set; }
    /// <summary>The folio of the platform's app, normalized (<see cref="PlatformOrderNumber"/>).</summary>
    public string ExternalOrderNumber { get; private set; } = null!;
    /// <summary>The platform's long id of the order, normalized (<see cref="PlatformOrderId"/>); null when it was not written.</summary>
    public string? ExternalOrderId { get; private set; }
    public PlatformCollectedBy CollectedBy { get; private set; }
    /// <summary>The revision of the platform's settings the terms were copied from.</summary>
    public int SettingsRevision { get; private set; }

    public decimal CommissionPercent { get; private set; }
    public PlatformCommissionBase CommissionBase { get; private set; }
    public decimal CommissionVatPercent { get; private set; }
    public decimal IsrWithholdingPercent { get; private set; }
    public decimal VatWithholdingPercent { get; private set; }

    /// <summary>The order's total when it was registered, the base of the amounts below.</summary>
    public decimal OrderTotal { get; private set; }
    public decimal Commission { get; private set; }
    public decimal CommissionTax { get; private set; }
    public decimal IsrWithheld { get; private set; }
    public decimal VatWithheld { get; private set; }
    /// <summary>
    /// What the platform should deposit for the order; negative when the business charged it (the platform discounts
    /// its commission and the IVA of it from a deposit).
    /// </summary>
    public decimal ExpectedSettlement { get; private set; }

    /// <summary>When the order was registered, by the server's clock: the same time as its «Plataforma» payment.</summary>
    public DateTimeOffset RegisteredAt { get; private set; }

    /// <summary>Writes, corrects or clears the long id; false when it is the same.</summary>
    internal bool ChangeExternalOrderId(PlatformOrderId? externalOrderId)
    {
        var value = externalOrderId?.Value;
        if (value == ExternalOrderId)
            return false;

        ExternalOrderId = value;
        return true;
    }
}
