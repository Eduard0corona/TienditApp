import {
  CfdiCancellationMotive, CfdiEmailDocument, CreditNoteAdjustment, GlobalInvoicePeriodicity, OrderInvoiceStatus, type CustomerFiscalData,
} from '../types';

// c_RegimenFiscal and c_UsoCFDI entries a sale usually needs (the API accepts the whole catalog). 616, «Sin obligaciones
// fiscales», is the general public's: those sales go in the global invoice.
export const CFDI_REGIMES: Array<[string, string]> = [
  ['601', 'General de Ley Personas Morales'],
  ['603', 'Personas Morales con Fines no Lucrativos'],
  ['605', 'Sueldos y Salarios'],
  ['606', 'Arrendamiento'],
  ['612', 'Personas Físicas con Actividades Empresariales y Profesionales'],
  ['621', 'Incorporación Fiscal'],
  ['625', 'Actividades con Plataformas Tecnológicas'],
  ['626', 'Régimen Simplificado de Confianza'],
];

export const CFDI_USES: Array<[string, string]> = [
  ['G01', 'Adquisición de mercancías'],
  ['G03', 'Gastos en general'],
  ['D01', 'Honorarios médicos, dentales y gastos hospitalarios'],
  ['S01', 'Sin efectos fiscales'],
];

// c_UsoCFDI says which regimes may ask for each use; the PAC refuses any other pair (CFDI40161), as the API does first.
const BUSINESS_USE_REGIMES = ['601', '603', '606', '612', '620', '621', '622', '623', '624', '625', '626'];
const PERSONAL_DEDUCTION_REGIMES = ['605', '606', '607', '608', '611', '612', '614', '615', '625'];

/** The uses a receiver in that regime may ask for: G for those with an activity, D for individuals, S01 for anyone. */
export function cfdiUsesFor(regime: string): Array<[string, string]> {
  return CFDI_USES.filter(([use]) => use === 'S01'
    || (use.startsWith('D') ? PERSONAL_DEDUCTION_REGIMES : BUSINESS_USE_REGIMES).includes(regime));
}

export const PAYMENT_FORMS: Record<string, string> = {
  '01': 'Efectivo',
  '03': 'Transferencia',
  '04': 'Tarjeta',
  '06': 'Dinero electrónico',
  // What a write-off's credit note is settled by (Fase 28D).
  '15': 'Condonación',
  // Paid with the balance in the customer's favor (Fase 26D): an advance applied.
  '30': 'Aplicación de anticipos',
  // Paid online or by a delivery platform: an intermediary of payments.
  '31': 'Intermediario de pagos',
  '99': 'Por definir',
};

/** c_MetodoPago: a sale paid in full is invoiced in one exhibition; one still owed, in installments (Fase 28C). */
export const PAYMENT_METHODS: Record<string, string> = {
  PUE: 'Una sola exhibición',
  PPD: 'En parcialidades',
};

const STATUS: Record<number, { label: string; chip: string }> = {
  [OrderInvoiceStatus.Stamping]: { label: 'Timbrando', chip: 'ta-chip-preparing' },
  [OrderInvoiceStatus.Stamped]: { label: 'Timbrada', chip: 'ta-chip-ready' },
  [OrderInvoiceStatus.Refused]: { label: 'Rechazada', chip: 'ta-chip-cancelled' },
  [OrderInvoiceStatus.Cancelling]: { label: 'Cancelándose', chip: 'ta-chip-new' },
  [OrderInvoiceStatus.Cancelled]: { label: 'Cancelada', chip: 'ta-chip-cancelled' },
};

export function invoiceStatusLabel(status: OrderInvoiceStatus): string {
  return STATUS[status]?.label ?? 'Desconocido';
}

export function invoiceStatusChip(status: OrderInvoiceStatus): string {
  return STATUS[status]?.chip ?? 'ta-chip-done';
}

// A payment complement goes through the same states as an invoice; «complemento» asks for them in masculine.
const COMPLEMENT_STATUS_LABELS: Record<number, string> = {
  [OrderInvoiceStatus.Stamping]: 'Timbrando',
  [OrderInvoiceStatus.Stamped]: 'Timbrado',
  [OrderInvoiceStatus.Refused]: 'Rechazado',
  [OrderInvoiceStatus.Cancelling]: 'Cancelándose',
  [OrderInvoiceStatus.Cancelled]: 'Cancelado',
};

export function complementStatusLabel(status: OrderInvoiceStatus): string {
  return COMPLEMENT_STATUS_LABELS[status] ?? 'Desconocido';
}

