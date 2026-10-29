import { describe, expect, it } from 'vitest';
import { OrderInvoiceStatus } from '../types';
import { CFDI_REGIMES, cfdiUsesFor, invoiceStatusChip, invoiceStatusLabel, isStuckStamping } from './cfdi';

describe('invoice status', () => {
  it('names every status the API sends, with the chip of a status (never the accent)', () => {
    const statuses = Object.values(OrderInvoiceStatus);
    expect(statuses.map(invoiceStatusLabel)).toEqual(['Timbrando', 'Timbrada', 'Rechazada', 'Cancelándose', 'Cancelada']);
    for (const status of statuses) expect(invoiceStatusChip(status)).toMatch(/^ta-chip-/);
  });
});

describe('stamping past the PAC\'s window (Fase 28K)', () => {
  const now = Date.parse('2026-10-26T18:00:00Z');

  it('is only «Timbrando» once the window the API gives ended', () => {
    expect(isStuckStamping({ status: OrderInvoiceStatus.Stamping, stampWindowEndsAt: '2026-10-26T17:59:00Z' }, now)).toBe(true);
    expect(isStuckStamping({ status: OrderInvoiceStatus.Stamping, stampWindowEndsAt: '2026-10-26T18:01:00Z' }, now)).toBe(false);
    // An API before the phase sends no window: what it does not say is not assumed.
    expect(isStuckStamping({ status: OrderInvoiceStatus.Stamping }, now)).toBe(false);
    expect(isStuckStamping({ status: OrderInvoiceStatus.Stamped, stampWindowEndsAt: '2026-10-20T00:00:00Z' }, now)).toBe(false);
  });
});

describe('uses of the CFDI by regime', () => {
  const uses = (regime: string) => cfdiUsesFor(regime).map(([use]) => use);

  it('offers only the uses the SAT lets each regime ask for', () => {
    // A company buys and spends; it has no personal deductions.
    expect(uses('601')).toEqual(['G01', 'G03', 'S01']);
    // A professional has both; a salaried person, only deductions.
    expect(uses('612')).toEqual(['G01', 'G03', 'D01', 'S01']);
    expect(uses('605')).toEqual(['D01', 'S01']);
    expect(uses('626')).toEqual(['G01', 'G03', 'S01']);
  });

  it('leaves out the regime of the general public, whose sales go in the global invoice', () => {
    expect(CFDI_REGIMES.map(([code]) => code)).not.toContain('616');
    for (const [regime] of CFDI_REGIMES) expect(uses(regime)).toContain('S01');
  });
});
