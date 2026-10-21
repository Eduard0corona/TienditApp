import { PlatformSettlementAdjustmentKind, PlatformSettlementStatus, PlatformStatementMatchStatus } from '../../types';

// Reportes › Liquidaciones only, out of utils/labels.ts, which the first visit loads. Every value of the contract has its
// label: a value the API adds does not build until it has one.

/** A settlement of a platform's deposit: open while it is reconciled, closed once it balanced, voided as history. */
const STATUS: Record<PlatformSettlementStatus, { label: string; chip: string }> = {
  [PlatformSettlementStatus.Open]: { label: 'Abierta', chip: 'ta-chip-new' },
  [PlatformSettlementStatus.Closed]: { label: 'Cerrada', chip: 'ta-chip-done' },
  [PlatformSettlementStatus.Voided]: { label: 'Anulada', chip: 'ta-chip-cancelled' },
};

export function getSettlementStatusLabel(status: PlatformSettlementStatus): string {
  return STATUS[status]?.label ?? String(status);
}

/** Chip class of a settlement status (ta-chip-*), always with its text. */
export function getSettlementStatusChip(status: PlatformSettlementStatus): string {
  return STATUS[status]?.chip ?? 'ta-chip-done';
}

/** Why a deposit differs from its orders, in the order the list offers them. */
export const ADJUSTMENT_KIND_LABELS: Record<PlatformSettlementAdjustmentKind, string> = {
  [PlatformSettlementAdjustmentKind.Promotion]: 'Promociones',
  [PlatformSettlementAdjustmentKind.Claim]: 'Faltantes y reclamos',
  [PlatformSettlementAdjustmentKind.Cancellation]: 'Cancelaciones',
  [PlatformSettlementAdjustmentKind.Fee]: 'Cargos de la plataforma',
  [PlatformSettlementAdjustmentKind.PreviousBalance]: 'Saldo anterior',
  [PlatformSettlementAdjustmentKind.Other]: 'Otro',
};

/**
 * What a folio of a platform's statement is in TienditApp (Fase 22H), as the comparison lists the ones the settlement does
 * not take; the two it takes («En esta liquidación», «Pendiente») say it too.
 */
export const STATEMENT_STATUS_LABELS: Record<PlatformStatementMatchStatus, string> = {
  [PlatformStatementMatchStatus.InThisSettlement]: 'Ya están en esta liquidación',
  [PlatformStatementMatchStatus.Pending]: 'Pendientes: entran a la liquidación',
  [PlatformStatementMatchStatus.AfterPeriod]: 'Se registraron después del periodo',
  [PlatformStatementMatchStatus.Settled]: 'Ya están en otra liquidación',
  [PlatformStatementMatchStatus.NothingExpected]: 'No traen nada: devueltos o sin cobrar',
  [PlatformStatementMatchStatus.Ambiguous]: 'Varios pedidos con el mismo folio: elige cuál en la lista',
  [PlatformStatementMatchStatus.Unknown]: 'No son pedidos de la plataforma en TienditApp',
};
