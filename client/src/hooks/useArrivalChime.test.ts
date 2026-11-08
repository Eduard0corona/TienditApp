import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// The page's sound lives as long as the module: each test loads it again, as a page that just opened.
let useArrivalChime: typeof import('./useArrivalChime').useArrivalChime;
let chimeModule: typeof import('./useArrivalChime');

/** The notes each fake audio context played, in order, with their wave and their loudest point. */
const played: number[] = [];
const waves: string[] = [];
const peaks: number[] = [];
/** The times of the ramps of the note being built, until it starts. */
const ramps: number[] = [];
/** When each note starts and stops, and when its sound reaches its loudest and fades: the timing the ear hears. */
const timings: { start: number; stop: number; loudest: number; quiet: number }[] = [];

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
      start: (at: number) => {
        played.push(oscillator.frequency.value);
        waves.push(oscillator.type);
        timings.push({ start: at, stop: NaN, loudest: ramps.shift() ?? NaN, quiet: ramps.shift() ?? NaN });
      },
      stop: (at: number) => {
        timings[timings.length - 1].stop = at;
      },
    };
    return oscillator;
  }

  createGain() {
    let rising = true;
    const gain = {
      gain: {
        setValueAtTime: () => undefined,
        // The first ramp goes up to the loudest point; the second, down to silence.
        exponentialRampToValueAtTime: (value: number, at: number) => {
          if (rising) peaks.push(value);
          ramps.push(at);
          rising = false;
        },
      },
      connect: (node: unknown) => node,
    };
    return gain;
  }
}

