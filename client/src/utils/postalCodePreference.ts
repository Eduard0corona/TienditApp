import { POSTAL_CODE_PENDING_KEY, POSTAL_CODE_STORAGE_KEY } from './customerDevice';
import { isPostalCode } from './postalCodes';

const STORAGE_KEY = POSTAL_CODE_STORAGE_KEY;

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

/**
 * The code chosen here that the customer account could not keep yet (offline, Fase 31J), or null: it is newer than the
 * account's, and goes again. '' is a code taken away.
 */
export function pendingPostalCode(): string | null {
  try {
    return window.localStorage.getItem(POSTAL_CODE_PENDING_KEY);
  } catch {
    return null;
  }
}

/** Remembers the code the account could not keep yet; null once it kept it. */
export function markPostalCodePending(postalCode: string | null): void {
  try {
    if (postalCode === null) window.localStorage.removeItem(POSTAL_CODE_PENDING_KEY);
    else window.localStorage.setItem(POSTAL_CODE_PENDING_KEY, postalCode);
  } catch {
    // Storage blocked: the next change sends it.
  }
}
