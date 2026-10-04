import api from './axios';
import type {
  AddOrderPaymentRequest,
  CreateOrderRequest,
  OrderDto,
  OrderStatus,
  ServiceOrderPieceStatus,
  OrderType,
  PaginatedList,
  UploadedFileDto,
} from '../types';

export const ordersApi = {
  getAll: (params?: {
    orderType?: OrderType;
    status?: OrderStatus;
    fromDate?: string;
    toDate?: string;
    /** Folio, customer name or phone digits; searched on the server. */
    search?: string;
    pageNumber?: number;
    pageSize?: number;
  }) =>
    api.get<PaginatedList<OrderDto>>('/orders', { params }),

  getById: (id: string) =>
    api.get<OrderDto>(`/orders/${id}`),

  create: (data: CreateOrderRequest) =>
    api.post<OrderDto>('/orders', data),

  updateStatus: (id: string, newStatus: OrderStatus) =>
    api.put(`/orders/${id}/status`, { newStatus }),

  cancel: (id: string, reason?: string) =>
    api.put(`/orders/${id}/cancel`, { reason }),

  addPayment: (id: string, data: AddOrderPaymentRequest) =>
    api.post<OrderDto>(`/orders/${id}/payments`, data),

  uploadServicePhoto: (file: File) => {
    const formData = new FormData();
    formData.append('file', file);
    return api.post<UploadedFileDto>('/orders/uploads/service-photo', formData, {
      headers: { 'Content-Type': 'multipart/form-data' },
    });
  },

  uploadPaymentEvidence: (file: File) => {
    const formData = new FormData();
    formData.append('file', file);
    return api.post<UploadedFileDto>('/orders/uploads/payment-evidence', formData, {
      headers: { 'Content-Type': 'multipart/form-data' },
    });
  },

  updateServicePieceStatus: (orderId: string, pieceId: string, newStatus: ServiceOrderPieceStatus) =>
    api.put(`/orders/${orderId}/service-pieces/${pieceId}/status`, { newStatus }),
};
