import api from './axios';
import type { OperationalReportDto } from '../types';

export const reportsApi = {
  getOperationalSummary: (params?: { fromDate?: string; toDate?: string }) =>
    api.get<OperationalReportDto>('/reports/operational-summary', { params }),
};
