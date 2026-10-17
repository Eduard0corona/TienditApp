import { isPostalCode } from './postalCodes';

const STORAGE_KEY = 'tienditapp.postalCode';

/** The postal code the customer gave on this device («Entregar en…»), or '' without one. */
export function readPostalCode(): string {
  try {
    const stored = window.localStorage.getItem(STORAGE_KEY) ?? '';
    return isPostalCode(stored) ? stored : '';
  } catch {
    // Storage blocked: asked again each visit.
    return '';
  }
}

/** Remembers the postal code on this device; '' forgets it. */
export function savePostalCode(postalCode: string): void {
  try {
    if (isPostalCode(postalCode)) window.localStorage.setItem(STORAGE_KEY, postalCode);
    else window.localStorage.removeItem(STORAGE_KEY);
  } catch {
    // Storage blocked: it lasts this page only.
  }
}
