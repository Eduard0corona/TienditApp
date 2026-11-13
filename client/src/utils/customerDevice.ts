import type { UserProfileDto } from '../types';
import { getAccess } from './access';

/** Where this device keeps the cart of each store (Fase 24B). */
export const CARTS_STORAGE_KEY = 'tienditapp.carts';
/** Where this device keeps the postal code given in Explorar or a store. */
export const POSTAL_CODE_STORAGE_KEY = 'tienditapp.postalCode';
/** Said to the window when the session ends on this device: a cart waiting to go to the account does not go. */
export const CUSTOMER_LEFT_EVENT = 'tienditapp:customer-left';

/** The customer account whose carts and postal code this device keeps (Fase 31J). */
export const CUSTOMER_STORAGE_KEY = 'tienditapp.customer';

/**
 * The session of a customer account starts on this device, which keeps its carts and postal code from now on: the carts
 * it chose before go to the account. Those of another account that left without the device forgetting them (a session
 * that ended while it was closed) go first: whoever signs in does not take them into their own account. A session of a
 * business's team changes nothing: its carts are only ever on this device.
 */
export function rememberCustomerDevice(user: UserProfileDto): void {
  if (!getAccess(user, null).myOrders) return;
  try {
    const kept = window.localStorage.getItem(CUSTOMER_STORAGE_KEY);
    if (kept && kept !== user.id) forgetCustomerDevice();
    window.localStorage.setItem(CUSTOMER_STORAGE_KEY, user.id);
    // What it chose here before goes to the account, which keeps it when the device forgets it.
    if (window.localStorage.getItem(CARTS_STORAGE_KEY)) {
      void import('./accountCartSync').then((sync) => sync.sendDeviceCarts()).catch(() => undefined);
    }
  } catch {
    // Storage blocked: there is nothing kept.
  }
}

/**
 * The session ends on this device, however it ends (signing out here or in another tab, expired or revoked, also found
 * so when the app opens), and nothing waiting goes to the account. When it was a customer account's, the device forgets its carts and
 * postal code (the account keeps them), so whoever comes next does not take them into their own account.
 */
export function forgetCustomerDevice(): void {
  try {
    if (window.localStorage.getItem(CUSTOMER_STORAGE_KEY)) {
      for (const key of [CARTS_STORAGE_KEY, POSTAL_CODE_STORAGE_KEY, CUSTOMER_STORAGE_KEY]) window.localStorage.removeItem(key);
    }
  } catch {
    // Storage blocked: there was nothing kept.
  }
  window.dispatchEvent(new Event(CUSTOMER_LEFT_EVENT));
}
