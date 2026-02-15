import api from './axios';
import type {
  PaginatedList,
  ConversationDto,
  ChatMessageDto,
  SendChatMessageRequest,
  ApiMessage,
} from '../types';

export const chatApi = {
  getConversations: (page = 1, pageSize = 20, search?: string) =>
    api.get<PaginatedList<ConversationDto>>('/whatsapp/chat', {
      params: { pageNumber: page, pageSize, search },
    }),

  getMessages: (conversationId: string, page = 1, pageSize = 50) =>
    api.get<PaginatedList<ChatMessageDto>>(`/whatsapp/chat/${conversationId}/messages`, {
      params: { pageNumber: page, pageSize },
    }),

  sendMessage: (data: SendChatMessageRequest) =>
    api.post<ChatMessageDto>('/whatsapp/chat/send', data),

  markAsRead: (conversationId: string) =>
    api.put<ApiMessage>(`/whatsapp/chat/${conversationId}/read`),
};
