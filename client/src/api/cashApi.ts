import api from './axios';
import type {
  AddCashTransactionRequest,
  CashSessionDto,
  CashTransactionDto,
  CloseCashSessionRequest,
  DailyCashCutDto,
  OpenCashSessionRequest,
  PaginatedList,
} from '../types';

export const cashApi = {
  getCurrentSession: () =>
    api.get<CashSessionDto | null>('/cash/current-session'),

  openSession: (data: OpenCashSessionRequest) =>
    api.post<CashSessionDto>('/cash/open', data),

  closeSession: (id: string, data: CloseCashSessionRequest) =>
    api.post<CashSessionDto>(`/cash/${id}/close`, data),

  getTransactions: (params?: {
    cashSessionId?: string;
    transactionType?: number;
    fromDate?: string;
    toDate?: string;
    pageNumber?: number;
    pageSize?: number;
  }) => api.get<PaginatedList<CashTransactionDto>>('/cash/transactions', { params }),

  getDailyCut: (params?: {
    date?: string;
    inventoryLocationId?: string;
    userName?: string;
  }) => api.get<DailyCashCutDto>('/cash/daily-cut', { params }),

  addTransaction: (data: AddCashTransactionRequest) =>
    api.post<CashTransactionDto>('/cash/transactions', data),
};
