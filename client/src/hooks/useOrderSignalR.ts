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

export function useOrderSignalR() {
  const connectionRef = useRef<HubConnection | null>(null);

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/orders', {
        accessTokenFactory: () => getAccessToken() || '',
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connectionRef.current = connection;

    connection.start().catch((err) => {
      console.error('OrderHub connection error:', err);
    });

    return () => {
      connection.stop();
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

  return { onOrderCreated, onOrderStatusChanged, onOrderCancelled };
}
