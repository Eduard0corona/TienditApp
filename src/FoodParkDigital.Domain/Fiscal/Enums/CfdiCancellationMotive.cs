namespace FoodParkDigital.Domain.Fiscal.Enums;

/// <summary>Why a CFDI is cancelled, as the SAT's catalog of cancellation motives says (Fase 28A, ADR 0045).</summary>
public enum CfdiCancellationMotive
{
    /// <summary>01: it has errors and another invoice replaces it. Not offered yet: it needs the invoice that replaces it.</summary>
    ErrorsWithReplacement = 1,

    /// <summary>02: it has errors and no invoice replaces it.</summary>
    ErrorsWithoutReplacement = 2,

    /// <summary>03: the sale did not happen.</summary>
    NotCarriedOut = 3,

    /// <summary>04: a nominative sale related to a global invoice: the global invoice is cancelled with it when a customer asks for theirs (Fase 28B).</summary>
    NominativeInGlobalInvoice = 4
}
