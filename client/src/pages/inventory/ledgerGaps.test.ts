import { describe, expect, it } from 'vitest';
import type { InventoryReconciliationEvent } from '../../api/inventoryReconciliation';
import { InventoryReconciliationEventKind, UnitType } from '../../types';
import { describeLedgerEvent, formatSignedQuantity } from './ledgerGaps';

const event = (overrides: Partial<InventoryReconciliationEvent>) =>
  ({ kind: InventoryReconciliationEventKind.NoHistory, quantity: 0, ...overrides }) as InventoryReconciliationEvent;

describe('ledger gaps', () => {
  it('signs a quantity with its unit', () => {
    expect(formatSignedQuantity(2, UnitType.Unit)).toBe('+2');
    expect(formatSignedQuantity(-0.25, UnitType.Kg)).toBe('−0.25 kg');
  });

  it('says in words where each stock stopped adding up to its history', () => {
    const at = { movementNumber: 'INV-20261021-0007' };
    expect(describeLedgerEvent(event({ kind: InventoryReconciliationEventKind.NoHistory, quantity: 3 }), UnitType.Unit))
      .toBe('Hay 3 sin ningún movimiento: se cargó antes del historial o por fuera de él.');
    expect(describeLedgerEvent(event({ kind: InventoryReconciliationEventKind.BeforeFirstMovement, quantity: 5, quantityFrom: 0, quantityTo: 5, ...at })))
      .toBe('Ya había 5 antes del primer movimiento, INV-20261021-0007.');
    expect(describeLedgerEvent(event({
      kind: InventoryReconciliationEventKind.BetweenMovements, quantity: 3, quantityFrom: 15, quantityTo: 18, previousMovementNumber: 'INV-20261021-0006', ...at,
    }))).toBe('Entre INV-20261021-0006 y INV-20261021-0007 la existencia pasó de 15 a 18 sin un movimiento.');
    expect(describeLedgerEvent(event({
      kind: InventoryReconciliationEventKind.InMovement, quantity: -1, quantityFrom: 16, quantityTo: 14, movementQuantity: -1, ...at,
    }), UnitType.Kg)).toBe('INV-20261021-0007 movió −1 kg, pero la existencia pasó de 16 kg a 14 kg.');
    expect(describeLedgerEvent(event({ kind: InventoryReconciliationEventKind.AfterLastMovement, quantity: 6, quantityFrom: 14, quantityTo: 20, ...at })))
      .toBe('Después de INV-20261021-0007 la existencia pasó de 14 a 20 sin un movimiento.');
    expect(describeLedgerEvent(event({ kind: InventoryReconciliationEventKind.Reconciled, quantity: -6, quantityFrom: 30, quantityTo: 30, movementQuantity: 6, ...at })))
      .toBe('INV-20261021-0007 cuadró el historial: anotó +6 sin mover la existencia.');
    expect(describeLedgerEvent(event({ kind: InventoryReconciliationEventKind.BeforeReviewedHistory, quantity: 2, ...at })))
      .toBe('Viene de antes de INV-20261021-0007: se revisaron los movimientos más recientes.');
  });
});
