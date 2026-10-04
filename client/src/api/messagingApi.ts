import api from './axios';
import type {
  CampaignDto,
  CampaignPreviewDto,
  CampaignAudienceRequest,
  CampaignStatus,
  CreateCampaignRequest,
  MessagePurpose,
  MessageTemplateDto,
  MessagingOptOutDto,
  MessagingSettingsDto,
  OutgoingMessageDto,
  OutgoingMessageStatus,
  PaginatedList,
  SaveMessageTemplateRequest,
  UpdateAppointmentRemindersRequest,
} from '../types';

/** Templates, campaigns, appointment reminders and opt-outs (sent by WhatsApp in the background). */
export const messagingApi = {
  getTemplates: (params?: { purpose?: MessagePurpose; includeInactive?: boolean }) =>
    api.get<MessageTemplateDto[]>('/messaging/templates', { params }),

  createTemplate: (data: SaveMessageTemplateRequest) =>
    api.post<MessageTemplateDto>('/messaging/templates', data),

  updateTemplate: (id: string, data: SaveMessageTemplateRequest) =>
    api.put<MessageTemplateDto>(`/messaging/templates/${id}`, data),

  getCampaigns: (params: { status?: CampaignStatus; pageNumber: number; pageSize: number }) =>
    api.get<PaginatedList<CampaignDto>>('/messaging/campaigns', { params }),

  getCampaign: (id: string) =>
    api.get<CampaignDto>(`/messaging/campaigns/${id}`),

  previewCampaign: (data: CampaignAudienceRequest) =>
    api.post<CampaignPreviewDto>('/messaging/campaigns/preview', data),

  createCampaign: (data: CreateCampaignRequest) =>
    api.post<CampaignDto>('/messaging/campaigns', data),

  cancelCampaign: (id: string) =>
    api.post<CampaignDto>(`/messaging/campaigns/${id}/cancel`),

  getMessages: (params: {
    campaignId?: string;
    purpose?: MessagePurpose;
    status?: OutgoingMessageStatus;
    search?: string;
    pageNumber: number;
    pageSize: number;
  }) => api.get<PaginatedList<OutgoingMessageDto>>('/messaging/messages', { params }),

  getOptOuts: (params: { search?: string; includeResubscribed?: boolean; pageNumber: number; pageSize: number }) =>
    api.get<PaginatedList<MessagingOptOutDto>>('/messaging/opt-outs', { params }),

  setOptOut: (phone: string, optedOut: boolean) =>
    api.post<void>('/messaging/opt-outs', { phone, optedOut }),

  getSettings: () => api.get<MessagingSettingsDto>('/messaging/settings'),

  updateReminders: (data: UpdateAppointmentRemindersRequest) =>
    api.put<MessagingSettingsDto>('/messaging/settings/reminders', data),
};
