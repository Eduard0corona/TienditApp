import api from './axios';
import type {
  AddOrderPaymentRequest,
  CreateOrderRequest,
  OrderDto,
  OrderStatus,
  ServiceOrderPieceStatus,
  OrderType,
  PaginatedList,
} from '../types';

export const ordersApi = {
  getAll: (params?: {
    orderType?: OrderType;
    status?: OrderStatus;
    fromDate?: string;
    toDate?: string;
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

  updateServicePieceStatus: (orderId: string, pieceId: string, newStatus: ServiceOrderPieceStatus) =>
    api.put(`/orders/${orderId}/service-pieces/${pieceId}/status`, { newStatus }),
};
