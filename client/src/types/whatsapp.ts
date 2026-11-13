// WhatsApp: the connection of the business and its chat.

export interface WhatsAppConfigDto {
  id: string;
  tenantId: string;
  isEnabled: boolean;
  phoneNumberId?: string;
  wabaId?: string;
  displayPhoneNumber?: string;
  hasAccessToken: boolean;
  verifyToken?: string;
}

export interface UpdateWhatsAppConfigRequest {
  isEnabled: boolean;
  accessToken?: string;
  phoneNumberId?: string;
  wabaId?: string;
  displayPhoneNumber?: string;
}

export interface SendWhatsAppMessageRequest {
  phoneNumber: string;
  message: string;
}

export interface ConversationDto {
  id: string;
  customerPhone: string;
  customerName?: string;
  lastMessageBody?: string;
  lastMessageAt: string;
  unreadCount: number;
  /** The automatic replies stay quiet until then (a person answered or paused them). */
  autoRepliesPausedUntil?: string | null;
}

export interface ChatMessageDto {
  id: string;
  conversationId: string;
  direction: string;
  body: string;
  status: string;
  sentAt: string;
  /** A photo ("Image") or an audio ("Audio") the customer sent, which the chat shows (Fase 31L). */
  mediaKind?: string | null;
  /** "Pending" while it downloads, "Saved" once kept, "Unavailable" when only its marker stays. */
  mediaState?: string | null;
  /** The private file ("/files/whatsapp/..."), opened with the session, once kept. */
  mediaPath?: string | null;
}

export interface SendChatMessageRequest {
  phoneNumber: string;
  message: string;
  conversationId?: string;
}
