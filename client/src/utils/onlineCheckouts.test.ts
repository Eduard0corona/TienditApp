import { afterEach, describe, expect, it, vi } from 'vitest';
import { onlineCheckoutReturnKey, rememberOnlineCheckout } from './onlineCheckouts';

const STORAGE_KEY = 'tienditapp.onlineCheckouts';
const NOW = new Date('2026-10-19T18:00:00Z');
const hoursLater = (hours: number) => new Date(NOW.getTime() + hours * 60 * 60 * 1000);

describe('onlineCheckouts', () => {
  afterEach(() => {
    window.localStorage.clear();
    vi.restoreAllMocks();
  });

  it('keeps the return key of each payment started on this device for a day', () => {
    rememberOnlineCheckout('c1', 'llave-1', NOW);
    rememberOnlineCheckout('c2', 'llave-2', hoursLater(1));

    expect(onlineCheckoutReturnKey('c1', hoursLater(23))).toBe('llave-1');
    expect(onlineCheckoutReturnKey('c2', hoursLater(2))).toBe('llave-2');
    expect(onlineCheckoutReturnKey('c1', hoursLater(24))).toBeNull();
    expect(onlineCheckoutReturnKey('otro', NOW)).toBeNull();
  });

  it('forgets the ones older than a day and keeps the ten newest', () => {
    for (let i = 0; i < 12; i++) rememberOnlineCheckout(`c${i}`, `llave-${i}`, new Date(NOW.getTime() + i * 60_000));
    rememberOnlineCheckout('nuevo', 'llave', new Date(NOW.getTime() + (24 * 60 + 1.5) * 60_000));

    const kept = Object.keys(JSON.parse(window.localStorage.getItem(STORAGE_KEY) ?? '{}'));
    // c0 and c1 were saved more than a day before; of the eleven left, the oldest (c2) goes.
    expect(kept).toEqual(['nuevo', 'c11', 'c10', 'c9', 'c8', 'c7', 'c6', 'c5', 'c4', 'c3']);
  });

  it('answers no key when what is stored is not its own, or storage is blocked', () => {
    window.localStorage.setItem(STORAGE_KEY, 'no es json');
    expect(onlineCheckoutReturnKey('c1', NOW)).toBeNull();
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify({ c1: { returnKey: 7, savedAt: NOW.toISOString() } }));
    expect(onlineCheckoutReturnKey('c1', NOW)).toBeNull();

    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('QuotaExceededError'); });
    expect(() => rememberOnlineCheckout('c2', 'llave', NOW)).not.toThrow();
    expect(onlineCheckoutReturnKey('c2', NOW)).toBeNull();
  });
});
