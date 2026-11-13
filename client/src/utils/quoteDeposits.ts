/**
 * The deposits of quotes this device went to pay (Fase 31N, ADR 0094): the link of the quote of each payment, which the
 * return page sends in the body to read how it stands and opens again. The link never goes in a URL the server sees: the
 * payment's id does, and server telemetry keeps URLs. Kept a day at most; storage blocked only means the return page asks
 * the customer to open the link of their message again.
 */
const STORAGE_KEY = 'tienditapp.quoteDeposits';
const MAX_AGE_MS = 24 * 60 * 60 * 1000;
const MAX_KEPT = 10;

interface KeptDeposit {
  token: string;
  savedAt: string;
}

function readAll(): Record<string, KeptDeposit> {
  try {
    const parsed: unknown = JSON.parse(window.localStorage.getItem(STORAGE_KEY) ?? '{}');
    return parsed && typeof parsed === 'object' ? parsed as Record<string, KeptDeposit> : {};
  } catch {
    return {};
  }
}

function writeAll(deposits: Record<string, KeptDeposit>, now: Date): void {
  try {
    const kept = Object.entries(deposits)
      .filter(([, deposit]) => typeof deposit?.token === 'string' && typeof deposit.savedAt === 'string'
        && now.getTime() - Date.parse(deposit.savedAt) < MAX_AGE_MS)
      .sort(([, a], [, b]) => b.savedAt.localeCompare(a.savedAt))
      .slice(0, MAX_KEPT);
    if (kept.length === 0) window.localStorage.removeItem(STORAGE_KEY);
    else window.localStorage.setItem(STORAGE_KEY, JSON.stringify(Object.fromEntries(kept)));
  } catch {
    // Storage blocked or full: the return page asks to open the link again.
  }
}

export function rememberQuoteDeposit(checkoutId: string, token: string, now = new Date()): void {
  writeAll({ ...readAll(), [checkoutId]: { token, savedAt: now.toISOString() } }, now);
}

/** The link of the quote whose deposit this device went to pay, or null. */
export function quoteDepositToken(checkoutId: string, now = new Date()): string | null {
  const kept = readAll()[checkoutId];
  if (!kept || typeof kept.token !== 'string' || now.getTime() - Date.parse(kept.savedAt) >= MAX_AGE_MS) return null;
  return kept.token;
}
