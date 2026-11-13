import { afterEach, describe, expect, it, vi } from 'vitest';
import { quoteDepositToken, rememberQuoteDeposit } from './quoteDeposits';

const STORAGE_KEY = 'tienditapp.quoteDeposits';
const NOW = new Date('2026-11-13T18:00:00Z');
const hoursLater = (hours: number) => new Date(NOW.getTime() + hours * 60 * 60 * 1000);

describe('quoteDeposits', () => {
  afterEach(() => {
    window.localStorage.clear();
    vi.restoreAllMocks();
  });

  it('keeps the link of the quote of each deposit this device went to pay, for a day', () => {
    rememberQuoteDeposit('c1', 'enlace-1', NOW);
    rememberQuoteDeposit('c2', 'enlace-2', hoursLater(1));

    expect(quoteDepositToken('c1', hoursLater(23))).toBe('enlace-1');
    expect(quoteDepositToken('c2', hoursLater(2))).toBe('enlace-2');
    expect(quoteDepositToken('c1', hoursLater(24))).toBeNull();
    expect(quoteDepositToken('otro', NOW)).toBeNull();
  });

  it('forgets the ones older than a day and keeps the ten newest', () => {
    for (let i = 0; i < 12; i++) rememberQuoteDeposit(`c${i}`, `enlace-${i}`, new Date(NOW.getTime() + i * 60_000));
    rememberQuoteDeposit('nuevo', 'enlace', new Date(NOW.getTime() + (24 * 60 + 1.5) * 60_000));

    const kept = Object.keys(JSON.parse(window.localStorage.getItem(STORAGE_KEY) ?? '{}'));
    expect(kept).toEqual(['nuevo', 'c11', 'c10', 'c9', 'c8', 'c7', 'c6', 'c5', 'c4', 'c3']);
  });

  it('answers no link when what is stored is not its own, or storage is blocked', () => {
    window.localStorage.setItem(STORAGE_KEY, 'no es json');
    expect(quoteDepositToken('c1', NOW)).toBeNull();
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify({ c1: { token: 7, savedAt: NOW.toISOString() } }));
    expect(quoteDepositToken('c1', NOW)).toBeNull();

    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('QuotaExceededError'); });
    expect(() => rememberQuoteDeposit('c2', 'enlace', NOW)).not.toThrow();
  });
});
