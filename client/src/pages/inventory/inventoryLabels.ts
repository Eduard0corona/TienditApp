import { InventoryCountStatus, InventoryMovementReason, InventoryMovementType, UnitType } from '../../types';
import { formatInBusinessZone } from '../../utils/dates';
import { UNIT_LABELS } from '../../utils/units';

// Every value of the contract has its label: a value the API adds does not build until it has one.
const MOVEMENT_TYPE_LABELS: Record<InventoryMovementType, string> = {
  [InventoryMovementType.Entry]: 'Entrada',
  [InventoryMovementType.Exit]: 'Salida',
  [InventoryMovementType.AdjustmentIncrease]: 'Ajuste (+)',
  [InventoryMovementType.AdjustmentDecrease]: 'Ajuste (-)',
  [InventoryMovementType.TransferIn]: 'Transferencia entrada',
  [InventoryMovementType.TransferOut]: 'Transferencia salida',
  [InventoryMovementType.ReturnFromProduction]: 'Devolución producción',
  [InventoryMovementType.ReturnToSupplier]: 'Devolución proveedor',
};

const MOVEMENT_REASON_LABELS: Record<InventoryMovementReason, string> = {
  [InventoryMovementReason.Purchase]: 'Compra',
  [InventoryMovementReason.Sale]: 'Venta',
  [InventoryMovementReason.Production]: 'Producción',
  [InventoryMovementReason.Consumption]: 'Consumo',
  [InventoryMovementReason.Waste]: 'Merma',
  [InventoryMovementReason.Damaged]: 'Dañado',
  [InventoryMovementReason.InventoryCount]: 'Conteo físico',
  [InventoryMovementReason.InitialLoad]: 'Carga inicial',
  [InventoryMovementReason.Return]: 'Devolución',
  [InventoryMovementReason.ManualAdjustment]: 'Ajuste manual',
  [InventoryMovementReason.Other]: 'Otro',
};

const COUNT_STATUS_LABELS: Record<InventoryCountStatus, string> = {
  [InventoryCountStatus.InProgress]: 'Abierto',
  [InventoryCountStatus.Posted]: 'Aplicado',
  [InventoryCountStatus.Cancelled]: 'Cancelado',
};

// Open is work going on (blue), posted is done (green), cancelled is red; always with text.
const COUNT_STATUS_CHIPS: Record<InventoryCountStatus, string> = {
  [InventoryCountStatus.InProgress]: 'ta-chip-confirmed',
  [InventoryCountStatus.Posted]: 'ta-chip-ready',
  [InventoryCountStatus.Cancelled]: 'ta-chip-cancelled',
};

const toOptions = <T extends number>(labels: Record<T, string>, values: readonly T[]) =>
  values.map((value) => ({ value, label: labels[value] }));

export const UNIT_OPTIONS = toOptions(UNIT_LABELS, Object.values(UnitType));
export const MOVEMENT_TYPE_OPTIONS = toOptions(MOVEMENT_TYPE_LABELS, Object.values(InventoryMovementType));
export const MOVEMENT_REASON_OPTIONS = toOptions(MOVEMENT_REASON_LABELS, Object.values(InventoryMovementReason));

export const COUNT_STATUS_OPTIONS = toOptions(COUNT_STATUS_LABELS, Object.values(InventoryCountStatus));

export const getCountStatusLabel = (status: InventoryCountStatus) => COUNT_STATUS_LABELS[status] ?? String(status);

export const getCountStatusChip = (status: InventoryCountStatus) => COUNT_STATUS_CHIPS[status] ?? 'ta-chip-done';

export const getMovementTypeLabel = (movementType: InventoryMovementType) =>
  MOVEMENT_TYPE_LABELS[movementType] ?? String(movementType);

export const getMovementReasonLabel = (movementReason: InventoryMovementReason) =>
  MOVEMENT_REASON_LABELS[movementReason] ?? String(movementReason);

/** "04/10/2026, 12:30 p.m." in the zone of the business. */
export const formatDateTime = (value?: string) => {
  if (!value) return '-';
  return formatInBusinessZone(value, {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
};

/** "4/10/2026" in the zone of the business. */
export const formatDate = (value?: string) => {
  if (!value) return '-';
  return formatInBusinessZone(value, { day: 'numeric', month: 'numeric', year: 'numeric' });
};
