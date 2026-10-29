import type { IssuedCfdi } from '../../api/invoicing';
import { IssuedCfdiKind } from '../../types';

export const KIND_LABELS: Record<IssuedCfdiKind, string> = {
  [IssuedCfdiKind.OrderInvoice]: 'Factura',
  [IssuedCfdiKind.GlobalInvoice]: 'Factura global',
  [IssuedCfdiKind.PaymentComplement]: 'Complemento de pago',
  [IssuedCfdiKind.CreditNote]: 'Nota de crédito',
};

/** «Factura», or «CFDI» for a kind this version does not know yet. */
export function kindLabel(kind: IssuedCfdiKind): string {
  return KIND_LABELS[kind] ?? 'CFDI';
}

/** «Complemento» and «CFDI» are masculine; the rest of the kinds, feminine. */
function isMasculine(kind: IssuedCfdiKind): boolean {
  return kind === IssuedCfdiKind.PaymentComplement || !(kind in KIND_LABELS);
}

/** «la factura F-1», «el complemento de pago P-1»: the CFDI a sentence or a button of its row names. */
export function theDocument(cfdi: Pick<IssuedCfdi, 'kind' | 'number'>): string {
  if (!(cfdi.kind in KIND_LABELS)) return `el CFDI ${cfdi.number}`;
  return `${isMasculine(cfdi.kind) ? 'el' : 'la'} ${kindLabel(cfdi.kind).toLowerCase()} ${cfdi.number}`;
}

/** «de la factura F-1», «del complemento de pago P-1»: what a button of the row acts on. */
export function ofDocument(cfdi: Pick<IssuedCfdi, 'kind' | 'number'>): string {
  const the = theDocument(cfdi);
  return the.startsWith('el ') ? `del ${the.slice(3)}` : `de ${the}`;
}

/** «timbrada», «timbrado»: a word of a state said of the CFDI, in its gender (given in feminine). */
export function inGender(kind: IssuedCfdiKind, feminine: string): string {
  return isMasculine(kind) ? `${feminine.slice(0, -1)}o` : feminine;
}
