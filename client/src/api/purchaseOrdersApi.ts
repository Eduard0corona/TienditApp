import api from './axios';
import type {
  CreatePurchaseOrderRequest,
  PaginatedList,
  PurchaseOrderDto,
  PurchaseOrderStatus,
  ReceivePurchaseOrderRequest,
} from '../types';

export const purchaseOrdersApi = {
  getAll: (params?: {
    supplierId?: string;
    status?: PurchaseOrderStatus;
    fromDate?: string;
    toDate?: string;
    pageNumber?: number;
    pageSize?: number;
  }) => api.get<PaginatedList<PurchaseOrderDto>>('/purchaseorders', { params }),

  getById: (id: string) =>
    api.get<PurchaseOrderDto>(`/purchaseorders/${id}`),

  create: (data: CreatePurchaseOrderRequest) =>
    api.post<PurchaseOrderDto>('/purchaseorders', data),

  receive: (id: string, data: ReceivePurchaseOrderRequest) =>
    api.post<PurchaseOrderDto>(`/purchaseorders/${id}/receive`, data),

  /** Draft → sent to the supplier. */
  send: (id: string) =>
    api.post<PurchaseOrderDto>(`/purchaseorders/${id}/send`),

  /** Part arrived and the rest will not: closes with what came in. */
  close: (id: string, reason?: string) =>
    api.post<PurchaseOrderDto>(`/purchaseorders/${id}/close`, { reason }),

  /** Only while nothing has been received. */
  cancel: (id: string) =>
    api.post<PurchaseOrderDto>(`/purchaseorders/${id}/cancel`),
};
