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

/** A sound that something arrived (its notes up) or left (the same notes down: a cancellation, Fase 29P). */
export type ChimeKind = 'arrival' | 'leaving';

/**
 * The tones a device picks for every sound of the page (Fase 29Z): the same for orders, requests, quotes and the kitchen,
 * each still turned on or off by itself. «Dos notas» is the one every sound had before.
 */
export type ChimeTone = 'notes' | 'bell' | 'doorbell' | 'soft';

interface ToneShape {
  wave: OscillatorType;
  /** Its notes as something arrives; it leaves with them the other way round. */
  notes: number[];
  /** Seconds from one note to the next, and how long each lasts. */
  step: number;
  length: number;
}

const TONES: Record<ChimeTone, ToneShape> = {
  notes: { wave: 'sine', notes: [880, 1175], step: 0.18, length: 0.3 },
  bell: { wave: 'triangle', notes: [1047, 1568], step: 0.25, length: 0.9 },
  doorbell: { wave: 'triangle', notes: [659, 831, 988], step: 0.16, length: 0.28 },
  soft: { wave: 'sine', notes: [523, 659], step: 0.28, length: 0.45 },
};

/** The tones as the device's choice lists them. */
export const CHIME_TONES: ReadonlyArray<{ value: ChimeTone; label: string }> = [
  { value: 'notes', label: 'Dos notas' },
  { value: 'bell', label: 'Campana' },
  { value: 'doorbell', label: 'Timbre' },
  { value: 'soft', label: 'Suave' },
];

/** The volume a device picks, from 10 to 100 in tens: 50, the one every sound had before (Fase 29Z). */
export const CHIME_VOLUMES = { min: 10, max: 100, step: 10, initial: 50 } as const;

const TONE_KEY = 'tienditapp:chime-tone';
const VOLUME_KEY = 'tienditapp:chime-volume';

/** The window event that tells every page piece that the device picked another tone or volume. */
const SETTINGS_CHANGED = 'tienditapp:chime-settings-changed';

export interface ChimeSettings {
  tone: ChimeTone;
  volume: number;
}

function readStored(key: string): string | null {
  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

/** What this device picked; what it never picked, or a value this version does not know, is as before. */
export function readChimeSettings(): ChimeSettings {
  const tone = readStored(TONE_KEY);
  const volume = Number(readStored(VOLUME_KEY));
  return {
    tone: tone !== null && tone in TONES ? (tone as ChimeTone) : 'notes',
    volume: Number.isInteger(volume) && volume >= CHIME_VOLUMES.min && volume <= CHIME_VOLUMES.max && volume % CHIME_VOLUMES.step === 0
      ? volume
      : CHIME_VOLUMES.initial,
  };
}

/** Saves the device's tone or volume, and every sound of the page (and of its other tabs) rings with it from then on. */
export function saveChimeSettings(change: Partial<ChimeSettings>) {
  try {
    if (change.tone !== undefined) window.localStorage.setItem(TONE_KEY, change.tone);
    if (change.volume !== undefined) window.localStorage.setItem(VOLUME_KEY, String(change.volume));
  } catch {
    // Private mode or blocked storage: the sound keeps the one it had.
  }
  window.dispatchEvent(new Event(SETTINGS_CHANGED));
}

/** The device's tone and volume, following a change made in another piece of the page or another tab. */
export function useChimeSettings(): ChimeSettings {
  const [settings, setSettings] = useState(readChimeSettings);
  useEffect(() => {
    const follow = () => setSettings(readChimeSettings());
    const otherTab = (event: StorageEvent) => {
      if (event.key === TONE_KEY || event.key === VOLUME_KEY) follow();
    };
    window.addEventListener(SETTINGS_CHANGED, follow);
    window.addEventListener('storage', otherTab);
    return () => {
      window.removeEventListener(SETTINGS_CHANGED, follow);
      window.removeEventListener('storage', otherTab);
    };
  }, []);
  return settings;
}

/** Rings the device's tone at its volume. The loudest point of a note is 0.7 at 100, and 0.35 at 50, as before. */
function ring(context: AudioContext, kind: ChimeKind) {
  const { tone, volume } = readChimeSettings();
  const shape = TONES[tone];
  const peak = (0.7 * volume) / CHIME_VOLUMES.max;
  const notes = kind === 'arrival' ? shape.notes : [...shape.notes].reverse();
  const start = context.currentTime;
  notes.forEach((frequency, index) => {
    const at = start + index * shape.step;
    const oscillator = context.createOscillator();
    const gain = context.createGain();
    oscillator.type = shape.wave;
    oscillator.frequency.value = frequency;
    gain.gain.setValueAtTime(0.0001, at);
    gain.gain.exponentialRampToValueAtTime(peak, at + 0.02);
    gain.gain.exponentialRampToValueAtTime(0.0001, at + shape.length);
    oscillator.connect(gain).connect(context.destination);
    oscillator.start(at);
    oscillator.stop(at + shape.length + 0.02);
  });
}

/**
 * Rings the device's tone once, to hear it while picking it: inside the click that asks for it, so it also starts the page's
 * sound, whatever sound is turned on.
 */
export function previewChime() {
  unlockPageAudio();
  if (pageAudio) ring(pageAudio, 'arrival');
}

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
    ring(context, kind);
  }, [enabled]);

  return { enabled, toggle, play, needsTouch: enabled && !unlocked };
}
