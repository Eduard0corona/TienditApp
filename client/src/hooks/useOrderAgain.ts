import { useEffect, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { scheduleAccountCartSave } from '../utils/accountCartSync';
import { linesOfOrder, saveCart } from '../utils/savedCart';
import { useCustomerAccount } from './useCustomerAccount';

interface OrderToRepeat {
  id: string;
  businessSlug: string;
  businessName: string;
  items: Array<{ productId: string; quantity: number; optionIds?: string[] | null; notes?: string | null }>;
}

/** How long «Pedir otra vez» waits for the account before it opens the store anyway (the device keeps the cart). */
export const ORDER_AGAIN_ACCOUNT_WAIT_MS = 2000;

export interface OrderAgain {
  orderAgain: (order: OrderToRepeat) => Promise<void>;
  /** The order whose store opens once the account kept its cart: its button says so, and the others wait. */
  opening: string | null;
}

/**
 * «Pedir otra vez» (Fase 24B): fills the store's cart with the order and opens the store, which keeps what its menu still
 * has. An order all by weight has nothing to put back: the menu opens as it is, and a cart left there stays.
 * With a customer account (Fase 31P) the cart goes to the account first, with what did not come back, and the store opens
 * once the account answers (kept, or not: the device keeps it), or after a moment: another device brings it back with
 * its notice, and this device has the server's time for it, so the store does not take an older cart of the account over
 * it. Leaving the page (or going elsewhere on it) meanwhile does not open the store after all: neither does a first tap
 * once a second one opened it.
 */
export function useOrderAgain(): OrderAgain {
  const navigate = useNavigate();
  const { key } = useLocation();
  const { customer } = useCustomerAccount();
  const [opening, setOpening] = useState<string | null>(null);
  // This visit of the page: it ends when the page closes or goes elsewhere.
  const visit = useRef<{ left: boolean } | null>(null);
  useEffect(() => {
    const here = { left: false };
    visit.current = here;
    return () => {
      here.left = true;
    };
  }, [key]);

  const orderAgain = async (order: OrderToRepeat) => {
    const { lines, skipped } = linesOfOrder(order.items);
    const here = visit.current;
    if (lines.length > 0) {
      const reorderOf = order.items.length;
      saveCart(order.businessSlug, { businessName: order.businessName, lines, reorderOf, skipped });
      if (customer) {
        setOpening(order.id);
        try {
          await new Promise<void>((resolve) => {
            const timer = window.setTimeout(resolve, ORDER_AGAIN_ACCOUNT_WAIT_MS);
            const answered = () => {
              window.clearTimeout(timer);
              resolve();
            };
            scheduleAccountCartSave(order.businessSlug, { lines, reorderOf, skipped }, answered, 0, answered);
          });
        } finally {
          setOpening(null);
        }
      }
    }
    if (here?.left) return;
    navigate(`/m/${order.businessSlug}`);
  };
  return { orderAgain, opening };
}
