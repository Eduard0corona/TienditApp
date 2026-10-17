import { getApiErrorStatus } from '../api/errors';

/**
 * While an order is in progress, the customer's screens ask for it again this often (Fase 24C): the business moves it from
 * Pedidos, and the customer sees the new step without reloading.
 */
export const TRACKING_REFRESH_MS = 20_000;

/**
 * How often to ask again: every 20 seconds while something is in progress, never once the server said it is gone (a 404:
 * the link expired, or the order is not the account's). A failed request of another kind (no connection) asks again.
 */
export function trackingRefresh(inProgress: boolean, error: unknown): number | false {
  if (getApiErrorStatus(error) === 404) return false;
  return inProgress ? TRACKING_REFRESH_MS : false;
}
