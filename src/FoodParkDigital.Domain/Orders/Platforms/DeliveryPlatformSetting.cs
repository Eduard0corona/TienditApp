using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.Platforms;

/// <summary>
/// How a business works with one delivery platform (Configuración › Negocio › Plataformas de reparto): whether it
/// takes its orders, and the terms of its contract. One row per business and platform. Each order of the platform
/// copies the terms and the <see cref="Revision"/> it was registered with (<see cref="Entities.PlatformOrderDetail"/>),
/// so changing them, or turning the platform off, never rewrites an order.
/// </summary>
public class DeliveryPlatformSetting : AuditableEntity, ITenantScoped, IAggregateRoot
{
    public const string TenantRequiredMessage = "El negocio es obligatorio.";
    public const string NotADeliveryPlatformMessage = "Esa no es una plataforma de reparto.";

    private DeliveryPlatformSetting() { }

    public DeliveryPlatformSetting(Guid tenantId, OrderChannel channel, bool isActive, PlatformTerms terms)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException(TenantRequiredMessage);
        if (!OrderChannelRules.IsDeliveryPlatform(channel))
            throw new DomainException(NotADeliveryPlatformMessage);
        ArgumentNullException.ThrowIfNull(terms);

        TenantId = tenantId;
        Channel = channel;
        IsActive = isActive;
        Apply(terms);
        Revision = 1;
    }

    public Guid TenantId { get; private set; }

    /// <summary>The platform. It never changes: another platform is another row.</summary>
    public OrderChannel Channel { get; private set; }

    /// <summary>Whether the business takes orders of the platform: only then Vender registers them.</summary>
    public bool IsActive { get; private set; }

    public decimal CommissionPercent { get; private set; }
    public PlatformCommissionBase CommissionBase { get; private set; }
    public decimal CommissionVatPercent { get; private set; }
    public decimal IsrWithholdingPercent { get; private set; }
    public decimal VatWithholdingPercent { get; private set; }

    /// <summary>Starts at 1 and goes up by one with every change; a save with an older one is refused.</summary>
    public int Revision { get; private set; }

    /// <summary>The terms of the contract, checked again against the limits of the domain.</summary>
    public PlatformTerms Terms => new(
        CommissionPercent, CommissionBase, CommissionVatPercent, IsrWithholdingPercent, VatWithholdingPercent);

    /// <summary>Changes whether the platform is active and its terms.</summary>
    /// <returns>False, without a new revision, when nothing changed.</returns>
    public bool Update(bool isActive, PlatformTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        if (IsActive == isActive && Terms == terms)
            return false;

        IsActive = isActive;
        Apply(terms);
        Revision++;
        return true;
    }

    private void Apply(PlatformTerms terms)
    {
        CommissionPercent = terms.CommissionPercent;
        CommissionBase = terms.CommissionBase;
        CommissionVatPercent = terms.CommissionVatPercent;
        IsrWithholdingPercent = terms.IsrWithholdingPercent;
        VatWithholdingPercent = terms.VatWithholdingPercent;
    }
}
