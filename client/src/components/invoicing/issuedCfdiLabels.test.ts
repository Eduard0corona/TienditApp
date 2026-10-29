import { describe, expect, it } from 'vitest';
import { IssuedCfdiKind } from '../../types';
import { inGender, kindLabel, ofDocument, theDocument } from './issuedCfdiLabels';

// A kind a newer API may send before this version knows it.
const UNKNOWN = 99 as IssuedCfdiKind;

describe('issuedCfdiLabels', () => {
  it('names each kind, and a kind it does not know yet as «CFDI»', () => {
    expect(kindLabel(IssuedCfdiKind.OrderInvoice)).toBe('Factura');
    expect(kindLabel(IssuedCfdiKind.GlobalInvoice)).toBe('Factura global');
    expect(kindLabel(IssuedCfdiKind.PaymentComplement)).toBe('Complemento de pago');
    expect(kindLabel(IssuedCfdiKind.CreditNote)).toBe('Nota de crédito');
    expect(kindLabel(UNKNOWN)).toBe('CFDI');
  });

  it('says the CFDI of a sentence or a button with its article', () => {
    expect(theDocument({ kind: IssuedCfdiKind.OrderInvoice, number: 'F-1' })).toBe('la factura F-1');
    expect(theDocument({ kind: IssuedCfdiKind.GlobalInvoice, number: 'F-2' })).toBe('la factura global F-2');
    expect(theDocument({ kind: IssuedCfdiKind.PaymentComplement, number: 'P-1' })).toBe('el complemento de pago P-1');
    expect(theDocument({ kind: IssuedCfdiKind.CreditNote, number: 'NC-1' })).toBe('la nota de crédito NC-1');
    expect(theDocument({ kind: UNKNOWN, number: 'X-1' })).toBe('el CFDI X-1');
  });

  it('joins «de» and «el» as Spanish does', () => {
    expect(ofDocument({ kind: IssuedCfdiKind.OrderInvoice, number: 'F-1' })).toBe('de la factura F-1');
    expect(ofDocument({ kind: IssuedCfdiKind.PaymentComplement, number: 'P-1' })).toBe('del complemento de pago P-1');
    expect(ofDocument({ kind: UNKNOWN, number: 'X-1' })).toBe('del CFDI X-1');
  });

  it('says a state in the gender of the kind', () => {
    expect(inGender(IssuedCfdiKind.CreditNote, 'rechazada')).toBe('rechazada');
    expect(inGender(IssuedCfdiKind.PaymentComplement, 'rechazada')).toBe('rechazado');
    expect(inGender(UNKNOWN, 'timbrada')).toBe('timbrado');
    expect(inGender(IssuedCfdiKind.PaymentComplement, 'la')).toBe('lo');
  });
});
