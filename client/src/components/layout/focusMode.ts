import { createContext, useContext } from 'react';

/** Screens that run alone, full screen, on a counter tablet or a kitchen TV. */
export type FocusArea = 'sell' | 'kitchen';

const FOCUS_PATHS: Readonly<Record<string, FocusArea>> = { '/sell': 'sell', '/kitchen': 'kitchen' };
const FOCUS_AREAS: readonly FocusArea[] = ['sell', 'kitchen'];
const STORAGE_KEY = 'tienditapp.focusedAreas';

export function getFocusArea(pathname: string): FocusArea | null {
  return FOCUS_PATHS[pathname] ?? null;
}

function isFocusArea(value: unknown): value is FocusArea {
  return typeof value === 'string' && (FOCUS_AREAS as readonly string[]).includes(value);
}

/** The screens this device keeps focused: a tablet that only sells reopens Vender without the menu. */
export function readFocusedAreas(): FocusArea[] {
  try {
    const parsed: unknown = JSON.parse(window.localStorage.getItem(STORAGE_KEY) ?? '[]');
    return Array.isArray(parsed) ? parsed.filter(isFocusArea) : [];
  } catch {
    return [];
  }
}

export function storeFocusedAreas(areas: FocusArea[]) {
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(areas));
  } catch {
    // Storage can be blocked (private mode); focus mode still works for this visit.
  }
}

export interface FocusMode {
  /** The focusable screen being shown, if any. */
  area: FocusArea | null;
  isFocused: boolean;
  /** Hides the menu and asks the browser for full screen (it needs the click that calls it). */
  enter: () => void;
  exit: () => void;
}

export const FocusModeContext = createContext<FocusMode>({
  area: null,
  isFocused: false,
  enter: () => undefined,
  exit: () => undefined,
});

export function useFocusMode(): FocusMode {
  return useContext(FocusModeContext);
}
