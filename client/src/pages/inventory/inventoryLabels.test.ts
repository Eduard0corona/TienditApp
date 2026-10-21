import { describe, expect, it } from 'vitest';
import { InventoryMovementReason, InventoryMovementType } from '../../types';
import {
  MANUAL_MOVEMENT_REASON_OPTIONS,
  MANUAL_MOVEMENT_TYPE_OPTIONS,
  MOVEMENT_REASON_OPTIONS,
  MOVEMENT_TYPE_OPTIONS,
  getMovementReasonLabel,
} from './inventoryLabels';

describe('movement options', () => {
  it('leave the legs of a transfer out of a manual movement, and keep them in the history filters', () => {
    const manualTypes = MANUAL_MOVEMENT_TYPE_OPTIONS.map((option) => option.value);
    expect(manualTypes).not.toContain(InventoryMovementType.TransferIn);
    expect(manualTypes).not.toContain(InventoryMovementType.TransferOut);
    expect(manualTypes).toHaveLength(MOVEMENT_TYPE_OPTIONS.length - 2);
    expect(MANUAL_MOVEMENT_REASON_OPTIONS.map((option) => option.value)).not.toContain(InventoryMovementReason.Transfer);
    expect(MOVEMENT_REASON_OPTIONS.map((option) => option.value)).toContain(InventoryMovementReason.Transfer);
    expect(getMovementReasonLabel(InventoryMovementReason.Transfer)).toBe('Transferencia');
  });

  it('leave the reconciliation of the history out of a manual movement, and keep it in the history filters', () => {
    expect(MANUAL_MOVEMENT_REASON_OPTIONS.map((option) => option.value)).not.toContain(InventoryMovementReason.LedgerReconciliation);
    expect(MOVEMENT_REASON_OPTIONS.map((option) => option.value)).toContain(InventoryMovementReason.LedgerReconciliation);
    expect(getMovementReasonLabel(InventoryMovementReason.LedgerReconciliation)).toBe('Cuadre del historial');
  });
});
