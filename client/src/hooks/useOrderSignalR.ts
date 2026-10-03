import { useCallback, useEffect, useRef } from 'react';
import type { HubConnection } from '@microsoft/signalr';
import { createHubConnection, keepHubConnected } from '../api/signalr';

export interface OrderCreatedPayload {
  orderId: string;
  orderNumber: string;
  businessType: string;
  total: number;
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

export interface ServiceBoardPieceUpdatedPayload {
  orderId: string;
  pieceId: string;
  status: string;
  assignedToUserId?: string;
}

export function useOrderSignalR() {
  const connectionRef = useRef<HubConnection | null>(null);

  useEffect(() => {
    let isDisposed = false;
    const connection = createHubConnection('/hubs/orders');
    connectionRef.current = connection;
    const stop = keepHubConnected(connection, 'OrderHub', () => isDisposed);

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

  return { onOrderCreated, onOrderStatusChanged, onOrderCancelled, onServiceBoardPieceUpdated };
}
