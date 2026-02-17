import api from './axios';
import type {
  CreateSupplierRequest,
  PaginatedList,
  SupplierDto,
  UpdateSupplierRequest,
} from '../types';

export const suppliersApi = {
  getAll: (params?: {
    search?: string;
    isActive?: boolean;
    pageNumber?: number;
    pageSize?: number;
  }) => api.get<PaginatedList<SupplierDto>>('/suppliers', { params }),

  getById: (id: string) =>
    api.get<SupplierDto>(`/suppliers/${id}`),

  create: (data: CreateSupplierRequest) =>
    api.post<SupplierDto>('/suppliers', data),

  update: (id: string, data: UpdateSupplierRequest) =>
    api.put<SupplierDto>(`/suppliers/${id}`, data),

  setActive: (id: string, isActive: boolean) =>
    api.patch(`/suppliers/${id}/active`, { isActive }),
};
