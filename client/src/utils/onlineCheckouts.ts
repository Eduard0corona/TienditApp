/**
 * The online payments this device started (Fase 24I): the return key of each one, which the return page sends in the
 * body to read the order's tracking link. It never goes in a URL: the checkout's id does, and server telemetry keeps
 * URLs. Kept a day at most; storage blocked only means the return page shows no tracking link.
 */
const STORAGE_KEY = 'tienditapp.onlineCheckouts';
const MAX_AGE_MS = 24 * 60 * 60 * 1000;
const MAX_KEPT = 10;

interface KeptCheckout {
  returnKey: string;
  savedAt: string;
}

function readAll(): Record<string, KeptCheckout> {
  try {
    const parsed: unknown = JSON.parse(window.localStorage.getItem(STORAGE_KEY) ?? '{}');
    return parsed && typeof parsed === 'object' ? parsed as Record<string, KeptCheckout> : {};
  } catch {
    return {};
  }
}

function writeAll(checkouts: Record<string, KeptCheckout>, now: Date): void {
  try {
    const kept = Object.entries(checkouts)
      .filter(([, checkout]) => typeof checkout?.returnKey === 'string'
        && now.getTime() - Date.parse(checkout.savedAt) < MAX_AGE_MS)
      .sort(([, a], [, b]) => b.savedAt.localeCompare(a.savedAt))
      .slice(0, MAX_KEPT);
    if (kept.length === 0) window.localStorage.removeItem(STORAGE_KEY);
    else window.localStorage.setItem(STORAGE_KEY, JSON.stringify(Object.fromEntries(kept)));
  } catch {
    // Storage blocked or full: the return page shows the payment without the tracking link.
  }
}

export function rememberOnlineCheckout(checkoutId: string, returnKey: string, now = new Date()): void {
  writeAll({ ...readAll(), [checkoutId]: { returnKey, savedAt: now.toISOString() } }, now);
}

/** The return key of a payment started on this device, or null. */
export function onlineCheckoutReturnKey(checkoutId: string, now = new Date()): string | null {
  const kept = readAll()[checkoutId];
  if (!kept || typeof kept.returnKey !== 'string' || now.getTime() - Date.parse(kept.savedAt) >= MAX_AGE_MS) return null;
  return kept.returnKey;
}
