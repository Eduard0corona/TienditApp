import api from './axios';
import type {
  AddOrderPaymentRequest,
  CancelTabItemRequest,
  CreateOrderItemRequest,
  CreateOrderRequest,
  OrderChannel,
  OrderDto,
  OrderStatus,
  ServiceOrderPieceStatus,
  OrderType,
  PaginatedList,
  RefundOrderRequest,
  RegisterCourierShipmentRequest,
  UploadedFileDto,
  VoidOrderRequest,
} from '../types';

export const ordersApi = {
  getAll: (params?: {
    orderType?: OrderType;
    status?: OrderStatus;
    /** The counter, a WhatsApp conversation or the online menu. */
    channel?: OrderChannel;
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

  /** Writes down (or corrects) the courier's trip of an order sent to an address: its folio and, optionally, its link. */
  registerCourierShipment: (id: string, request: RegisterCourierShipmentRequest) =>
    api.put(`/orders/${id}/delivery/shipment`, request),

  /** Cancels an order; what it consumed goes back to the stock unless `restockItems` is false. */
  cancel: (id: string, reason?: string, restockItems?: boolean) =>
    api.put(`/orders/${id}/cancel`, { reason, restockItems }),

  addPayment: (id: string, data: AddOrderPaymentRequest) =>
    api.post<OrderDto>(`/orders/${id}/payments`, data),

  /** Gives money back, optionally for returned units. */
  refund: (id: string, data: RefundOrderRequest) =>
    api.post<OrderDto>(`/orders/${id}/refunds`, data),

  /** Refunds every payment, returns the units and cancels when the state allows it. */
  voidOrder: (id: string, data: VoidOrderRequest) =>
    api.post<OrderDto>(`/orders/${id}/void`, data),

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

  /** Adds a round to an open tab and sends it to the kitchen. */
  addRound: (orderId: string, items: CreateOrderItemRequest[]) =>
    api.post<OrderDto>(`/orders/${orderId}/rounds`, { items }),

  /** Closes a paid tab and frees its table. */
  closeTab: (orderId: string) =>
    api.post<OrderDto>(`/orders/${orderId}/close-tab`, {}),

  moveTab: (orderId: string, diningTableId: string) =>
    api.put<OrderDto>(`/orders/${orderId}/table`, { diningTableId }),

  /** Cancels units of a dish already sent to the kitchen: the tab stops charging them. */
  cancelTabItem: (orderId: string, request: CancelTabItemRequest) =>
    api.post<OrderDto>(`/orders/${orderId}/item-cancellations`, request),

  /** The waiter took a ready round to the table. */
  markRoundServed: (ticketId: string) =>
    api.put(`/orders/kitchen-tickets/${ticketId}/served`, {}),
};
