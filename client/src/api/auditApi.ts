import api from './axios';
import type { AuditLogDto, PaginatedList } from '../types';

export const auditApi = {
  getLogs: (params?: {
    entityName?: string;
    action?: string;
    from?: string;
    to?: string;
    pageNumber?: number;
    pageSize?: number;
  }) =>
    api.get<PaginatedList<AuditLogDto>>('/audit/logs', { params }),
};
