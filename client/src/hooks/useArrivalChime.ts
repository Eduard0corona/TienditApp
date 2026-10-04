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

/**
 * A short two-note chime for new orders, per device (a kitchen screen wants it, the owner's phone may not).
 * Browsers only play sound after someone touches the page, so the audio unlocks on the first touch.
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
    if (!enabled || unlocked) return undefined;
    window.addEventListener('pointerdown', unlock, { once: true });
    return () => window.removeEventListener('pointerdown', unlock);
  }, [enabled, unlocked, unlock]);

  useEffect(() => () => void contextRef.current?.close().catch(() => undefined), []);

  const toggle = useCallback(() => {
    setEnabled((current) => {
      const next = !current;
      writeFlag(storageKey, next);
      return next;
    });
    // The click on the toggle is the touch the browser asks for.
    unlock();
  }, [storageKey, unlock]);

  const play = useCallback(() => {
    const context = contextRef.current;
    if (!enabled || !context || context.state !== 'running') return;
    const start = context.currentTime;
    [880, 1175].forEach((frequency, index) => {
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
