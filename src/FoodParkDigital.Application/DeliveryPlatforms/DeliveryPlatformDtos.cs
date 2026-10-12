using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Application.DeliveryPlatforms;

/// <summary>The three delivery platforms of the business, in the order of the enum (Rappi, Uber Eats, DiDi Food).</summary>
public class DeliveryPlatformsDto
{
    public List<DeliveryPlatformDto> Platforms { get; set; } = [];
}

/// <summary>
/// How the business works with one delivery platform: whether it takes its orders, and the terms of its contract. A
/// platform never saved reads as not configured, off, without a commission and with the fiscal defaults.
/// </summary>
public class DeliveryPlatformDto
{
    public OrderChannel Channel { get; set; }
    /// <summary>Whether the business saved it once; until then it is off and has no commission.</summary>
    public bool IsConfigured { get; set; }
    /// <summary>Whether Vender registers its orders.</summary>
    public bool IsActive { get; set; }
    /// <summary>The commission of the contract, in percent; null until the business writes it (nobody's contract is made up).</summary>
    public decimal? CommissionPercent { get; set; }
    public PlatformCommissionBase CommissionBase { get; set; }
    /// <summary>IVA the platform charges on its commission, in percent.</summary>
    public decimal CommissionVatPercent { get; set; }
    /// <summary>ISR withheld from the sale without IVA when the platform charged it, in percent.</summary>
    public decimal IsrWithholdingPercent { get; set; }
    /// <summary>Share of the order's IVA withheld when the platform charged it, in percent of that IVA.</summary>
    public decimal VatWithholdingPercent { get; set; }
    /// <summary>The revision to send back when saving; 0 while it was never saved.</summary>
    public int Revision { get; set; }
    /// <summary>When it was last changed (or created), and by whom.</summary>
    public DateTimeOffset? ChangedAt { get; set; }
    public string? ChangedBy { get; set; }
    /// <summary>
    /// What the terms give on an order of $1,000 with IVA at 16 % included that the platform charged, as the domain
    /// calculates it; null while it is not configured.
    /// </summary>
    public PlatformSettlementExampleDto? Example { get; set; }
}

/// <summary>An example order of the platform: what it takes, what it withholds and what it deposits.</summary>
public class PlatformSettlementExampleDto
{
    public decimal OrderTotal { get; set; }
    /// <summary>The cost of the channel: the commission and its IVA.</summary>
    public decimal CommissionAndTax { get; set; }
    /// <summary>ISR and IVA withheld: payments on account of the business's taxes, not a cost.</summary>
    public decimal Withheld { get; set; }
    /// <summary>What the platform deposits for the order.</summary>
    public decimal ExpectedSettlement { get; set; }
}

/// <summary>A platform the business set up and whether it takes orders, without its rates (Vender and Pedidos read it).</summary>
public class DeliveryPlatformChannelDto
{
    public OrderChannel Channel { get; set; }
    public bool IsActive { get; set; }
}
