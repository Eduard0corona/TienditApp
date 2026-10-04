namespace FoodParkDigital.Application.Orders.DTOs;

/// <summary>
/// The CFDI 4.0 invoice ("Ingreso") an order would produce, with the amounts already split per concept. It is a
/// preview: nothing is stamped or sent to the SAT.
/// </summary>
public class CfdiDraftDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = null!;
    public string Version { get; set; } = "4.0";
    /// <summary>TipoDeComprobante: I (ingreso).</summary>
    public string VoucherType { get; set; } = "I";
    public string Currency { get; set; } = "MXN";
    /// <summary>Exportacion: 01 (no aplica).</summary>
    public string Exportation { get; set; } = "01";
    /// <summary>LugarExpedicion: fiscal postal code of the business.</summary>
    public string? IssuedAtPostalCode { get; set; }
    /// <summary>FormaPago (c_FormaPago).</summary>
    public string PaymentForm { get; set; } = null!;
    /// <summary>MetodoPago: PUE when the sale is paid in full, PPD otherwise.</summary>
    public string PaymentMethod { get; set; } = null!;
    public CfdiPartyDto Issuer { get; set; } = new();
    public CfdiPartyDto Receiver { get; set; } = new();
    /// <summary>True when no receiver was given and the sale goes to the general public.</summary>
    public bool IsGeneralPublic { get; set; }
    public List<CfdiConceptDto> Concepts { get; set; } = new();
    /// <summary>Transfers of the whole invoice, one per IVA rate.</summary>
    public List<CfdiTransferDto> Transfers { get; set; } = new();
    public decimal SubTotal { get; set; }
    public decimal TotalTransferred { get; set; }
    public decimal Total { get; set; }
    public decimal OrderTotal { get; set; }
    /// <summary>Invoice total minus order total: zero unless rounding could not be absorbed by any concept.</summary>
    public decimal Difference { get; set; }
    /// <summary>True when nothing in <see cref="Blockers"/> prevents stamping it.</summary>
    public bool IsReadyToStamp { get; set; }
    /// <summary>Missing data the SAT would reject.</summary>
    public List<string> Blockers { get; set; } = new();
    /// <summary>Things to review that do not prevent stamping.</summary>
    public List<string> Warnings { get; set; } = new();
}

public class CfdiPartyDto
{
    public string? Rfc { get; set; }
    public string? Name { get; set; }
    /// <summary>c_RegimenFiscal code.</summary>
    public string? FiscalRegime { get; set; }
    /// <summary>Fiscal postal code (receiver only; the issuer's is <see cref="CfdiDraftDto.IssuedAtPostalCode"/>).</summary>
    public string? PostalCode { get; set; }
    /// <summary>UsoCFDI (receiver only).</summary>
    public string? CfdiUse { get; set; }
}

public class CfdiConceptDto
{
    public Guid OrderItemId { get; set; }
    /// <summary>ClaveProdServ.</summary>
    public string ProductCode { get; set; } = null!;
    /// <summary>ClaveUnidad.</summary>
    public string UnitCode { get; set; } = null!;
    public string Description { get; set; } = null!;
    public decimal Quantity { get; set; }
    /// <summary>ValorUnitario before IVA, with up to six decimals.</summary>
    public decimal UnitValue { get; set; }
    /// <summary>Importe before IVA.</summary>
    public decimal Amount { get; set; }
    /// <summary>ObjetoImp: 01 or 02.</summary>
    public string TaxObject { get; set; } = null!;
    public CfdiTransferDto? Transfer { get; set; }
}

public class CfdiTransferDto
{
    public decimal Base { get; set; }
    /// <summary>Impuesto: 002 (IVA).</summary>
    public string Tax { get; set; } = "002";
    /// <summary>TipoFactor: Tasa or Exento.</summary>
    public string FactorType { get; set; } = null!;
    /// <summary>TasaOCuota with six decimals; none when exempt.</summary>
    public decimal? RateOrQuota { get; set; }
    /// <summary>Importe of the tax; none when exempt.</summary>
    public decimal? Amount { get; set; }
}
