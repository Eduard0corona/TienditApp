// Invoicing (CFDI): the enums of the invoice of an order, the global invoice, credit notes and the CFDI issued.

// The invoice of an order stamped through the PAC (Fase 28A), the global invoice of a period (Fase 28B), what a credit
// note credits (Fase 28D), the kinds of CFDI of Reportes › CFDI emitidos (Fase 28H), which file of a CFDI its WhatsApp
// link downloads (Fase 28M), whether an email carries a CFDI's files or the notice that the SAT cancelled it (Fase 28N) and
// why an email of a month's CFDI to the accountant did not go (Fase 28O).
export {
  CfdiCancellationMotive, CfdiEmailDocument, CfdiEmailFailure, CfdiEmailKind, CfdiMonthEmailFailure, CreditNoteAdjustment,
  GlobalInvoicePeriodicity, IssuedCfdiExportFormat, IssuedCfdiKind, OrderInvoiceFileKind, OrderInvoiceStatus,
} from '../api/generated/enums';
