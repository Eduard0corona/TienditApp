import { useEffect, useRef, useCallback } from 'react';
import type { HubConnection } from '@microsoft/signalr';
import { createHubConnection, keepHubConnected } from '../api/signalr';
import type { ConversationDto, ChatMessageDto } from '../types';

interface NewMessagePayload {
  conversation: ConversationDto;
  message: ChatMessageDto;
}

/** A sent message was delivered, read or failed (the ticks). */
export interface MessageStatusPayload {
  conversationId: string;
  messageId: string;
  status: string;
}

export function useChatSignalR() {
  const connectionRef = useRef<HubConnection | null>(null);

  useEffect(() => {
    let isDisposed = false;
    const connection = createHubConnection('/hubs/chat');
    connectionRef.current = connection;
    const stop = keepHubConnected(connection, 'ChatHub', () => isDisposed);

    return () => {
      isDisposed = true;
      stop();
    };
  }, []);

  const onNewMessage = useCallback(
    (callback: (data: NewMessagePayload) => void) => {
      const conn = connectionRef.current;
      if (!conn) return () => {};

      conn.on('NewMessage', callback);
      return () => conn.off('NewMessage', callback);
    },
    []
  );

  const onConversationUpdated = useCallback(
    (callback: (data: ConversationDto) => void) => {
      const conn = connectionRef.current;
      if (!conn) return () => {};

      conn.on('ConversationUpdated', callback);
      return () => conn.off('ConversationUpdated', callback);
    },
    []
  );

  const onMessageStatus = useCallback(
    (callback: (data: MessageStatusPayload) => void) => {
      const conn = connectionRef.current;
      if (!conn) return () => {};

      conn.on('MessageStatus', callback);
      return () => conn.off('MessageStatus', callback);
    },
    []
  );

  return { onNewMessage, onConversationUpdated, onMessageStatus };
}
