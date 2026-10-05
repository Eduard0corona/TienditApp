import { describe, expect, it } from 'vitest';
import type { PurchaseOrderDto, PurchaseOrderLineDto } from '../types';
import { PurchaseOrderStatus, UnitType } from '../types';
import { buildPurchaseOrderMessage, formatOrderedQuantity } from './purchaseOrderMessage';

const plain = (text: string) => text.replace(/\u00a0/g, ' ');

let nextId = 0;
const id = () => `id-${++nextId}`;

const line = (patch: Partial<PurchaseOrderLineDto>): PurchaseOrderLineDto => ({
  id: id(),
  supplyItemId: id(),
  supplyItemName: 'Tomate',
  unit: UnitType.Box,
  inventoryLocationId: id(),
  inventoryLocationName: 'Bodega',
  quantity: 3,
  receivedQuantity: 0,
  pendingQuantity: 3,
  unitCost: 150,
  taxPercent: 0,
  subTotal: 450,
  taxAmount: 0,
  total: 450,
  ...patch,
});

const order = (patch: Partial<PurchaseOrderDto>): PurchaseOrderDto => ({
  id: id(),
  supplierId: id(),
  supplierName: 'Abarrotes López',
  supplierPhone: '5512345678',
  orderNumber: 'OC-000012',
  status: PurchaseOrderStatus.Draft,
  orderedAt: '2026-10-05T15:00:00Z',
  subTotal: 1250,
  tax: 0,
  total: 1250,
  lines: [
    line({}),
    line({ supplyItemName: 'Queso Oaxaca', unit: UnitType.Kg, quantity: 2.5, pendingQuantity: 2.5, notes: 'Fresco' }),
  ],
  ...patch,
});

describe('purchase order message', () => {
  it('writes each quantity with the unit the supply is bought in', () => {
    expect(formatOrderedQuantity(1, UnitType.Box)).toBe('1 caja');
    expect(formatOrderedQuantity(3, UnitType.Box)).toBe('3 cajas');
    expect(formatOrderedQuantity(2.5, UnitType.Kg)).toBe('2.5 kg');
    expect(formatOrderedQuantity(12, UnitType.Unit)).toBe('12 piezas');
  });

  it('lists the whole order with its total for the supplier', () => {
    const text = plain(buildPurchaseOrderMessage(order({ referenceNumber: 'COT-7' }), 'La Esquina'));

    expect(text).toContain('*Orden de compra OC-000012*\nLa Esquina');
    expect(text).toContain('Hola, Abarrotes López. Te pedimos:');
    expect(text).toContain('• Tomate: 3 cajas');
    expect(text).toContain('• Queso Oaxaca: 2.5 kg\n   _Fresco_');
    expect(text).toContain('Referencia: COT-7');
    expect(text).toContain('Total: $1,250.00');
  });

  it('asks only for what is missing once part of the order arrived', () => {
    const text = plain(buildPurchaseOrderMessage(order({
      status: PurchaseOrderStatus.PartiallyReceived,
      lines: [
        line({ receivedQuantity: 2, pendingQuantity: 1 }),
        line({ supplyItemName: 'Queso Oaxaca', unit: UnitType.Kg, quantity: 2.5, receivedQuantity: 2.5, pendingQuantity: 0 }),
      ],
    })));

    expect(text).toContain('(pendiente)');
    expect(text).toContain('Aún falta por entregar:\n• Tomate: 1 caja');
    expect(text).not.toContain('Queso Oaxaca');
    expect(text).not.toContain('Total:');
  });
});