describe('useArrivalChime', () => {
  beforeEach(async () => {
    played.length = 0;
    waves.length = 0;
    peaks.length = 0;
    timings.length = 0;
    ramps.length = 0;
    window.localStorage.clear();
    vi.stubGlobal('AudioContext', FakeAudioContext);
    vi.resetModules();
    chimeModule = await import('./useArrivalChime');
    ({ useArrivalChime } = chimeModule);
  });

  afterEach(() => {
    vi.restoreAllMocks();
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

  it('plays every chime with the one touch, and says so in each, as the agenda that opens again (Fase 29P)', async () => {
    window.localStorage.setItem('tienditapp:appointments-sound', 'on');
    const agenda = renderHook(() => useArrivalChime('tienditapp:appointments-sound'));
    expect(agenda.result.current.needsTouch).toBe(true);

    // The touch reaches the agenda before the top bar mounts.
    await act(async () => {
      window.dispatchEvent(new Event('pointerdown'));
    });
    const topBar = renderHook(() => useArrivalChime('tienditapp:appointments-sound'));
    expect(agenda.result.current.needsTouch).toBe(false);
    expect(topBar.result.current.needsTouch).toBe(false);
    act(() => topBar.result.current.play());
    expect(played).toEqual([880, 1175]);

    // The agenda opens again: it does not ask for the touch the page already had.
    agenda.unmount();
    const reopened = renderHook(() => useArrivalChime('tienditapp:appointments-sound'));
    expect(reopened.result.current.needsTouch).toBe(false);
  });

  it('still unlocks with a touch after another chime of the page goes away (Fase 29P)', async () => {
    window.localStorage.setItem('tienditapp:appointments-sound', 'on');
    const agenda = renderHook(() => useArrivalChime('tienditapp:appointments-sound'));
    const topBar = renderHook(() => useArrivalChime('tienditapp:appointments-sound'));

    agenda.unmount();
    await act(async () => {
      window.dispatchEvent(new Event('pointerdown'));
    });

    expect(topBar.result.current.needsTouch).toBe(false);
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
    // The same touch started the page's sound: the top bar plays it.
    act(() => topBar.result.current.play('leaving'));
    expect(played).toEqual([1175, 880]);

    await act(async () => {
      agenda.result.current.toggle();
    });
    expect(topBar.result.current.enabled).toBe(false);
  });

  it('rings every sound with the tone and the volume this device picked, the same as before until it picks (Fase 29Z)', async () => {
    const orders = renderHook(() => useArrivalChime('tienditapp:orders-sound'));
    await act(async () => {
      orders.result.current.toggle();
    });
    act(() => orders.result.current.play());
    // Never picked: two sine notes up at 0.35, as every sound before, 0.18 s apart, loudest at 0.02 s, quiet at 0.3 s.
    expect([played, waves, peaks]).toEqual([[880, 1175], ['sine', 'sine'], [0.35, 0.35]]);
    expect(timings.map((t) => [t.start, t.loudest, t.quiet, t.stop].map((s) => Math.round(s * 100) / 100))).toEqual(
      [[0, 0.02, 0.3, 0.32], [0.18, 0.2, 0.48, 0.5]],
    );

    played.length = 0;
    waves.length = 0;
    peaks.length = 0;
    timings.length = 0;
    act(() => chimeModule.saveChimeSettings({ tone: 'doorbell', volume: 100 }));
    const agenda = renderHook(() => useArrivalChime('tienditapp:appointments-sound'));
    await act(async () => {
      agenda.result.current.toggle();
    });
    act(() => orders.result.current.play());
    act(() => agenda.result.current.play('leaving'));

    // Every sound of the page with it, an arrival up and a cancellation down; 100 is twice as loud as 50.
    expect(played).toEqual([659, 831, 988, 988, 831, 659]);
    expect(new Set(waves)).toEqual(new Set(['triangle']));
    expect(peaks.every((peak) => Math.abs(peak - 0.7) < 1e-9)).toBe(true);
    expect(window.localStorage.getItem('tienditapp:chime-tone')).toBe('doorbell');
    expect(window.localStorage.getItem('tienditapp:chime-volume')).toBe('100');
  });

  it.each([
    // Bell: two longer, brighter notes. Doorbell: three short ones. Soft: two low, slow ones.
    ['bell', [1047, 1568], 'triangle', [[0, 0.02, 0.9, 0.92], [0.25, 0.27, 1.15, 1.17]]],
    ['doorbell', [659, 831, 988], 'triangle', [[0, 0.02, 0.28, 0.3], [0.16, 0.18, 0.44, 0.46], [0.32, 0.34, 0.6, 0.62]]],
    ['soft', [523, 659], 'sine', [[0, 0.02, 0.45, 0.47], [0.28, 0.3, 0.73, 0.75]]],
  ] as const)('rings the %s tone with its own notes, wave and timing (Fase 29Z)', async (tone, notes, wave, expected) => {
    chimeModule.saveChimeSettings({ tone });
    const orders = renderHook(() => useArrivalChime('tienditapp:orders-sound'));
    await act(async () => {
      orders.result.current.toggle();
    });
    act(() => orders.result.current.play());

    expect(played).toEqual(notes);
    expect(waves).toEqual(notes.map(() => wave));
    expect(timings.map((t) => [t.start, t.loudest, t.quiet, t.stop].map((s) => Math.round(s * 100) / 100))).toEqual(expected);
  });

  it('keeps the sound as before with a tone or volume it does not know, and follows another tab that picks (Fase 29Z)', async () => {
    window.localStorage.setItem('tienditapp:chime-tone', 'sirena');
    window.localStorage.setItem('tienditapp:chime-volume', '55');
    expect(chimeModule.readChimeSettings()).toEqual({ tone: 'notes', volume: 50 });
    window.localStorage.setItem('tienditapp:chime-volume', '110');
    expect(chimeModule.readChimeSettings().volume).toBe(50);
    // A key every object has is no tone either.
    window.localStorage.setItem('tienditapp:chime-tone', 'toString');
    expect(chimeModule.readChimeSettings().tone).toBe('notes');

    const { result } = renderHook(() => chimeModule.useChimeSettings());
    act(() => {
      window.localStorage.setItem('tienditapp:chime-tone', 'bell');
      window.localStorage.setItem('tienditapp:chime-volume', '20');
      window.dispatchEvent(new StorageEvent('storage', { key: 'tienditapp:chime-volume', newValue: '20' }));
    });
    expect(result.current).toEqual({ tone: 'bell', volume: 20 });

    // Picked in this page, every piece follows it at once.
    act(() => chimeModule.saveChimeSettings({ volume: 80 }));
    expect(result.current).toEqual({ tone: 'bell', volume: 80 });
  });

  it('lets the device hear its tone while it picks it, whatever sound is on, and that click starts the sound (Fase 29Z)', async () => {
    window.localStorage.setItem('tienditapp:appointments-sound', 'on');
    const agenda = renderHook(() => useArrivalChime('tienditapp:appointments-sound'));
    expect(agenda.result.current.needsTouch).toBe(true);
    chimeModule.saveChimeSettings({ tone: 'soft', volume: 10 });

    await act(async () => {
      chimeModule.previewChime();
    });

    // Once the sound plays, not before.
    await waitFor(() => expect(played).toEqual([523, 659]));
    expect(peaks.every((peak) => Math.abs(peak - 0.07) < 1e-9)).toBe(true);
    expect(agenda.result.current.needsTouch).toBe(false);
    // Picked again at once (the arrows through the tones): no second chime on top of the first.
    await act(async () => {
      chimeModule.previewChime();
    });
    expect(played).toEqual([523, 659]);
  });

  it('rings nothing while the browser does not let the sound start (Fase 29Z)', async () => {
    vi.spyOn(FakeAudioContext.prototype, 'resume').mockReturnValue(Promise.reject(new Error('interrupted')));

    await act(async () => {
      chimeModule.previewChime();
    });

    expect(played).toEqual([]);
  });

  it('keeps the tone and volume for the page when its browser will not store them (Fase 29Z)', async () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('blocked', 'SecurityError');
    });
    const { result } = renderHook(() => chimeModule.useChimeSettings());

    act(() => chimeModule.saveChimeSettings({ tone: 'bell', volume: 70 }));

    expect(result.current).toEqual({ tone: 'bell', volume: 70 });
    expect(chimeModule.readChimeSettings()).toEqual({ tone: 'bell', volume: 70 });
  });
});
