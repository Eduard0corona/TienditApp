import { useEffect } from 'react';
import { useQueryClient } from '@tanstack/react-query';

/**
 * Fase 31A (ADR 0083): «Mis pedidos» hears that an order of the account changed (the business accepted it, it is
 * ready, it went out, it was cancelled) and reads the account's orders again through /api/v1/me, which checks they are
 * its own. Only the id of the order comes: the screens ask for the rest. They still ask every 20 seconds behind it, for
 * a connection that drops or an event lost.
 */
export function useMyOrdersLive() {
  const queryClient = useQueryClient();

  useEffect(() => {
    let isDisposed = false;
    let stop: (() => void) | undefined;
    const changed = () => void queryClient.invalidateQueries({ queryKey: ['me', 'orders'] });
    // These screens are not in the first visit, and SignalR loads apart all the same.
    void import('../api/signalr').then(({ createHubConnection, keepHubConnected }) => {
      if (isDisposed) return;
      const connection = createHubConnection('/hubs/my-orders');
      connection.on('MyOrderChanged', changed);
      // What changed while it was down is read again when it connects.
      stop = keepHubConnected(connection, 'MyOrdersHub', () => isDisposed, changed);
    }).catch(() => {
      // A new version replaced the chunk: the page reloads (vite:preloadError), and the screens still ask every 20 seconds.
    });
    return () => {
      isDisposed = true;
      stop?.();
    };
  }, [queryClient]);
}
