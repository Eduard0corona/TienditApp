import api from './axios';
import type {
  ProductDto,
  ProductImageUrlsDto,
  PaginatedList,
  CreateProductRequest,
  UpdateProductRequest,
} from '../types';

export const productsApi = {
  getAll: (params?: {
    categoryId?: string;
    isAvailable?: boolean;
    pageNumber?: number;
    pageSize?: number;
  }) =>
    api.get<PaginatedList<ProductDto>>('/products', { params }),

  getById: (id: string) =>
    api.get<ProductDto>(`/products/${id}`),

  create: (data: CreateProductRequest) =>
    api.post<ProductDto>('/products', data),

  update: (id: string, data: UpdateProductRequest) =>
    api.put<ProductDto>(`/products/${id}`, data),

  delete: (id: string) =>
    api.delete(`/products/${id}`),

  toggleAvailability: (id: string, isAvailable: boolean) =>
    api.patch(`/products/${id}/availability`, { isAvailable }),

  uploadImage: (id: string, file: File) => {
    const formData = new FormData();
    formData.append('file', file);
    return api.post<ProductImageUrlsDto>(`/products/${id}/image`, formData, {
      headers: { 'Content-Type': 'multipart/form-data' },
    });
  },
};
