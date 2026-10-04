import { isAxiosError } from 'axios';
import { getApiErrorMessage } from '../api/errors';
import type { CreateOrderRequest, OrderDto } from '../types';
import { OFFLINE_STORES, offlineDb } from './offlineDb';

/** A sale charged without connection, kept on this device until the server has it. */
export interface PendingSale {
  /** Also the sale's idempotency key: sending it again never charges twice. */
  id: string;
  tenantId: string;
  /** Only the cashier who charged it sends it (the payments enter their drawer). */
  userId: string;
  /** Folio shown on the ticket until the server gives the real one. */
  folio: string;
  request: CreateOrderRequest;
  total: number;
  capturedAt: string;
  status: 'pending' | 'failed';
  /** Why the server refused it (a price changed, a product is no longer sold...), or the last server error. */
  error?: string;
  attempts: number;
}

/** A sale the server keeps failing with an error of its own stops blocking the rest after these tries. */
const MAX_SERVER_ERROR_ATTEMPTS = 10;

export const PENDING_SALES_EVENT = 'ta-pending-sales-changed';

function notifyChanged() {
  window.dispatchEvent(new Event(PENDING_SALES_EVENT));
}

/** A request that never reached the server (no connection, DNS, timeout), as opposed to a refusal. */
export function isNetworkError(error: unknown): boolean {
  if (typeof navigator !== 'undefined' && !navigator.onLine) return true;
  return isAxiosError(error) && !error.response;
}

/**
 * What a failed send means for the sale: `wait` when the server could not take it now (no connection, the
 * session must be renewed by logging in again, too many requests, a gateway down), `server-error` when the server
 * failed on its own, `refused` when it answered that the sale cannot be recorded as it is.
 */
function classifySendError(error: unknown): 'wait' | 'server-error' | 'refused' {
  if (isNetworkError(error)) return 'wait';
  const status = isAxiosError(error) ? error.response?.status : undefined;
  if (status === 401 || status === 408 || status === 429 || status === 502 || status === 503 || status === 504) return 'wait';
  if (status !== undefined && status >= 500) return 'server-error';
  return 'refused';
}

function nextFolio(tenantId: string): string {
  const storageKey = `ta.offlineFolio.${tenantId}`;
  let next = 1;
  try {
    next = (Number(window.localStorage.getItem(storageKey)) || 0) + 1;
    window.localStorage.setItem(storageKey, String(next));
  } catch {
    next = Math.floor(Date.now() / 1000) % 10_000;
  }
  return `SC-${String(next).padStart(4, '0')}`;
}

export async function enqueueSale(sale: Omit<PendingSale, 'folio' | 'status' | 'attempts'>): Promise<PendingSale> {
  const pending: PendingSale = { ...sale, folio: nextFolio(sale.tenantId), status: 'pending', attempts: 0 };
  await offlineDb.put(OFFLINE_STORES.sales, pending);
  notifyChanged();
  return pending;
}

export async function listPendingSales(tenantId: string, userId: string): Promise<PendingSale[]> {
  try {
    const all = await offlineDb.getAll<PendingSale>(OFFLINE_STORES.sales);
    return all
      .filter((sale) => sale.tenantId === tenantId && sale.userId === userId)
      .sort((a, b) => a.capturedAt.localeCompare(b.capturedAt));
  } catch {
    return [];
  }
}

export async function discardPendingSale(id: string): Promise<void> {
  await offlineDb.delete(OFFLINE_STORES.sales, id);
  notifyChanged();
}

export async function retryPendingSale(sale: PendingSale): Promise<void> {
  await offlineDb.put(OFFLINE_STORES.sales, { ...sale, status: 'pending', error: undefined });
  notifyChanged();
}

export interface SyncResult {
  sent: Array<{ sale: PendingSale; order: OrderDto }>;
  failed: PendingSale[];
  /** The connection dropped again: the rest wait for the next try. */
  interrupted: boolean;
}

let running: Promise<SyncResult> | null = null;

/**
 * Sends the pending sales of the cashier, oldest first, with their keys (a sale the server already has comes back
 * as it is). A refused sale is kept as failed for the cashier to review; when the server cannot take it now (no
 * connection, an expired session, an error of its own) the round stops and the next one starts from that sale.
 */
export function syncPendingSales(
  tenantId: string,
  userId: string,
  send: (request: CreateOrderRequest) => Promise<OrderDto>,
): Promise<SyncResult> {
  running ??= (async () => {
    const result: SyncResult = { sent: [], failed: [], interrupted: false };
    try {
      const sales = (await listPendingSales(tenantId, userId)).filter((sale) => sale.status === 'pending');
      for (const sale of sales) {
        try {
          const order = await send({ ...sale.request, idempotencyKey: sale.id, capturedOfflineAt: sale.capturedAt });
          await offlineDb.delete(OFFLINE_STORES.sales, sale.id);
          result.sent.push({ sale, order });
        } catch (error: unknown) {
          const kind = classifySendError(error);
          if (kind === 'wait') {
            result.interrupted = true;
            break;
          }
          const attempts = sale.attempts + 1;
          const message = getApiErrorMessage(error, 'El servidor no aceptó la venta.');
          if (kind === 'server-error' && attempts < MAX_SERVER_ERROR_ATTEMPTS) {
            // Kept pending with the error to see; the next round tries again from this sale.
            await offlineDb.put(OFFLINE_STORES.sales, { ...sale, attempts, error: message } satisfies PendingSale);
            result.interrupted = true;
            break;
          }
          const failed: PendingSale = { ...sale, status: 'failed', attempts, error: message };
          await offlineDb.put(OFFLINE_STORES.sales, failed);
          result.failed.push(failed);
        }
      }
    } finally {
      running = null;
      notifyChanged();
    }
    return result;
  })();
  return running;
}
