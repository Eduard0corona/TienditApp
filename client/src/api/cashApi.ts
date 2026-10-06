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
  /**
   * The drawer the user's payments enter when selling at a branch (their own, else the only one open there);
   * empty when a payment would be refused for lack of a drawer.
   */
  getCurrentSession: (branchId?: string) =>
    api.get<CashSessionDto | null>('/cash/current-session', { params: { branchId } }),

  openSession: (data: OpenCashSessionRequest) =>
    api.post<CashSessionDto>('/cash/open', data),

  closeSession: (id: string, data: CloseCashSessionRequest) =>
    api.post<CashSessionDto>(`/cash/${id}/close`, data),

  /** Shift history with expected, counted and difference per drawer. */
  getSessions: (params?: {
    fromDate?: string;
    toDate?: string;
    branchId?: string;
    status?: number;
    onlyWithDifference?: boolean;
    pageNumber?: number;
    pageSize?: number;
  }) => api.get<PaginatedList<CashSessionDto>>('/cash/sessions', { params }),

  getTransactions: (params?: {
    cashSessionId?: string;
    transactionType?: number;
    fromDate?: string;
    toDate?: string;
    /** Reference, notes or who registered it, over every page. */
    search?: string;
    pageNumber?: number;
    pageSize?: number;
  }) => api.get<PaginatedList<CashTransactionDto>>('/cash/transactions', { params }),

  getDailyCut: (params?: {
    date?: string;
    branchId?: string;
    userName?: string;
  }) => api.get<DailyCashCutDto>('/cash/daily-cut', { params }),

  addTransaction: (data: AddCashTransactionRequest) =>
    api.post<CashTransactionDto>('/cash/transactions', data),
};
