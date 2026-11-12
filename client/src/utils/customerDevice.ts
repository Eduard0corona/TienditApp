import type { UserProfileDto } from '../types';
import { getAccess } from './access';

/** Where this device keeps the cart of each store (Fase 24B). */
export const CARTS_STORAGE_KEY = 'tienditapp.carts';
/** Where this device keeps the postal code given in Explorar or a store. */
export const POSTAL_CODE_STORAGE_KEY = 'tienditapp.postalCode';
/** Said to the window when a customer account leaves this device: a cart waiting to go to the account does not go. */
export const CUSTOMER_LEFT_EVENT = 'tienditapp:customer-left';

/**
 * A customer account leaves this device (Fase 31J): its carts and postal code are kept in the account, so the device forgets
 * them and does not send what was waiting, and whoever signs in next here does not take them into their own account. A
 * session of a business's team keeps them: its carts were only ever on this device.
 */
export function forgetCustomerDevice(user: UserProfileDto): void {
  if (!getAccess(user, null).myOrders) return;
  try {
    window.localStorage.removeItem(CARTS_STORAGE_KEY);
    window.localStorage.removeItem(POSTAL_CODE_STORAGE_KEY);
  } catch {
    // Storage blocked: there was nothing kept.
  }
  window.dispatchEvent(new Event(CUSTOMER_LEFT_EVENT));
}
