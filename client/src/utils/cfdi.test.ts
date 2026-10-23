import { describe, expect, it } from 'vitest';
import { OrderInvoiceStatus } from '../types';
import { invoiceStatusChip, invoiceStatusLabel } from './cfdi';

describe('invoice status', () => {
  it('names every status the API sends, with the chip of a status (never the accent)', () => {
    const statuses = Object.values(OrderInvoiceStatus);
    expect(statuses.map(invoiceStatusLabel)).toEqual(['Timbrando', 'Timbrada', 'Rechazada', 'Cancelándose', 'Cancelada']);
    for (const status of statuses) expect(invoiceStatusChip(status)).toMatch(/^ta-chip-/);
  });
});
