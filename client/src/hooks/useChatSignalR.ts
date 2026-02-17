import { useEffect, useRef, useCallback } from 'react';
import { HubConnectionBuilder, HubConnection, LogLevel } from '@microsoft/signalr';
import { getAccessToken } from '../api/axios';
import type { ConversationDto, ChatMessageDto } from '../types';

interface NewMessagePayload {
  conversation: ConversationDto;
  message: ChatMessageDto;
}

export function useChatSignalR() {
  const connectionRef = useRef<HubConnection | null>(null);

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/chat', {
        accessTokenFactory: () => getAccessToken() || '',
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connectionRef.current = connection;

    connection.start().catch((err) => {
      console.error('ChatHub connection error:', err);
    });

    return () => {
      connection.stop();
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

  return { onNewMessage, onConversationUpdated };
}
