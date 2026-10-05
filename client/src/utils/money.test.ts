import { describe, expect, it } from 'vitest';
import { formatMoney, formatSignedMoney, formatUnitCost, parseMoneyInput, toCents } from './money';

// Intl separates the symbol with a non-breaking space in some runtimes; compare without it.
const plain = (text: string) => text.replace(/\u00a0/g, ' ');

describe('money', () => {
  it('formats pesos with thousands separators and two decimals', () => {
    expect(plain(formatMoney(1234.5))).toBe('$1,234.50');
    expect(plain(formatMoney(0))).toBe('$0.00');
  });

  it('keeps up to four decimals in unit costs', () => {
    expect(plain(formatUnitCost(12.3456))).toBe('$12.3456');
    expect(plain(formatUnitCost(12.5))).toBe('$12.50');
  });

  it('signs money movements', () => {
    expect(plain(formatSignedMoney(100))).toBe('+$100.00');
    expect(plain(formatSignedMoney(-50))).toBe('−$50.00');
  });

  it('reads what a person types and refuses the rest', () => {
    expect(parseMoneyInput('$1,234.50')).toBe(1234.5);
    expect(parseMoneyInput('80')).toBe(80);
    expect(parseMoneyInput('80.555')).toBeNull();
    expect(parseMoneyInput('-5')).toBeNull();
    expect(parseMoneyInput('abc')).toBeNull();
  });

  it('counts whole cents without floating point drift', () => {
    expect(toCents(0.1 + 0.2)).toBe(30);
    expect(toCents(19.99)).toBe(1999);
  });
});
