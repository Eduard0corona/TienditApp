import api from './axios';
import type {
  CreateCustomerRequest,
  CustomerCardDto,
  CustomerDto,
  PaginatedList,
  UpdateCustomerRequest,
} from '../types';

export const customersApi = {
  getAll: (params?: {
    search?: string;
    isActive?: boolean;
    pageNumber?: number;
    pageSize?: number;
  }) =>
    api.get<PaginatedList<CustomerDto>>('/customers', { params }),

  getById: (id: string) =>
    api.get<CustomerDto>(`/customers/${id}`),

  /** Orders, balance, favorite products, appointments and opt-out of a customer, by phone or id. */
  getCard: (params: { phone?: string; customerId?: string }) =>
    api.get<CustomerCardDto>('/customers/card', { params }),

  create: (data: CreateCustomerRequest) =>
    api.post<CustomerDto>('/customers', data),

  update: (id: string, data: UpdateCustomerRequest) =>
    api.put<CustomerDto>(`/customers/${id}`, data),

  setActive: (id: string, isActive: boolean) =>
    api.patch(`/customers/${id}/active`, { isActive }),

  delete: (id: string) =>
    api.delete(`/customers/${id}`),
};

