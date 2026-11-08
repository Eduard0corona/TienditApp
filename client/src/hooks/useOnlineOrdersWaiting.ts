import { useEffect, useRef } from 'react';
import { useAttention } from '../api/queries';
import { useArrivalChime } from './useArrivalChime';
import { useToast } from './useToast';

/** The sound of the online orders on this device, the one Pedidos turns on (Fase 29P). */
export const ORDERS_SOUND_KEY = 'tienditapp:orders-sound';

/** Fase 29X: "Un pedido en línea lleva 15 minutos sin confirmar.", or how many do. */
export function describeWaiting(urgent: number): { title: string; message: string } {
  return {
    title: 'Pedidos por confirmar',
    message: urgent === 1
      ? 'Un pedido en línea lleva 15 minutos sin confirmar. A la media hora cuenta entre los más lentos en Explorar.'
      : `${urgent} pedidos en línea llevan 15 minutos o más sin confirmar. A la media hora cuentan entre los más lentos en Explorar.`,
  };
}

/**
 * Tells whoever handles the orders, on any screen, when an order of the online menu has waited 15 minutes without an answer
 * (Fase 29X, ADR 0078): the top bar mounts it. It reads the bell's count, which the server works out every minute, so it
 * tells within a minute; one that passes while another is confirmed in the same minute goes unsaid (the bell still counts
 * it). What was waiting when the screen opened is in the bell, not told again. It rings when this device has the sound of
 * the online orders on.
 */
export function useOnlineOrdersWaitingLive(enabled: boolean) {
  const attention = useAttention(enabled);
  const toast = useToast();
  const toastRef = useRef(toast);
  useEffect(() => {
    toastRef.current = toast;
  }, [toast]);
  const chime = useArrivalChime(ORDERS_SOUND_KEY);
  const playRef = useRef(chime.play);
  useEffect(() => {
    playRef.current = chime.play;
  }, [chime.play]);

  const urgent = attention.data
    ? attention.data.items.find((item) => item.key === 'onlineOrdersToConfirm')?.urgentCount ?? 0
    : null;
  const previous = useRef<number | null>(null);
  useEffect(() => {
    if (urgent === null) return;
    const before = previous.current;
    previous.current = urgent;
    if (before === null || urgent <= before) return;
    const { title, message } = describeWaiting(urgent);
    toastRef.current.warning(message, { title, durationMs: 10_000 });
    playRef.current('arrival');
  }, [urgent]);
}
