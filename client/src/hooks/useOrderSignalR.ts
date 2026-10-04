import { useCallback, useEffect, useRef } from 'react';
import type { HubConnection } from '@microsoft/signalr';
import { createHubConnection, keepHubConnected } from '../api/signalr';

export interface OrderCreatedPayload {
  orderId: string;
  orderNumber: string;
  businessType: string;
  total: number;
  /** Status name the order was created in ("Pending" waits for the business to confirm it). */
  status?: string;
}

export interface OrderStatusChangedPayload {
  orderId: string;
  previousStatus: string;
  newStatus: string;
}

export interface OrderCancelledPayload {
  orderId: string;
  previousStatus: string;
  reason?: string;
}

export interface KitchenTicketsChangedPayload {
  orderId: string;
  round: number;
}

export interface ServiceBoardPieceUpdatedPayload {
  orderId: string;
  pieceId: string;
  status: string;
  assignedToUserId?: string;
}

export function useOrderSignalR() {
  const connectionRef = useRef<HubConnection | null>(null);
  const connectedListenersRef = useRef(new Set<() => void>());

  useEffect(() => {
    let isDisposed = false;
    const connection = createHubConnection('/hubs/orders');
    connectionRef.current = connection;
    const stop = keepHubConnected(connection, 'OrderHub', () => isDisposed, () => {
      connectedListenersRef.current.forEach((listener) => listener());
    });

    return () => {
      isDisposed = true;
      stop();
    };
  }, []);

  const onOrderCreated = useCallback((callback: (data: OrderCreatedPayload) => void) => {
    const conn = connectionRef.current;
    if (!conn) return () => {};

    conn.on('OrderCreated', callback);
    return () => conn.off('OrderCreated', callback);
  }, []);

  const onOrderStatusChanged = useCallback((callback: (data: OrderStatusChangedPayload) => void) => {
    const conn = connectionRef.current;
    if (!conn) return () => {};

    conn.on('OrderStatusChanged', callback);
    return () => conn.off('OrderStatusChanged', callback);
  }, []);

  const onOrderCancelled = useCallback((callback: (data: OrderCancelledPayload) => void) => {
    const conn = connectionRef.current;
    if (!conn) return () => {};

    conn.on('OrderCancelled', callback);
    return () => conn.off('OrderCancelled', callback);
  }, []);

  const onServiceBoardPieceUpdated = useCallback((callback: (data: ServiceBoardPieceUpdatedPayload) => void) => {
    const conn = connectionRef.current;
    if (!conn) return () => {};

    conn.on('ServiceBoardPieceUpdated', callback);
    return () => conn.off('ServiceBoardPieceUpdated', callback);
  }, []);

  /** A round of an open tab was sent or moved in the kitchen. */
  const onKitchenTicketsChanged = useCallback((callback: (data: KitchenTicketsChangedPayload) => void) => {
    const conn = connectionRef.current;
    if (!conn) return () => {};

    conn.on('KitchenTicketsChanged', callback);
    return () => conn.off('KitchenTicketsChanged', callback);
  }, []);

  /** Runs each time the hub (re)connects; events sent while disconnected are not replayed. */
  const onConnected = useCallback((callback: () => void) => {
    const listeners = connectedListenersRef.current;
    listeners.add(callback);
    return () => {
      listeners.delete(callback);
    };
  }, []);

  return { onOrderCreated, onOrderStatusChanged, onOrderCancelled, onServiceBoardPieceUpdated, onKitchenTicketsChanged, onConnected };
}
