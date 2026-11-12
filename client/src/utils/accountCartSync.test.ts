import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { accountApi } from '../api/accountApi';
import { ACCOUNT_CART_DELAY_MS, cartSignature, scheduleAccountCartSave } from './accountCartSync';

vi.mock('../api/accountApi', () => ({ accountApi: { saveCart: vi.fn() } }));

const api = vi.mocked(accountApi);
const line = (quantity: number) => ({ productId: 'taco', optionIds: [], quantity });

/** Lets the promises that were resolved run. */
const settle = () => vi.advanceTimersByTimeAsync(0);

describe('accountCartSync', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    api.saveCart.mockReset();
  });
  afterEach(() => vi.useRealTimers());

  it('sends the cart a moment after its last change, once', async () => {
    api.saveCart.mockResolvedValue({} as never);

    scheduleAccountCartSave('demo', { lines: [line(1)] });
    scheduleAccountCartSave('demo', { lines: [line(2)] });
    await vi.advanceTimersByTimeAsync(ACCOUNT_CART_DELAY_MS - 1);
    expect(api.saveCart).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1);

    expect(api.saveCart).toHaveBeenCalledTimes(1);
    expect(api.saveCart).toHaveBeenCalledWith('demo', { lines: [line(2)], reorderOf: null, skipped: null });
  });

  it('sends one at a time and the last one last, so the account never ends with an older cart', async () => {
    let finishFirst: () => void = () => undefined;
    api.saveCart
      .mockImplementationOnce(() => new Promise((resolve) => { finishFirst = () => resolve({} as never); }))
      .mockResolvedValue({} as never);

    scheduleAccountCartSave('demo', { lines: [line(1)] });
    await vi.advanceTimersByTimeAsync(ACCOUNT_CART_DELAY_MS);
    // The order was sent while the first save went: the cart is emptied.
    scheduleAccountCartSave('demo', { lines: [] });
    await vi.advanceTimersByTimeAsync(ACCOUNT_CART_DELAY_MS);
    expect(api.saveCart).toHaveBeenCalledTimes(1);

    finishFirst();
    await settle();
    expect(api.saveCart).toHaveBeenCalledTimes(2);
    expect(api.saveCart).toHaveBeenLastCalledWith('demo', { lines: [], reorderOf: null, skipped: null });
  });

  it('drops a save that fails: the device keeps the cart, and the next change sends it', async () => {
    api.saveCart.mockRejectedValueOnce(new Error('offline')).mockResolvedValue({} as never);

    scheduleAccountCartSave('demo', { lines: [line(1)] });
    await vi.advanceTimersByTimeAsync(ACCOUNT_CART_DELAY_MS);
    scheduleAccountCartSave('demo', { lines: [line(3)] });
    await vi.advanceTimersByTimeAsync(ACCOUNT_CART_DELAY_MS);

    expect(api.saveCart).toHaveBeenCalledTimes(2);
    expect(api.saveCart).toHaveBeenLastCalledWith('demo', { lines: [line(3)], reorderOf: null, skipped: null });
  });

  it('keeps each store apart, and says when a cart is the same', async () => {
    api.saveCart.mockResolvedValue({} as never);

    scheduleAccountCartSave('demo', { lines: [line(1)] });
    scheduleAccountCartSave('cafe', { lines: [line(2)], reorderOf: 4 });
    await vi.advanceTimersByTimeAsync(ACCOUNT_CART_DELAY_MS);

    expect(api.saveCart).toHaveBeenCalledWith('demo', { lines: [line(1)], reorderOf: null, skipped: null });
    expect(api.saveCart).toHaveBeenCalledWith('cafe', { lines: [line(2)], reorderOf: 4, skipped: null });
    expect(cartSignature({ lines: [line(1)] })).toBe(cartSignature({ lines: [line(1)] }));
    expect(cartSignature({ lines: [line(1)] })).not.toBe(cartSignature({ lines: [line(2)] }));
  });
});
