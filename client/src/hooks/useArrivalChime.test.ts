import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useArrivalChime } from './useArrivalChime';

/** The notes each fake audio context played, in order. */
const played: number[] = [];

class FakeAudioContext {
  state: AudioContextState = 'suspended';
  currentTime = 0;
  destination = {};

  resume() {
    this.state = 'running';
    return Promise.resolve();
  }

  close() {
    return Promise.resolve();
  }

  createOscillator() {
    const oscillator = {
      type: 'sine',
      frequency: { value: 0 },
      connect: (node: unknown) => node,
      start: () => played.push(oscillator.frequency.value),
      stop: () => undefined,
    };
    return oscillator;
  }

  createGain() {
    const gain = {
      gain: { setValueAtTime: () => undefined, exponentialRampToValueAtTime: () => undefined },
      connect: (node: unknown) => node,
    };
    return gain;
  }
}

describe('useArrivalChime', () => {
  beforeEach(() => {
    played.length = 0;
    window.localStorage.clear();
    vi.stubGlobal('AudioContext', FakeAudioContext);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    window.localStorage.clear();
  });

  it('plays two notes up for an arrival and two down for a cancellation, only while it is on (Fase 29P)', async () => {
    const { result } = renderHook(() => useArrivalChime('tienditapp:test-sound'));

    act(() => result.current.play());
    expect(played).toEqual([]);

    await act(async () => {
      result.current.toggle();
    });
    act(() => result.current.play());
    act(() => result.current.play('leaving'));

    expect(played).toEqual([880, 1175, 1175, 880]);
    expect(window.localStorage.getItem('tienditapp:test-sound')).toBe('on');

    // Turned off, it plays nothing more.
    await act(async () => {
      result.current.toggle();
    });
    act(() => result.current.play());
    expect(played).toEqual([880, 1175, 1175, 880]);
    expect(window.localStorage.getItem('tienditapp:test-sound')).toBe('off');
  });

  it('unlocks with a key as with a touch, and follows another tab of the browser (Fase 29P)', async () => {
    window.localStorage.setItem('tienditapp:test-sound', 'on');
    const { result } = renderHook(() => useArrivalChime('tienditapp:test-sound'));
    expect(result.current.needsTouch).toBe(true);

    await act(async () => {
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab' }));
    });
    expect(result.current.needsTouch).toBe(false);
    act(() => result.current.play());
    expect(played).toEqual([880, 1175]);

    act(() => {
      window.dispatchEvent(new StorageEvent('storage', { key: 'tienditapp:test-sound', newValue: 'off' }));
    });
    expect(result.current.enabled).toBe(false);
    act(() => {
      window.dispatchEvent(new StorageEvent('storage', { key: 'tienditapp:other-sound', newValue: 'on' }));
    });
    expect(result.current.enabled).toBe(false);
  });

  it('turns on and off with it every chime of the page with its key, and no other (Fase 29P)', async () => {
    const agenda = renderHook(() => useArrivalChime('tienditapp:appointments-sound'));
    const topBar = renderHook(() => useArrivalChime('tienditapp:appointments-sound'));
    const orders = renderHook(() => useArrivalChime('tienditapp:orders-sound'));

    await act(async () => {
      agenda.result.current.toggle();
    });

    expect(topBar.result.current.enabled).toBe(true);
    expect(orders.result.current.enabled).toBe(false);
    // The top bar unlocked its own sound with the same touch: it plays.
    act(() => topBar.result.current.play('leaving'));
    expect(played).toEqual([1175, 880]);

    await act(async () => {
      agenda.result.current.toggle();
    });
    expect(topBar.result.current.enabled).toBe(false);
  });
});
