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
