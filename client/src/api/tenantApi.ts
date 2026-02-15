import api from './axios';
import type { TenantProfileDto, UpdateTenantRequest, LogoImageUrlsDto } from '../types';

export const tenantApi = {
  getMyTenant: () =>
    api.get<TenantProfileDto>('/auth/my-tenant'),

  updateMyTenant: (data: UpdateTenantRequest) =>
    api.put<TenantProfileDto>('/auth/my-tenant', data),

  uploadLogo: (file: File) => {
    const formData = new FormData();
    formData.append('file', file);
    return api.post<LogoImageUrlsDto>('/auth/my-tenant/logo', formData, {
      headers: { 'Content-Type': 'multipart/form-data' },
    });
  },

  deleteLogo: () =>
    api.delete('/auth/my-tenant/logo'),
};
