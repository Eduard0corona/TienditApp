import type { InventoryReconciliationEvent } from '../../api/inventoryReconciliation';
import type { InventoryReconciliationEventKind, UnitType } from '../../types';
import { formatStockQuantity } from '../../utils/units';
import { formatDateTime } from './inventoryLabels';

/** "+2", "−0.25 kg": what a gap adds to the stock, with its sign. */
export const formatSignedQuantity = (quantity: number, unit?: UnitType | null) =>
  `${quantity > 0 ? '+' : '−'}${formatStockQuantity(Math.abs(quantity), unit)}`;

const at = (number?: string | null, date?: string | null) => (date ? `${number} (${formatDateTime(date)})` : `${number}`);

type Describe = (event: InventoryReconciliationEvent, quantity: (value?: number | null) => string, unit?: UnitType | null) => string;

// Each kind of place in words, by the number the contract gives it. The import of the kinds is a type, so the enum stays
// out of the first visit, and a kind the API adds does not build until it has its words here.
const DESCRIPTIONS: Record<InventoryReconciliationEventKind, Describe> = {
  // NoHistory
  1: (event, quantity) => `Hay ${quantity(event.quantity)} sin ningún movimiento: se cargó antes del historial o por fuera de él.`,
  // BeforeFirstMovement
  2: (event, quantity) => `Ya había ${quantity(event.quantityTo)} antes del primer movimiento, ${at(event.movementNumber, event.movementDate)}.`,
  // BetweenMovements
  3: (event, quantity) =>
    `Entre ${event.previousMovementNumber} y ${at(event.movementNumber, event.movementDate)} la existencia pasó de ${quantity(event.quantityFrom)} a ${quantity(event.quantityTo)} sin un movimiento.`,
  // InMovement
  4: (event, quantity, unit) =>
    `${at(event.movementNumber, event.movementDate)} movió ${formatSignedQuantity(event.movementQuantity ?? 0, unit)}, pero la existencia pasó de ${quantity(event.quantityFrom)} a ${quantity(event.quantityTo)}.`,
  // AfterLastMovement
  5: (event, quantity) =>
    `Después de ${at(event.movementNumber, event.movementDate)} la existencia pasó de ${quantity(event.quantityFrom)} a ${quantity(event.quantityTo)} sin un movimiento.`,
  // Reconciled
  6: (event, _quantity, unit) =>
    `${at(event.movementNumber, event.movementDate)} cuadró el historial: anotó ${formatSignedQuantity(event.movementQuantity ?? 0, unit)} sin mover la existencia.`,
  // BeforeReviewedHistory
  7: (event) => `Viene de antes de ${at(event.movementNumber, event.movementDate)}: se revisaron los movimientos más recientes.`,
};

/**
 * One place of the history in words (Fase 27C): where the stock changed without a movement, a movement that wrote its
 * balance wrong, or a reconciliation that recorded what was missing.
 */
export function describeLedgerEvent(event: InventoryReconciliationEvent, unit?: UnitType | null): string {
  const describe = DESCRIPTIONS[event.kind];
  return describe ? describe(event, (value) => formatStockQuantity(value ?? 0, unit), unit) : 'Aquí la existencia y el historial se separan.';
}
