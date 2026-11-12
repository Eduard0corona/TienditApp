import { accountApi, type MyCart } from '../api/accountApi';
import { CUSTOMER_LEFT_EVENT } from './customerDevice';
import { adoptAccountSavedAt, type SavedCartLine } from './savedCart';

/** What the account keeps of a cart (Fase 31J): its lines and «Pedir otra vez». */
export interface AccountCartPayload {
  lines: SavedCartLine[];
  reorderOf?: number;
  skipped?: number;
}

/** A moment after the last change: a cart that changes with each tap goes once. */
export const ACCOUNT_CART_DELAY_MS = 800;

/** Told what the account kept: its cart now, or null when it was emptied. */
export type AccountCartSaved = (saved: MyCart | null) => void;

interface Pending {
  payload: AccountCartPayload | null;
  onSaved: AccountCartSaved | null;
  timer: number | null;
  running: Promise<void> | null;
}

const pending = new Map<string, Pending>();

/** The same lines in the same order, as the account received them last. */
export function cartSignature(payload: AccountCartPayload): string {
  return JSON.stringify([payload.lines, payload.reorderOf ?? null, payload.skipped ?? null]);
}

/**
 * Keeps the cart of a store in the customer account, a moment after its last change, one save at a time and the last one
 * last: a save sent while another goes waits for it, so the account never ends with an older cart. A save that fails
 * (offline, the session ended) is dropped: the device keeps the cart and the next change sends it again. One kept gives
 * the device the account's time for it, and `onSaved` what the account kept.
 */
export function scheduleAccountCartSave(
  slug: string,
  payload: AccountCartPayload,
  onSaved: AccountCartSaved | null = null,
  delay = ACCOUNT_CART_DELAY_MS,
): void {
  const entry = pending.get(slug) ?? { payload: null, onSaved: null, timer: null, running: null };
  pending.set(slug, entry);
  entry.payload = payload;
  entry.onSaved = onSaved;
  if (entry.timer !== null) window.clearTimeout(entry.timer);
  entry.timer = window.setTimeout(() => {
    entry.timer = null;
    void send(slug, entry);
  }, delay);
}

/** The account leaves this device: what was waiting to go to it does not go (what is on its way finishes or fails). */
export function cancelAccountCartSaves(): void {
  for (const entry of pending.values()) {
    if (entry.timer !== null) window.clearTimeout(entry.timer);
    entry.timer = null;
    entry.payload = null;
    entry.onSaved = null;
  }
  pending.clear();
}
window.addEventListener(CUSTOMER_LEFT_EVENT, cancelAccountCartSaves);

async function send(slug: string, entry: Pending): Promise<void> {
  if (entry.running) {
    await entry.running;
    // A newer change is waiting its moment: it sends then.
    if (entry.timer !== null) return;
  }
  const payload = entry.payload;
  const onSaved = entry.onSaved;
  if (!payload) return;
  entry.payload = null;
  entry.onSaved = null;
  entry.running = Promise.resolve()
    .then(() => accountApi.saveCart(slug, { lines: payload.lines, reorderOf: payload.reorderOf ?? null, skipped: payload.skipped ?? null }))
    .then((saved) => {
      // A cart kept answers it; an emptied one answers nothing (204).
      const kept = saved && typeof saved.savedAt === 'string' ? saved : null;
      if (kept) adoptAccountSavedAt(slug, payload.lines, kept.savedAt);
      onSaved?.(kept);
    }, () => undefined);
  await entry.running;
  entry.running = null;
  // Changed while it went, and its moment already passed: the last one goes now.
  if (entry.payload && entry.timer === null) await send(slug, entry);
}
