const MONEY_FORMAT = new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' });

/** Amount with currency symbol and thousands separator ($1,234.50). */
export function formatMoney(amount: number): string {
  return MONEY_FORMAT.format(amount);
}

/** Signed amount for money movements: +$100.00 entering, −$50.00 leaving. */
export function formatSignedMoney(amount: number): string {
  if (amount === 0) return MONEY_FORMAT.format(0);
  return `${amount > 0 ? '+' : '−'}${MONEY_FORMAT.format(Math.abs(amount))}`;
}

/**
 * Parses an amount typed by a person ("1,234.50", "$80", "80.5") into a number with at most two
 * decimals. Returns null when it is not a valid non-negative amount.
 */
export function parseMoneyInput(value: string): number | null {
  const cleaned = value.replace(/[$\s,]/g, '');
  if (!/^\d+(\.\d{0,2})?$/.test(cleaned)) return null;
  return Number(cleaned);
}

/** Whole cents of an amount that comes with at most two decimals. */
export function toCents(amount: number): number {
  return Math.round(amount * 100);
}
