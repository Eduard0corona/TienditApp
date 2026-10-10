import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CHECK_EVERY_MS, MIN_GAP_MS, useNewVersion } from './useNewVersion';

const page = (build: string) => new Response(`<script type="module" crossorigin src="/assets/index-${build}.js"></script>`);

describe('useNewVersion', () => {
  let visibility: DocumentVisibilityState;

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] });
    document.head.innerHTML = '<script type="module" crossorigin src="/assets/index-old111.js"></script>';
    visibility = 'visible';
    vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => visibility);
  });

  afterEach(() => {
    vi.useRealTimers();
    document.head.innerHTML = '';
  });

  async function after(ms: number, fire: () => void = () => undefined) {
    await act(async () => {
      vi.advanceTimersByTime(ms);
      fire();
    });
  }

  it('does not ask right after loading, which got the current version', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch');
    renderHook(() => useNewVersion());

    await after(0, () => window.dispatchEvent(new Event('focus')));

    expect(fetchMock).not.toHaveBeenCalled();
  });

  it.each([
    ['the tab comes back into view', () => document.dispatchEvent(new Event('visibilitychange'))],
    ['the window gets the focus', () => window.dispatchEvent(new Event('focus'))],
    ['the connection comes back', () => window.dispatchEvent(new Event('online'))],
  ])('asks when %s', async (_, fire) => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(page('new222'));
    const { result } = renderHook(() => useNewVersion());

    await after(MIN_GAP_MS, fire);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(result.current).toBe(true);
  });

  it('asks once when focus and visibility come together', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(page('old111'));
    const { result } = renderHook(() => useNewVersion());

    await after(MIN_GAP_MS, () => {
      window.dispatchEvent(new Event('focus'));
      document.dispatchEvent(new Event('visibilitychange'));
    });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(result.current).toBe(false);
  });

  it('keeps asking every few minutes while the page stays in view, and not while it is hidden', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(page('old111'));
    const { result } = renderHook(() => useNewVersion());

    await after(CHECK_EVERY_MS);
    expect(fetchMock).toHaveBeenCalledTimes(1);

    visibility = 'hidden';
    fetchMock.mockResolvedValue(page('new222'));
    await after(CHECK_EVERY_MS);
    expect(fetchMock).toHaveBeenCalledTimes(1);

    visibility = 'visible';
    await after(CHECK_EVERY_MS);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(result.current).toBe(true);
  });

  it('asks again next time when the server cannot be reached', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch')
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValueOnce(page('new222'));
    const { result } = renderHook(() => useNewVersion());

    await after(MIN_GAP_MS, () => window.dispatchEvent(new Event('focus')));
    expect(result.current).toBe(false);

    await after(MIN_GAP_MS, () => window.dispatchEvent(new Event('online')));
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(result.current).toBe(true);
  });

  it('asks nothing in development, where Vite serves the sources', async () => {
    document.head.innerHTML = '<script type="module" src="/src/main.tsx"></script>';
    const fetchMock = vi.spyOn(globalThis, 'fetch');
    renderHook(() => useNewVersion());

    await after(CHECK_EVERY_MS, () => window.dispatchEvent(new Event('focus')));

    expect(fetchMock).not.toHaveBeenCalled();
  });
});
