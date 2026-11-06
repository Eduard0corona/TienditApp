import { useCallback, useEffect, useRef, useState } from 'react';

type AudioWindow = Window & { webkitAudioContext?: typeof AudioContext };

function readFlag(key: string): boolean {
  try {
    return window.localStorage.getItem(key) === 'on';
  } catch {
    return false;
  }
}

function writeFlag(key: string, enabled: boolean) {
  try {
    window.localStorage.setItem(key, enabled ? 'on' : 'off');
  } catch {
    // Private mode or blocked storage: the choice lasts until the page closes.
  }
}

/** The window event that tells every chime of the page that one with its key was turned on or off. */
const CHIME_CHANGED = 'tienditapp:chime-changed';

interface ChimeChange {
  key: string;
  enabled: boolean;
}

/** A sound that something arrived (two notes up) or left (two notes down: a cancellation, Fase 29P). */
export type ChimeKind = 'arrival' | 'leaving';

/**
 * A short two-note chime for new orders, per browser (a kitchen screen wants it, the owner's phone may not).
 * Browsers only play sound after someone touches the page or presses a key, so the audio unlocks then. Every chime of the
 * page with the same key follows the one that was turned on or off (the agenda turns on the one of the top bar, Fase 29P),
 * and so do the other tabs of the browser.
 */
export function useArrivalChime(storageKey: string) {
  const [enabled, setEnabled] = useState(() => readFlag(storageKey));
  const [unlocked, setUnlocked] = useState(false);
  const contextRef = useRef<AudioContext | null>(null);

  const unlock = useCallback(() => {
    const AudioContextClass = window.AudioContext ?? (window as AudioWindow).webkitAudioContext;
    if (!AudioContextClass) return;
    contextRef.current ??= new AudioContextClass();
    void contextRef.current.resume().then(() => setUnlocked(contextRef.current?.state === 'running'));
  }, []);

  useEffect(() => {
    const listener = (event: Event) => {
      const change = (event as CustomEvent<ChimeChange>).detail;
      if (change?.key !== storageKey) return;
      setEnabled(change.enabled);
      // Still inside the touch that turned it on: the browser lets this one play too.
      if (change.enabled) unlock();
    };
    // Another tab of the same browser turned it on or off: this one follows (it unlocks with its own next touch).
    const otherTab = (event: StorageEvent) => {
      if (event.key === storageKey) setEnabled(event.newValue === 'on');
    };
    window.addEventListener(CHIME_CHANGED, listener);
    window.addEventListener('storage', otherTab);
    return () => {
      window.removeEventListener(CHIME_CHANGED, listener);
      window.removeEventListener('storage', otherTab);
    };
  }, [storageKey, unlock]);

  useEffect(() => {
    if (!enabled || unlocked) return undefined;
    // A touch, a click or a key: whoever works with the keyboard also unlocks it.
    window.addEventListener('pointerdown', unlock, { once: true });
    window.addEventListener('keydown', unlock, { once: true });
    return () => {
      window.removeEventListener('pointerdown', unlock);
      window.removeEventListener('keydown', unlock);
    };
  }, [enabled, unlocked, unlock]);

  useEffect(() => () => void contextRef.current?.close().catch(() => undefined), []);

  const toggle = useCallback(() => {
    const next = !enabled;
    setEnabled(next);
    writeFlag(storageKey, next);
    window.dispatchEvent(new CustomEvent<ChimeChange>(CHIME_CHANGED, { detail: { key: storageKey, enabled: next } }));
    // The click on the toggle is the touch the browser asks for; turning it off needs no sound.
    if (next) unlock();
  }, [enabled, storageKey, unlock]);

  const play = useCallback((kind: ChimeKind = 'arrival') => {
    const context = contextRef.current;
    if (!enabled || !context || context.state !== 'running') return;
    const start = context.currentTime;
    (kind === 'arrival' ? [880, 1175] : [1175, 880]).forEach((frequency, index) => {
      const at = start + index * 0.18;
      const oscillator = context.createOscillator();
      const gain = context.createGain();
      oscillator.type = 'sine';
      oscillator.frequency.value = frequency;
      gain.gain.setValueAtTime(0.0001, at);
      gain.gain.exponentialRampToValueAtTime(0.35, at + 0.02);
      gain.gain.exponentialRampToValueAtTime(0.0001, at + 0.3);
      oscillator.connect(gain).connect(context.destination);
      oscillator.start(at);
      oscillator.stop(at + 0.32);
    });
  }, [enabled]);

  return { enabled, toggle, play, needsTouch: enabled && !unlocked };
}
