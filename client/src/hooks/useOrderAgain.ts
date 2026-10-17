import { useNavigate } from 'react-router-dom';
import { linesOfOrder, saveCart } from '../utils/savedCart';

interface OrderToRepeat {
  businessSlug: string;
  businessName: string;
  items: Array<{ productId: string; quantity: number; optionIds?: string[] | null }>;
}

/**
 * «Pedir otra vez» (Fase 24B): fills the store's cart with the order and opens the store, which keeps what its menu still
 * has. An order all by weight has nothing to put back: the menu opens as it is, and a cart left there stays.
 */
export function useOrderAgain(): (order: OrderToRepeat) => void {
  const navigate = useNavigate();
  return (order) => {
    const { lines, skipped } = linesOfOrder(order.items);
    if (lines.length > 0) {
      saveCart(order.businessSlug, { businessName: order.businessName, lines, reorderOf: order.items.length, skipped });
    }
    navigate(`/m/${order.businessSlug}`);
  };
}
