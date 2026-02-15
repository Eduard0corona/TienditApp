import api from './axios';
import type {
  CategoryDto,
  CreateCategoryRequest,
  UpdateCategoryRequest,
} from '../types';

export const categoriesApi = {
  getAll: () =>
    api.get<CategoryDto[]>('/categories'),

  getById: (id: string) =>
    api.get<CategoryDto>(`/categories/${id}`),

  create: (data: CreateCategoryRequest) =>
    api.post<CategoryDto>('/categories', data),

  update: (id: string, data: UpdateCategoryRequest) =>
    api.put<CategoryDto>(`/categories/${id}`, data),

  delete: (id: string) =>
    api.delete(`/categories/${id}`),

  toggleActive: (id: string, isActive: boolean) =>
    api.patch(`/categories/${id}/active`, { isActive }),
};
