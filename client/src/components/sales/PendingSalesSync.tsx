import { useCallback, useEffect } from 'react';
import { usePendingSales } from '../../hooks/usePendingSales';
import { useToast } from '../../hooks/useToast';

const RETRY_MS = 30_000;

/**
 * Sends the sales charged without connection as soon as it comes back, on any screen: when the browser reports
 * the connection, when the app opens and every 30 seconds while some are waiting.
 */
export default function PendingSalesSync() {
  const toast = useToast();
  const { pendingCount, sync } = usePendingSales();
  const hasPending = pendingCount > 0;

  const run = useCallback(async () => {
    const result = await sync();
    if (!result) return;
    if (result.sent.length === 1) {
      const [{ sale, order }] = result.sent;
      toast.success(`Venta ${sale.folio} enviada: ahora es el pedido ${order.orderNumber}.`);
    } else if (result.sent.length > 1) {
      toast.success(`${result.sent.length} ventas sin conexión enviadas.`);
    }
    if (result.failed.length > 0) {
      toast.error(result.failed.length === 1
        ? `La venta ${result.failed[0].folio} no se aceptó: revísala en Vender.`
        : `${result.failed.length} ventas sin conexión no se aceptaron: revísalas en Vender.`);
    }
  }, [sync, toast]);

  useEffect(() => {
    if (!hasPending) return;
    void run();
    const onOnline = () => void run();
    window.addEventListener('online', onOnline);
    const timer = window.setInterval(() => void run(), RETRY_MS);
    return () => {
      window.removeEventListener('online', onOnline);
      window.clearInterval(timer);
    };
  }, [hasPending, run]);

  return null;
}
