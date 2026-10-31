namespace FoodParkDigital.Domain.Fiscal.Enums;

/// <summary>What an email of a CFDI tells its customer (Fase 28N, ADR 0059). Stored as a number: keep the values.</summary>
public enum CfdiEmailKind
{
    /// <summary>The XML and the PDF of a stamped CFDI (Fase 28E).</summary>
    Documents = 0,

    /// <summary>The SAT cancelled the CFDI: it no longer stands, and why (Fase 28N).</summary>
    Cancellation = 1
}
