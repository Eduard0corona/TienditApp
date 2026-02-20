import api from './axios';
import type {
  BranchDto,
  CreateBranchRequest,
  UpdateBranchRequest,
} from '../types';

export const branchesApi = {
  getAll: (params?: { isActive?: boolean }) =>
    api.get<BranchDto[]>('/branches', { params }),

  create: (data: CreateBranchRequest) =>
    api.post<BranchDto>('/branches', data),

  update: (id: string, data: UpdateBranchRequest) =>
    api.put<BranchDto>(`/branches/${id}`, data),

  setActive: (id: string, isActive: boolean) =>
    api.patch(`/branches/${id}/active`, { isActive }),
};
