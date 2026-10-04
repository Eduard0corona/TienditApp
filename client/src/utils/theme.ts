export type Theme = 'light' | 'dark';

const THEME_STORAGE_KEY = 'tienditapp.theme';

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
