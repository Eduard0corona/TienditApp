import { useCallback, useEffect, useRef } from 'react';
import { HubConnectionBuilder, HubConnection, LogLevel } from '@microsoft/signalr';
import { getAccessToken } from '../api/axios';

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

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/orders', {
        accessTokenFactory: () => getAccessToken() || '',
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connectionRef.current = connection;

    const startConnection = async () => {
      try {
        await connection.start();
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        const stoppedDuringNegotiation = message.toLowerCase().includes('stopped during negotiation');

        // In React StrictMode the effect is mounted/unmounted twice in dev.
        // Ignore negotiation errors caused by intentional teardown.
        if (!isDisposed && !stoppedDuringNegotiation) {
          console.error('OrderHub connection error:', err);
        }
      }
    };

    // Delay start one tick to avoid StrictMode mount/unmount negotiation races in dev.
    const startTimeoutId = window.setTimeout(() => {
      void startConnection();
    }, 0);

    return () => {
      isDisposed = true;
      window.clearTimeout(startTimeoutId);
      void connection.stop().catch(() => undefined);
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
