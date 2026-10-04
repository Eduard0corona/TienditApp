import api from './axios';
import type { KitchenOrderDto, KitchenTicketDto } from '../types';

export const kitchenApi = {
  getOrders: () =>
    api.get<KitchenOrderDto[]>('/kitchen/orders'),

  startPreparation: (orderId: string) =>
    api.put(`/kitchen/orders/${orderId}/start`, {}),

  markReady: (orderId: string) =>
    api.put(`/kitchen/orders/${orderId}/ready`, {}),

  /** Rounds of open tabs still in the kitchen or waiting to be taken to the table. */
  getTickets: () =>
    api.get<KitchenTicketDto[]>('/kitchen/tickets'),

  startTicket: (ticketId: string) =>
    api.put(`/kitchen/tickets/${ticketId}/start`, {}),

  markTicketReady: (ticketId: string) =>
    api.put(`/kitchen/tickets/${ticketId}/ready`, {}),

  markTicketServed: (ticketId: string) =>
    api.put(`/kitchen/tickets/${ticketId}/served`, {}),
};
