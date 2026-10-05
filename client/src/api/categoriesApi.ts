import api from './axios';
import type {
  CategoryDto,
  CategoryTemplateDto,
  CreateCategoryRequest,
  InstallCategoryTemplateResultDto,
  UpdateCategoryRequest,
} from '../types';

export const categoriesApi = {
  /** Active categories; the Categorías screen also asks for the archived ones to restore them. */
  getAll: (includeArchived = false) =>
    api.get<CategoryDto[]>('/categories', { params: includeArchived ? { includeArchived } : undefined }),

  getById: (id: string) =>
    api.get<CategoryDto>(`/categories/${id}`),

  create: (data: CreateCategoryRequest) =>
    api.post<CategoryDto>('/categories', data),

  update: (id: string, data: UpdateCategoryRequest) =>
    api.put<CategoryDto>(`/categories/${id}`, data),

  /** Archiving a category with products needs another active category to move them to. */
  toggleActive: (id: string, isActive: boolean, moveProductsToCategoryId?: string) =>
    api.patch(`/categories/${id}/active`, { isActive, moveProductsToCategoryId }),

  getTemplates: () =>
    api.get<CategoryTemplateDto[]>('/categories/templates'),

  installTemplate: (templateId: string) =>
    api.post<InstallCategoryTemplateResultDto>(`/categories/templates/${templateId}/install`),
};
