import api from './axios';
import type { SendPromotionRequest } from '../types';

export const marketingApi = {
  sendPromotion: (data: SendPromotionRequest) =>
    api.post<{ sent: number }>('/marketing/promotions/send', data),
};
