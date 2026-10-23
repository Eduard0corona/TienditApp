namespace FoodParkDigital.Domain.Fiscal.Enums;

/// <summary>
/// How often a business issues its global invoice (c_Periodicidad of the SAT, Fase 28B, ADR 0046). The value is the SAT's
/// code: 01 to 05.
/// </summary>
public enum GlobalInvoicePeriodicity
{
    /// <summary>01: one day.</summary>
    Daily = 1,

    /// <summary>02: a week, Monday to Sunday.</summary>
    Weekly = 2,

    /// <summary>03: a fortnight, the 1st to the 15th or the 16th to the end of the month.</summary>
    Fortnightly = 3,

    /// <summary>04: a calendar month.</summary>
    Monthly = 4,

    /// <summary>05: two months (January and February, March and April…), only for the regimes the SAT allows it to.</summary>
    Bimonthly = 5
}
