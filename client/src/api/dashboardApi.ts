import api from './axios';
import type { DashboardMetricsDto } from '../types';

export const dashboardApi = {
  getMetrics: (params?: { from?: string; to?: string }) =>
    api.get<DashboardMetricsDto>('/dashboard/metrics', { params }),
};
