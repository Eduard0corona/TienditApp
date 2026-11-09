import { useSyncExternalStore } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { orderInvoiceChangeKey } from '../../api/invoicing';

/**
 * Whether a change of the order's invoicing is on its way, from this dialog or from one opened before it: stamping or
 * replacing the invoice, issuing a complement or a credit note, asking the PAC again, cancelling or sending one. While
 * it is, the dialog of the order's invoices does not close and sends nothing else. What `useIsMutating` does, over the
 * query client the first visit already loads, so its code stays out of that visit.
 */
export function useChangingOrderInvoicing(orderId: string) {
  const client = useQueryClient();
  return useSyncExternalStore(
    (notify) => client.getMutationCache().subscribe(notify),
    () => client.isMutating({ mutationKey: orderInvoiceChangeKey(orderId) }) > 0,
  );
}
