import api from './axios';
import { apiRequest, type Schemas } from './typedClient';
import type { OperationalReportDto, OrderChannel } from '../types';

export type SalesByBranchReport = Schemas['SalesByBranchReportDto'];
export type SalesByChannelReport = Schemas['SalesByChannelReportDto'];
export type ChannelSales = Schemas['ChannelSalesDto'];
export type DeliveryPlatformOrder = Schemas['DeliveryPlatformOrderDto'];
export type DeliveryPlatformOrdersPage = Schemas['DeliveryPlatformOrderDtoPaginatedList'];

/** A range of local dates of the business (yyyy-mm-dd); the server fills in the current month when omitted. */
interface ReportRangeQuery {
  from?: string;
  to?: string;
  branchId?: string;
}

export const reportsApi = {
  getOperationalSummary: (params?: { fromDate?: string; toDate?: string }) =>
    api.get<OperationalReportDto>('/reports/operational-summary', { params }),

  /** Sales by branch and by day between two local dates (yyyy-mm-dd); the current month when omitted. */
  getSalesByBranch: (query: ReportRangeQuery) =>
    apiRequest('get', '/api/v1/reports/sales-by-branch', { query }),

  /**
   * Sales by where the orders came from, on the same cash basis as Inicio and sales by branch, with what each delivery
   * platform charged, its commission and its IVA, what it withheld and what it should deposit.
   */
  getSalesByChannel: (query: ReportRangeQuery, signal?: AbortSignal) =>
    apiRequest('get', '/api/v1/reports/sales-by-channel', { query, signal }),

  /** The orders of one delivery platform registered in the range, the newest first, with the amounts each one copied. */
  getDeliveryPlatformOrders: (
    query: ReportRangeQuery & { channel: OrderChannel; search?: string; pageNumber: number; pageSize: number },
    signal?: AbortSignal,
  ) => apiRequest('get', '/api/v1/reports/delivery-platform-orders', { query, signal }),
};
