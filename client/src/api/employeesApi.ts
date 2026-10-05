import api from './axios';
import type { EmployeeDto, PaginatedList } from '../types';

/** The team as other screens read it (who attends an appointment). The team screen uses the typed client. */
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
};
