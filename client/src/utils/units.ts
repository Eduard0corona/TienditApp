// How each unit is sold (UnitTypeRules in the API): weight, volume, length and time admit up to three
// decimals; pieces, packs and portions are whole. Line amounts are rounded once to cents with banker's
// rounding, like OrderItem.TotalPrice.
import { UnitType } from '../types';

export const QUANTITY_DECIMALS = 3;

const FRACTIONAL_UNITS: ReadonlySet<number> = new Set([
  UnitType.Kg,
  UnitType.G,
  UnitType.Lb,
  UnitType.Oz,
  UnitType.Liter,
  UnitType.Ml,
  UnitType.Meter,
  UnitType.Hour,
]);

const SHORT_LABELS: Partial<Record<UnitType, string>> = {
  [UnitType.Kg]: 'kg',
  [UnitType.G]: 'g',
  [UnitType.Lb]: 'lb',
  [UnitType.Oz]: 'oz',
  [UnitType.Liter]: 'L',
  [UnitType.Ml]: 'ml',
  [UnitType.Meter]: 'm',
  [UnitType.Hour]: 'h',
};

/** CFDI ClaveUnidad a unit gets when the product has none (UnitTypeRules.DefaultSatUnitCode). */
export function getDefaultSatUnitCode(unit?: UnitType | null): string {
  switch (unit) {
    case UnitType.Pack: return 'XPK';
    case UnitType.Box: return 'XBX';
    case UnitType.Pair: return 'PR';
    case UnitType.Kg: return 'KGM';
    case UnitType.G: return 'GRM';
    case UnitType.Lb: return 'LBR';
    case UnitType.Oz: return 'ONZ';
    case UnitType.Liter: return 'LTR';
    case UnitType.Ml: return 'MLT';
    case UnitType.Meter: return 'MTR';
    case UnitType.Roll: return 'XRO';
    case UnitType.Hour: return 'HUR';
    case UnitType.Session:
    case UnitType.Service:
      return 'E48';
    default:
      return 'H87';
  }
}

export function allowsFractions(unit?: UnitType | null): boolean {
  return unit !== undefined && unit !== null && FRACTIONAL_UNITS.has(unit);
}

/** "kg", "L", "h"... for measured units; empty for pieces. */
export function getUnitShortLabel(unit?: UnitType | null): string {
  return unit === undefined || unit === null ? '' : SHORT_LABELS[unit] ?? '';
}

/** What the + and − buttons add or take: a tenth of a kilo or liter, 50 g, half an hour, one piece. */
export function getQuantityStep(unit?: UnitType | null): number {
  switch (unit) {
    case UnitType.Kg:
    case UnitType.Lb:
    case UnitType.Liter:
    case UnitType.Meter:
      return 0.1;
    case UnitType.G:
    case UnitType.Ml:
      return 50;
    case UnitType.Hour:
      return 0.5;
    default:
      return 1;
  }
}

/** Rounds to the three decimals a quantity keeps, avoiding floating point leftovers (0.1 + 0.2). */
export function roundQuantity(quantity: number): number {
  return Math.round(quantity * 10 ** QUANTITY_DECIMALS) / 10 ** QUANTITY_DECIMALS;
}

/** "1.255 kg", "0.5 L" or "3" (pieces). */
export function formatQuantity(quantity: number, unit?: UnitType | null): string {
  const number = quantity.toLocaleString('es-MX', { maximumFractionDigits: QUANTITY_DECIMALS });
  const label = getUnitShortLabel(unit);
  return label ? `${number} ${label}` : number;
}

/**
 * Parses what the cashier typed ("1.25", "1,25", ".5"). Null when it is not a valid quantity for the unit:
 * not positive, more than three decimals, or a fraction of a piece.
 */
export function parseQuantity(text: string, unit?: UnitType | null): number | null {
  const normalized = text.trim().replace(',', '.');
  if (!/^\d*\.?\d+$|^\d+\.$/.test(normalized)) return null;
  const value = Number(normalized);
  if (!Number.isFinite(value) || value <= 0) return null;
  if (roundQuantity(value) !== value) return null;
  if (!allowsFractions(unit) && !Number.isInteger(value)) return null;
  return value;
}

/** numerator / denominator rounded half to even, both non-negative integers. */
function divideRoundingHalfEven(numerator: number, denominator: number): number {
  const quotient = Math.floor(numerator / denominator);
  const twiceRemainder = 2 * (numerator - quotient * denominator);
  if (twiceRemainder > denominator) return quotient + 1;
  if (twiceRemainder < denominator) return quotient;
  return quotient % 2 === 0 ? quotient : quotient + 1;
}

/** Amount of a line in cents: quantity × price rounded once, half to even. */
export function lineAmountCents(unitPrice: number, quantity: number): number {
  const priceCents = Math.round(unitPrice * 100);
  const thousandths = Math.round(quantity * 1000);
  return divideRoundingHalfEven(priceCents * thousandths, 1000);
}

export function lineAmount(unitPrice: number, quantity: number): number {
  return lineAmountCents(unitPrice, quantity) / 100;
}
