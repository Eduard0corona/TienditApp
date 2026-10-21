import type { InventoryReconciliationEvent } from '../../api/inventoryReconciliation';
import { InventoryReconciliationEventKind, type UnitType } from '../../types';
import { formatStockQuantity } from '../../utils/units';
import { formatDateTime } from './inventoryLabels';

/** "+2", "−0.25 kg": what a gap adds to the stock, with its sign. */
export const formatSignedQuantity = (quantity: number, unit?: UnitType | null) =>
  `${quantity > 0 ? '+' : '−'}${formatStockQuantity(Math.abs(quantity), unit)}`;

const at = (number?: string | null, date?: string | null) => (date ? `${number} (${formatDateTime(date)})` : `${number}`);

/**
 * One place of the history in words (Fase 27C): where the stock changed without a movement, a movement that wrote its
 * balance wrong, or a reconciliation that recorded what was missing.
 */
export function describeLedgerEvent(event: InventoryReconciliationEvent, unit?: UnitType | null): string {
  const quantity = (value?: number | null) => formatStockQuantity(value ?? 0, unit);
  switch (event.kind) {
    case InventoryReconciliationEventKind.NoHistory:
      return `Hay ${quantity(event.quantity)} sin ningún movimiento: se cargó antes del historial o por fuera de él.`;
    case InventoryReconciliationEventKind.BeforeFirstMovement:
      return `Ya había ${quantity(event.quantityTo)} antes del primer movimiento, ${at(event.movementNumber, event.movementDate)}.`;
    case InventoryReconciliationEventKind.BetweenMovements:
      return `Entre ${event.previousMovementNumber} y ${at(event.movementNumber, event.movementDate)} la existencia pasó de ${quantity(event.quantityFrom)} a ${quantity(event.quantityTo)} sin un movimiento.`;
    case InventoryReconciliationEventKind.InMovement:
      return `${at(event.movementNumber, event.movementDate)} movió ${formatSignedQuantity(event.movementQuantity ?? 0, unit)}, pero la existencia pasó de ${quantity(event.quantityFrom)} a ${quantity(event.quantityTo)}.`;
    case InventoryReconciliationEventKind.AfterLastMovement:
      return `Después de ${at(event.movementNumber, event.movementDate)} la existencia pasó de ${quantity(event.quantityFrom)} a ${quantity(event.quantityTo)} sin un movimiento.`;
    case InventoryReconciliationEventKind.Reconciled:
      return `${at(event.movementNumber, event.movementDate)} cuadró el historial: anotó ${formatSignedQuantity(event.movementQuantity ?? 0, unit)} sin mover la existencia.`;
    case InventoryReconciliationEventKind.BeforeReviewedHistory:
      return `Viene de antes de ${at(event.movementNumber, event.movementDate)}: se revisaron los movimientos más recientes.`;
    default:
      return 'Aquí la existencia y el historial se separan.';
  }
}
