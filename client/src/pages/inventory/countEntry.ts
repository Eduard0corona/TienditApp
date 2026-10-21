import type { UnitType } from '../../types';
import { allowsFractions, formatStockQuantity, roundQuantity } from '../../utils/units';

// What a person types in the sheet of a physical count (Fase 27A), checked as the API checks it
// (InventoryCount.IsValidCountedQuantity): zero or more, up to 9,999,999, three decimals at most and whole for
// pieces. The API checks it again and decides the differences; this only says it before saving.

export const MAX_COUNTED_QUANTITY = 9_999_999;

export type CountedQuantity = { value: number | null } | { error: string };

export function countedQuantityMessage(unit?: UnitType | null): string {
  return allowsFractions(unit)
    ? 'La cantidad contada va de 0 a 9,999,999, con máximo 3 decimales.'
    : 'La cantidad contada va de 0 a 9,999,999, en piezas enteras.';
}

/** Empty leaves the item uncounted; "0" says there is none. */
export function parseCountedQuantity(text: string, unit?: UnitType | null): CountedQuantity {
  const normalized = text.trim().replace(',', '.');
  if (normalized === '') return { value: null };
  if (!/^\d*\.?\d+$|^\d+\.$/.test(normalized)) return { error: countedQuantityMessage(unit) };

  const value = Number(normalized);
  const valid = Number.isFinite(value)
    && value <= MAX_COUNTED_QUANTITY
    && roundQuantity(value) === value
    && (allowsFractions(unit) || Number.isInteger(value));
  return valid ? { value } : { error: countedQuantityMessage(unit) };
}

/** "+0.5 kg", "−2" or "0": what was found over (+) or missing from (−) what the system had. */
export function formatDifference(difference: number, unit?: UnitType | null): string {
  if (difference === 0) return formatStockQuantity(0, unit);
  return `${difference > 0 ? '+' : '−'}${formatStockQuantity(Math.abs(difference), unit)}`;
}
