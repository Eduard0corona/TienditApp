import api from './axios';
import { apiRequest, type Schemas } from './typedClient';
import type { OperationalReportDto } from '../types';

export type SalesByBranchReport = Schemas['SalesByBranchReportDto'];

export const reportsApi = {
  getOperationalSummary: (params?: { fromDate?: string; toDate?: string }) =>
    api.get<OperationalReportDto>('/reports/operational-summary', { params }),

  /** Sales by branch and by day between two local dates (yyyy-mm-dd); the current month when omitted. */
  getSalesByBranch: (query: { from?: string; to?: string; branchId?: string }) =>
    apiRequest('get', '/api/v1/reports/sales-by-branch', { query }),
};
