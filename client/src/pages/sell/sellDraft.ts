// The sale being built survives a reload or a trip to another screen (per browser and business). It is
// a convenience: storage can be unavailable and the sale still works.

export interface SellDraftLine {
  productId: string;
  quantity: number;
  notes: string;
  /** Variants and extras chosen for the line. */
  optionIds?: string[];
}

/**
 * A charge sent for the sale of the draft and not confirmed yet. A retry, also after a reload, sends its key again
 * and the server answers with the sale it already recorded instead of recording another one.
 */
export interface PendingCharge {
  /** The sale's idempotency key (`CreateOrderRequest.idempotencyKey`). */
  key: string;
  /** The sale it was sent for (`describeSale`): another cart is another sale, with a key of its own. */
  sale: string;
}

export interface SellDraft {
  lines: SellDraftLine[];
  customerName: string;
  customerPhone: string;
  tableNumber: string;
  orderNotes: string;
  pendingCharge?: PendingCharge;
}

const storageKey = (tenantId: string) => `tienditapp.sell.draft.v1.${tenantId}`;

/** What the server receives of the sale (its lines and customer), to tell whether a cart is still the one charged. */
export function describeSale(draft: SellDraft): string {
  return JSON.stringify([
    draft.lines.map((line) => [line.productId, line.quantity, line.notes.trim(), line.optionIds ?? []]),
    [draft.customerName, draft.customerPhone, draft.tableNumber, draft.orderNotes].map((value) => value.trim()),
  ]);
}

export function readSellDraft(tenantId: string): SellDraft | null {
  try {
    const raw = window.localStorage.getItem(storageKey(tenantId));
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Partial<SellDraft>;
    const charge = parsed.pendingCharge;
    return {
      lines: Array.isArray(parsed.lines)
        ? parsed.lines
          .filter((line) => typeof line?.productId === 'string' && Number.isFinite(line.quantity) && line.quantity > 0)
          .map((line) => ({
            ...line,
            optionIds: Array.isArray(line.optionIds) ? line.optionIds.filter((id) => typeof id === 'string') : [],
          }))
        : [],
      customerName: typeof parsed.customerName === 'string' ? parsed.customerName : '',
      customerPhone: typeof parsed.customerPhone === 'string' ? parsed.customerPhone : '',
      tableNumber: typeof parsed.tableNumber === 'string' ? parsed.tableNumber : '',
      orderNotes: typeof parsed.orderNotes === 'string' ? parsed.orderNotes : '',
      pendingCharge: typeof charge?.key === 'string' && typeof charge.sale === 'string'
        ? { key: charge.key, sale: charge.sale }
        : undefined,
    };
  } catch {
    return null;
  }
}

export function writeSellDraft(tenantId: string, draft: SellDraft) {
  try {
    const isEmpty = draft.lines.length === 0 && !draft.customerName && !draft.customerPhone
      && !draft.tableNumber && !draft.orderNotes;
    if (isEmpty) window.localStorage.removeItem(storageKey(tenantId));
    else window.localStorage.setItem(storageKey(tenantId), JSON.stringify(draft));
  } catch {
    // Storage blocked (private mode): the draft only lives in this screen.
  }
}
