import api from './axios';
import { apiRequest, type Schemas } from './typedClient';
import type {
  AppointmentDto,
  AppointmentStatus,
  CreateAppointmentRequest,
  PaginatedList,
} from '../types';

export type OnlineBookingSettings = Schemas['OnlineBookingSettingsDto'];
export type OnlineBookingStaffSettings = Schemas['OnlineBookingStaffSettingsDto'];
export type OnlineBookingStaffMember = Schemas['OnlineBookingStaffMemberDto'];
export type UpdateOnlineBookingStaff = Schemas['UpdateOnlineBookingStaffRequest'];

export const appointmentsApi = {
  getAll: (params?: {
    status?: AppointmentStatus;
    fromDate?: string;
    toDate?: string;
    search?: string;
    /** Only those at the customer's address (true) or at the business (false). */
    homeVisit?: boolean;
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

  /** A request booked online (Fase 24D) becomes a scheduled appointment, and its customer is told. */
  accept: (id: string) => apiRequest('post', '/api/v1/appointments/{id}/accept', { path: { id } }),

  /** A request booked online is declined: its time is free again, and its customer is told. */
  decline: (id: string) => apiRequest('post', '/api/v1/appointments/{id}/decline', { path: { id } }),

  /** Whether customers book from the online menu, how many appointments at once, and what is missing for it. */
  getOnlineBooking: (signal?: AbortSignal) => apiRequest('get', '/api/v1/appointments/online-booking', { signal }),

  updateOnlineBooking: (enabled: boolean, capacity: number) =>
    apiRequest('put', '/api/v1/appointments/online-booking', { body: { enabled, capacity } }),

  /** The active team, with who customers book online, their services and hours (Fase 24J, owners and admins). */
  getOnlineBookingStaff: (signal?: AbortSignal) => apiRequest('get', '/api/v1/appointments/online-booking/staff', { signal }),

  /** Sets what customers see of a team member and when they book them online. */
  updateOnlineBookingStaff: (userId: string, body: UpdateOnlineBookingStaff) =>
    apiRequest('put', '/api/v1/appointments/online-booking/staff/{userId}', { path: { userId }, body }),
};
