namespace FoodParkDigital.Domain.Fiscal;

/// <summary>CFDI 4.0 "ObjetoImp": whether a concept is subject to tax.</summary>
public enum CfdiTaxObject
{
    /// <summary>01: not subject to tax.</summary>
    NotSubject = 1,
    /// <summary>02: subject to tax (also an IVA-exempt concept, declared with TipoFactor "Exento").</summary>
    Subject = 2
}
