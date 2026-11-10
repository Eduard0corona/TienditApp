import { useEffect, useRef, useState } from 'react';
import { ordersApi } from '../../api/ordersApi';
import { getApiErrorMessage } from '../../api/errors';
import type { OrderDto } from '../../types';
import type { TabTarget } from './sellModel';

/**
 * A round goes to an open tab: what the tab already has, once read; a tab that cannot be read says so. It is read once per
 * tab: `onError` may change between renders without reading it again.
 */
export function useTabOrder(tabTarget: TabTarget | null, onError: (message: string) => void): OrderDto | null {
  const [tabOrder, setTabOrder] = useState<OrderDto | null>(null);
  const reportError = useRef(onError);
  useEffect(() => {
    reportError.current = onError;
  });
  useEffect(() => {
    if (tabTarget?.kind !== 'round') return;
    let cancelled = false;
    ordersApi.getById(tabTarget.orderId)
      .then(({ data }) => {
        if (!cancelled) setTabOrder(data);
      })
      .catch((err: unknown) => {
        if (!cancelled) reportError.current(getApiErrorMessage(err, 'No se encontró la cuenta.'));
      });
    return () => {
      cancelled = true;
    };
  }, [tabTarget]);
  return tabOrder;
}
