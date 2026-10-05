import api from './axios';
import type {
  AppointmentDto,
  AppointmentStatus,
  CreateAppointmentRequest,
  PaginatedList,
} from '../types';

export const appointmentsApi = {
  getAll: (params?: {
    status?: AppointmentStatus;
    fromDate?: string;
    toDate?: string;
    search?: string;
    pageNumber?: number;
    pageSize?: number;
  }) => api.get<PaginatedList<AppointmentDto>>('/appointments', { params }),

  getById: (id: string) =>
    api.get<AppointmentDto>(`/appointments/${id}`),

  create: (data: CreateAppointmentRequest) =>
    api.post<AppointmentDto>('/appointments', data),

  /** Edits an open appointment, including moving it to another time. */
  update: (id: string, data: CreateAppointmentRequest) =>
    api.put<AppointmentDto>(`/appointments/${id}`, data),

  updateStatus: (id: string, newStatus: AppointmentStatus) =>
    api.put<AppointmentDto>(`/appointments/${id}/status`, { newStatus }),
};
