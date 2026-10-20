import { OrderWriteOffReason } from '../types';

// Kept out of labels.ts, which the first visit loads: only Clientes and Pedidos show why a debt was forgiven.

/** Why the business forgave a debt (Fase 26B), in the owner's words, in the order they are offered. */
export const WRITE_OFF_REASONS: ReadonlyArray<{ value: OrderWriteOffReason; label: string }> = [
  { value: OrderWriteOffReason.WontBeCollected, label: 'No se va a cobrar' },
  { value: OrderWriteOffReason.RecordingError, label: 'Error al registrar' },
  { value: OrderWriteOffReason.Courtesy, label: 'Cortesía' },
  { value: OrderWriteOffReason.Other, label: 'Otro' },
];

export function getWriteOffReasonLabel(reason: OrderWriteOffReason | null | undefined): string {
  return WRITE_OFF_REASONS.find((option) => option.value === reason)?.label ?? 'Otro';
}
