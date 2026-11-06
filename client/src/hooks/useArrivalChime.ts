import { useCallback, useEffect, useState } from 'react';

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

/** The window event that tells every chime of the page that the shared sound started or stopped playing. */
const AUDIO_STATE = 'tienditapp:chime-audio';

/**
 * The page's one sound, shared by every chime (Fase 29P): what one touch unlocks, every chime plays, and each one says
 * whether it still needs a touch by the sound that plays, not by one of its own (the agenda asks for the touch the top bar
 * needs). It lasts as long as the page.
 */
let pageAudio: AudioContext | null = null;

function audioPlays(): boolean {
  return pageAudio?.state === 'running';
}

/** Starts the page's sound inside a touch or a key, the only moment the browser lets it start. */
function unlockPageAudio() {
  const AudioContextClass = window.AudioContext ?? (window as AudioWindow).webkitAudioContext;
  if (!AudioContextClass) return;
  if (!pageAudio) {
    pageAudio = new AudioContextClass();
    // The browser may stop it again (a phone that locks): every chime then asks for a touch again.
    pageAudio.addEventListener?.('statechange', () => window.dispatchEvent(new Event(AUDIO_STATE)));
  }
  void pageAudio.resume()
    .then(() => window.dispatchEvent(new Event(AUDIO_STATE)))
    .catch(() => undefined);
}

/** A sound that something arrived (two notes up) or left (two notes down: a cancellation, Fase 29P). */
export type ChimeKind = 'arrival' | 'leaving';

/**
 * A short two-note chime for new orders, per browser (a kitchen screen wants it, the owner's phone may not).
 * Browsers only play sound after someone touches the page or presses a key, so the sound of the page unlocks then, once
 * for every chime. Every chime of the page with the same key follows the one that was turned on or off (the agenda turns
 * on the one of the top bar, Fase 29P), and so do the other tabs of the browser.
 */
export function useArrivalChime(storageKey: string) {
  const [enabled, setEnabled] = useState(() => readFlag(storageKey));
  const [unlocked, setUnlocked] = useState(audioPlays);

  useEffect(() => {
    const follow = () => setUnlocked(audioPlays());
    window.addEventListener(AUDIO_STATE, follow);
    return () => window.removeEventListener(AUDIO_STATE, follow);
  }, []);

  useEffect(() => {
    const listener = (event: Event) => {
      const change = (event as CustomEvent<ChimeChange>).detail;
      if (change?.key !== storageKey) return;
      // The toggle that turned it on already started the page's sound with its touch.
      setEnabled(change.enabled);
    };
    // Another tab of the same browser turned it on or off: this one follows (it unlocks with the page's next touch).
    const otherTab = (event: StorageEvent) => {
      if (event.key === storageKey) setEnabled(event.newValue === 'on');
    };
    window.addEventListener(CHIME_CHANGED, listener);
    window.addEventListener('storage', otherTab);
    return () => {
      window.removeEventListener(CHIME_CHANGED, listener);
      window.removeEventListener('storage', otherTab);
    };
  }, [storageKey]);

  useEffect(() => {
    if (!enabled || unlocked) return undefined;
    // A touch, a click or a key: whoever works with the keyboard also unlocks it. A listener of its own, so that another
    // chime that goes away does not take this one's with it.
    const onTouch = () => unlockPageAudio();
    window.addEventListener('pointerdown', onTouch, { once: true });
    window.addEventListener('keydown', onTouch, { once: true });
    return () => {
      window.removeEventListener('pointerdown', onTouch);
      window.removeEventListener('keydown', onTouch);
    };
  }, [enabled, unlocked]);

  const toggle = useCallback(() => {
    const next = !enabled;
    setEnabled(next);
    writeFlag(storageKey, next);
    window.dispatchEvent(new CustomEvent<ChimeChange>(CHIME_CHANGED, { detail: { key: storageKey, enabled: next } }));
    // The click on the toggle is the touch the browser asks for; turning it off needs no sound.
    if (next) unlockPageAudio();
  }, [enabled, storageKey]);

  const play = useCallback((kind: ChimeKind = 'arrival') => {
    const context = pageAudio;
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
