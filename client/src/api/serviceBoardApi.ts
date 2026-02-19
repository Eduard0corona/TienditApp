import api from './axios';
import type { PaginatedList, ServiceBoardPieceDto, ServiceOrderPieceStatus } from '../types';

export const serviceBoardApi = {
  getPieces: (params?: {
    search?: string;
    status?: ServiceOrderPieceStatus;
    includeCompleted?: boolean;
    includeCancelled?: boolean;
    onlyMine?: boolean;
    pageNumber?: number;
    pageSize?: number;
  }) =>
    api.get<PaginatedList<ServiceBoardPieceDto>>('/service-board/pieces', { params }),

  assignToMe: (pieceId: string) =>
    api.put(`/service-board/pieces/${pieceId}/assign-to-me`, {}),

  release: (pieceId: string) =>
    api.put(`/service-board/pieces/${pieceId}/release`, {}),
};
