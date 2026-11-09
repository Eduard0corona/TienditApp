// Messaging: campaigns, templates, outgoing messages, reminders and opt-outs.

import type {
  CampaignAudienceKind, CampaignStatus, MessagePurpose, OptOutSource, OutgoingMessageStatus,
} from '../api/generated/enums';

// AppointmentNotice: booked, moved, confirmed or cancelled; queued by the system, it has no templates.
export { MessagePurpose } from '../api/generated/enums';

export { OutgoingMessageStatus } from '../api/generated/enums';

export { CampaignStatus } from '../api/generated/enums';

export { CampaignAudienceKind } from '../api/generated/enums';

export { OptOutSource } from '../api/generated/enums';

export interface MessageTemplateDto {
  id: string;
  name: string;
  purpose: MessagePurpose;
  body: string;
  /** Template approved in Meta; without it the text only reaches customers who wrote in the last 24 hours. */
  providerTemplateName?: string;
  languageCode: string;
  isActive: boolean;
  /** Placeholders in the order they become the Meta template parameters. */
  parameters: string[];
  createdAt: string;
  updatedAt?: string;
}

export interface SaveMessageTemplateRequest {
  name: string;
  purpose?: MessagePurpose;
  body: string;
  providerTemplateName?: string;
  languageCode?: string;
  isActive?: boolean;
}

export interface CampaignProgressDto {
  pending: number;
  sent: number;
  delivered: number;
  read: number;
  failed: number;
  skipped: number;
  cancelled: number;
}

export interface CampaignDto {
  id: string;
  name: string;
  templateId: string;
  templateName?: string;
  audienceKind: CampaignAudienceKind;
  audienceMinimumOrders: number;
  audienceDays?: number;
  status: CampaignStatus;
  scheduledAt: string;
  startedAt?: string;
  completedAt?: string;
  cancelledAt?: string;
  recipientCount: number;
  excludedCount: number;
  createdBy?: string;
  createdAt: string;
  progress: CampaignProgressDto;
}

export interface CampaignAudienceRequest {
  templateId: string;
  audienceKind: CampaignAudienceKind;
  minimumOrders: number;
  days?: number;
}

export interface CreateCampaignRequest extends CampaignAudienceRequest {
  name: string;
  /** Empty starts now. */
  scheduledAt?: string;
}

export interface CampaignPreviewDto {
  recipientCount: number;
  excludedCount: number;
  truncated: boolean;
  maxRecipients: number;
  usesApprovedTemplate: boolean;
  samples: Array<{ recipientName?: string; phone: string; text: string }>;
}

export interface OutgoingMessageDto {
  id: string;
  purpose: MessagePurpose;
  campaignId?: string;
  appointmentId?: string;
  phone: string;
  recipientName?: string;
  body: string;
  status: OutgoingMessageStatus;
  attempts: number;
  createdAt: string;
  nextAttemptAt: string;
  sentAt?: string;
  deliveredAt?: string;
  readAt?: string;
  lastError?: string;
}

export interface MessagingOptOutDto {
  id: string;
  phone: string;
  source: OptOutSource;
  optedOutAt: string;
  resubscribedAt?: string;
  isActive: boolean;
}

export interface MessagingSettingsDto {
  whatsAppConnected: boolean;
  appointmentsEnabled: boolean;
  appointmentRemindersEnabled: boolean;
  appointmentReminderHoursBefore: number;
  appointmentReminderTemplateId?: string;
  defaultReminderBody: string;
  minReminderHoursBefore: number;
  maxReminderHoursBefore: number;
  campaignPlaceholders: string[];
  reminderPlaceholders: string[];
  /** Fase 26C: the variables of a reminder of the fiado. */
  creditReminderPlaceholders: string[];
  creditDueSoonPlaceholders: string[];
}

export interface UpdateAppointmentRemindersRequest {
  enabled: boolean;
  hoursBefore: number;
  templateId?: string;
}
