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
    onlyAvailableNow?: boolean;
    itemType?: 'Product' | 'Service';
    /** Name, SKU or exact barcode; searched on the server. */
    search?: string;
    pageNumber?: number;
    pageSize?: number;
  }) =>
    api.get<PaginatedList<ProductDto>>('/products', { params }),

  getById: (id: string) =>
    api.get<ProductDto>(`/products/${id}`),

  create: (data: CreateProductRequest, itemType: 'Product' | 'Service' = 'Product') =>
    api.post<ProductDto>('/products', data, { params: { itemType } }),

  update: (id: string, data: UpdateProductRequest, itemType: 'Product' | 'Service' = 'Product') =>
    api.put<ProductDto>(`/products/${id}`, data, { params: { itemType } }),

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
