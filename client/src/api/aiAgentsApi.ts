import api from './axios';
import type { AiAgentDto, CreateAiAgentRequest, UpdateAiAgentRequest } from '../types';

export const aiAgentsApi = {
  getAll: (params?: { isActive?: boolean }) =>
    api.get<AiAgentDto[]>('/ai-agents', { params }),

  getById: (id: string) =>
    api.get<AiAgentDto>(`/ai-agents/${id}`),

  create: (data: CreateAiAgentRequest) =>
    api.post<AiAgentDto>('/ai-agents', data),

  update: (id: string, data: UpdateAiAgentRequest) =>
    api.put<AiAgentDto>(`/ai-agents/${id}`, data),

  setActive: (id: string, isActive: boolean) =>
    api.patch(`/ai-agents/${id}/active`, { isActive }),

  delete: (id: string) =>
    api.delete(`/ai-agents/${id}`),
};
