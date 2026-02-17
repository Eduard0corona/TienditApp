import api from './axios';
import type { KitchenOrderDto } from '../types';

export const kitchenApi = {
  getOrders: () =>
    api.get<KitchenOrderDto[]>('/kitchen/orders'),

  startPreparation: (orderId: string) =>
    api.put(`/kitchen/orders/${orderId}/start`, {}),

  markReady: (orderId: string) =>
    api.put(`/kitchen/orders/${orderId}/ready`, {}),
};