/** The two reasons TienditApp cancels with (01 needs the invoice that replaces it; 04 is for the global invoice). */
export const CANCELLATION_MOTIVES: Array<[CfdiCancellationMotive, string]> = [
  [CfdiCancellationMotive.ErrorsWithoutReplacement, '02 · Tiene errores y no se va a sustituir'],
  [CfdiCancellationMotive.NotCarriedOut, '03 · La venta no se llevó a cabo'],
];

/** The reasons a payment complement is cancelled with: 02 when it has errors, 03 when the payment did not happen. */
export const COMPLEMENT_CANCELLATION_MOTIVES: Array<[CfdiCancellationMotive, string]> = [
  [CfdiCancellationMotive.ErrorsWithoutReplacement, '02 · Tiene errores (después emites otro para el cobro)'],
  [CfdiCancellationMotive.NotCarriedOut, '03 · El cobro no se llevó a cabo'],
];

/**
 * The reasons a credit note is cancelled with: 02 when it has errors; a write-off's also 03, when it did not happen (before
 * it is undone). A refund recorded in TienditApp is never undone, so its credit note takes only 02, as the API says.
 */
export function creditNoteCancellationMotives(kind: CreditNoteAdjustment): Array<[CfdiCancellationMotive, string]> {
  if (kind === CreditNoteAdjustment.Refund)
    return [[CfdiCancellationMotive.ErrorsWithoutReplacement, '02 · Tiene errores (después emites otra para el reembolso)']];
  return [
    [CfdiCancellationMotive.ErrorsWithoutReplacement, '02 · Tiene errores (después emites otra para la condonación)'],
    [CfdiCancellationMotive.NotCarriedOut, '03 · La condonación no se llevó a cabo'],
  ];
}

/**
 * The reasons a global invoice is cancelled with: 04 when a customer asked for the invoice of a sale it holds (it is
 * issued again without that sale), 02 when it has errors. Nobody accepts it: it goes to the general public.
 */
export const GLOBAL_CANCELLATION_MOTIVES: Array<[CfdiCancellationMotive, string]> = [
  [CfdiCancellationMotive.NominativeInGlobalInvoice, '04 · Un cliente pidió la factura de una de sus ventas'],
  [CfdiCancellationMotive.ErrorsWithoutReplacement, '02 · Tiene errores y no se va a sustituir'],
];

/** The periods a global invoice covers (c_Periodicidad); bimonthly only for the Incorporación Fiscal regime (621). */
export const GLOBAL_PERIODICITIES: Array<[GlobalInvoicePeriodicity, string]> = [
  [GlobalInvoicePeriodicity.Daily, 'Diaria'],
  [GlobalInvoicePeriodicity.Weekly, 'Semanal'],
  [GlobalInvoicePeriodicity.Fortnightly, 'Quincenal'],
  [GlobalInvoicePeriodicity.Monthly, 'Mensual'],
  [GlobalInvoicePeriodicity.Bimonthly, 'Bimestral'],
];

export function periodicityLabel(periodicity: GlobalInvoicePeriodicity): string {
  return GLOBAL_PERIODICITIES.find(([value]) => value === periodicity)?.[1] ?? 'Periodo';
}

/** A blob saved with its name, as the browser downloads a file. */
export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.click();
  // Long enough for the download to read it.
  window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

/** Fiscal data as the API takes it: the RFC in capitals, without spaces around. */
export function normalizeReceiver(value: CustomerFiscalData): CustomerFiscalData {
  return {
    rfc: value.rfc.trim().toUpperCase(),
    name: value.name.trim(),
    postalCode: value.postalCode.trim(),
    fiscalRegime: value.fiscalRegime,
    cfdiUse: value.cfdiUse,
  };
}

export const EMPTY_RECEIVER: CustomerFiscalData = { rfc: '', name: '', postalCode: '', fiscalRegime: '612', cfdiUse: 'G03' };

/** «la factura F-1», «el complemento de pago P-1», «la nota de crédito NC-1»: what an email of a CFDI carries (Fase 28E). */
export function cfdiEmailLabel(document: CfdiEmailDocument, number: string): string {
  switch (document) {
    case CfdiEmailDocument.PaymentComplement:
      return `el complemento de pago ${number}`;
    case CfdiEmailDocument.CreditNote:
      return `la nota de crédito ${number}`;
    default:
      return `la factura ${number}`;
  }
}

/**
 * One plain address, as the API takes it for the CFDI: printable ASCII without spaces (the platform's SMTP sends
 * seven-bit addresses), one @ and a domain with a dot (the API decides).
 */
export function isPlainEmail(email: string): boolean {
  const trimmed = email.trim();
  return /^[!-~]+$/.test(trimmed) && /^[^@,;<>"]+@[^@,;<>"]+\.[^@,;<>".]+$/.test(trimmed);
}
