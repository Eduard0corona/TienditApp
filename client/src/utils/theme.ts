import type { BusinessTheme } from '../types';
import { getBusinessTheme, themeCssVariables } from './businessThemes';

export type Theme = 'light' | 'dark';

const THEME_STORAGE_KEY = 'tienditapp.theme';
const BUSINESS_THEME_STORAGE_KEY = 'tienditapp.businessTheme';

// The chosen theme is remembered per browser (a convenience: it may be unavailable in private mode).
export function readStoredTheme(): Theme | null {
  try {
    const stored = window.localStorage.getItem(THEME_STORAGE_KEY);
    return stored === 'light' || stored === 'dark' ? stored : null;
  } catch {
    return null;
  }
}

export function storeTheme(theme: Theme) {
  try {
    window.localStorage.setItem(THEME_STORAGE_KEY, theme);
  } catch {
    // Storage can be blocked; the theme still applies for this visit.
  }
}

export function applyTheme(theme: Theme) {
  document.documentElement.setAttribute('data-bs-theme', theme);
}

// The colors of the business. Light or dark is each person's choice on their device (above); the business theme is
// the owner's, for the whole team and the public menu.
let businessTheme: BusinessTheme | null = null;
// A screen showing other colors for a while: the public menu of a business, or the picker previewing a theme.
let previewTheme: BusinessTheme | null | undefined;

function paintBusinessTheme() {
  const definition = getBusinessTheme(previewTheme !== undefined ? previewTheme : businessTheme);
  const root = document.documentElement;
  for (const [name, value] of Object.entries(themeCssVariables(definition))) root.style.setProperty(name, value);
  root.setAttribute('data-business-theme', String(definition.theme));
  // The browser bar on phones takes the business color too.
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', definition.light.accent);
}

/** The theme of the last business on this browser, so a reload paints its colors before the business loads. */
export function readStoredBusinessTheme(): BusinessTheme | null {
  try {
    const stored = window.localStorage.getItem(BUSINESS_THEME_STORAGE_KEY);
    if (stored === null || stored.trim() === '') return null;
    const value = Number(stored);
    return Number.isInteger(value) ? (value as BusinessTheme) : null;
  } catch {
    return null;
  }
}

/** Paints the theme of the signed-in business (null: none, TienditApp's) and remembers it for the next reload. */
export function applyBusinessTheme(theme: BusinessTheme | null | undefined) {
  businessTheme = theme ?? null;
  try {
    if (businessTheme === null) window.localStorage.removeItem(BUSINESS_THEME_STORAGE_KEY);
    else window.localStorage.setItem(BUSINESS_THEME_STORAGE_KEY, String(businessTheme));
  } catch {
    // Storage can be blocked; the theme still applies for this visit.
  }
  paintBusinessTheme();
}

/**
 * Shows another theme on top of the business's until called with undefined: the public menu paints its own
 * business, and Apariencia previews a theme before it is saved. Nothing is remembered.
 */
export function previewBusinessTheme(theme: BusinessTheme | null | undefined) {
  previewTheme = theme;
  paintBusinessTheme();
}

/** Before the first render: the colors of the last business, without waiting for it to load. */
export function restoreBusinessTheme() {
  businessTheme = readStoredBusinessTheme();
  previewTheme = undefined;
  paintBusinessTheme();
}
