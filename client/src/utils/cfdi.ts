import { CfdiCancellationMotive, OrderInvoiceStatus, type CustomerFiscalData } from '../types';

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
  // Paid with the balance in the customer's favor (Fase 26D): an advance applied.
  '30': 'Aplicación de anticipos',
  // Paid online or by a delivery platform: an intermediary of payments.
  '31': 'Intermediario de pagos',
  '99': 'Por definir',
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

/** The two reasons TienditApp cancels with (01 needs the invoice that replaces it; 04 is for the global invoice). */
export const CANCELLATION_MOTIVES: Array<[CfdiCancellationMotive, string]> = [
  [CfdiCancellationMotive.ErrorsWithoutReplacement, '02 · Tiene errores y no se va a sustituir'],
  [CfdiCancellationMotive.NotCarriedOut, '03 · La venta no se llevó a cabo'],
];

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
