import api from './axios';
import type {
  CreateEmployeeRequest,
  EmployeeDto,
  EmployeePermissionOptionDto,
  PaginatedList,
  UpdateEmployeeRequest,
} from '../types';

export const employeesApi = {
  getAll: (params?: {
    search?: string;
    appRole?: string;
    tenantRole?: string;
    status?: string;
    pageNumber?: number;
    pageSize?: number;
  }) =>
    api.get<PaginatedList<EmployeeDto>>('/employees', { params }),

  getById: (tenantUserId: string) =>
    api.get<EmployeeDto>(`/employees/${tenantUserId}`),

  getPermissionOptions: () =>
    api.get<EmployeePermissionOptionDto[]>('/employees/permission-options'),

  create: (data: CreateEmployeeRequest) =>
    api.post<EmployeeDto>('/employees', data),

  update: (tenantUserId: string, data: UpdateEmployeeRequest) =>
    api.put<EmployeeDto>(`/employees/${tenantUserId}`, data),

  setActive: (tenantUserId: string, isActive: boolean) =>
    api.patch(`/employees/${tenantUserId}/active`, { isActive }),
};
