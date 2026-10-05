import api from './axios';
import type { AuditLogDto, PaginatedList } from '../types';

export const auditApi = {
  getLogs: (params?: {
    entityName?: string;
    action?: string;
    from?: string;
    to?: string;
    /** What changed, the action or who did it, over every page. */
    search?: string;
    pageNumber?: number;
    pageSize?: number;
  }) =>
    api.get<PaginatedList<AuditLogDto>>('/audit/logs', { params }),
};
