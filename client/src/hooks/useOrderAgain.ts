import { useNavigate } from 'react-router-dom';
import { scheduleAccountCartSave } from '../utils/accountCartSync';
import { linesOfOrder, saveCart } from '../utils/savedCart';
import { useCustomerAccount } from './useCustomerAccount';

interface OrderToRepeat {
  businessSlug: string;
  businessName: string;
  items: Array<{ productId: string; quantity: number; optionIds?: string[] | null; notes?: string | null }>;
}

/** How long «Pedir otra vez» waits for the account before it opens the store anyway (the device keeps the cart). */
export const ORDER_AGAIN_ACCOUNT_WAIT_MS = 2000;

/**
 * «Pedir otra vez» (Fase 24B): fills the store's cart with the order and opens the store, which keeps what its menu still
 * has. An order all by weight has nothing to put back: the menu opens as it is, and a cart left there stays.
 * With a customer account (Fase 31P) the cart goes to the account first, with what did not come back, and the store opens
 * once it is kept there (or after a moment, if it cannot be): another device brings it back with its notice, and this
 * device has the server's time for it, so the store does not take an older cart of the account over it.
 */
export function useOrderAgain(): (order: OrderToRepeat) => Promise<void> {
  const navigate = useNavigate();
  const { customer } = useCustomerAccount();
  return async (order) => {
    const { lines, skipped } = linesOfOrder(order.items);
    if (lines.length > 0) {
      const reorderOf = order.items.length;
      saveCart(order.businessSlug, { businessName: order.businessName, lines, reorderOf, skipped });
      if (customer) {
        await new Promise<void>((resolve) => {
          const timer = window.setTimeout(resolve, ORDER_AGAIN_ACCOUNT_WAIT_MS);
          scheduleAccountCartSave(order.businessSlug, { lines, reorderOf, skipped }, () => {
            window.clearTimeout(timer);
            resolve();
          }, 0);
        });
      }
    }
    navigate(`/m/${order.businessSlug}`);
  };
}
