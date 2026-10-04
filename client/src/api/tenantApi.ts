import api from './axios';
import type {
  InstallStarterCatalogResultDto,
  TenantSalesPolicyDto,
  TenantSetupDto,
  TenantProfileDto,
  UpdateTenantRequest,
  LogoImageUrlsDto,
  WhatsAppConfigDto,
  UpdateWhatsAppConfigRequest,
  SendWhatsAppMessageRequest,
  ApiMessage,
} from '../types';

export const tenantApi = {
  getMyTenant: () =>
    api.get<TenantProfileDto>('/auth/my-tenant'),

  /** How future sales are priced (IVA) and closed. Past orders keep the policy they were sold with. */
  updateSalesPolicy: (data: TenantSalesPolicyDto) =>
    api.put<TenantSalesPolicyDto>('/auth/my-tenant/sales-policy', data),

  getSetup: () =>
    api.get<TenantSetupDto>('/auth/my-tenant/setup'),

  completeSetup: () =>
    api.post('/auth/my-tenant/setup/complete'),

  /** Fills an empty catalog with a sample one; refused once the business has products. */
  installStarterCatalog: (templateId: string) =>
    api.post<InstallStarterCatalogResultDto>('/auth/my-tenant/setup/starter-catalog', { templateId }),

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

  getWhatsAppConfig: () =>
    api.get<WhatsAppConfigDto>('/auth/my-tenant/whatsapp'),

  updateWhatsAppConfig: (data: UpdateWhatsAppConfigRequest) =>
    api.put<WhatsAppConfigDto>('/auth/my-tenant/whatsapp', data),

  sendWhatsAppMessage: (data: SendWhatsAppMessageRequest) =>
    api.post<ApiMessage>('/auth/my-tenant/whatsapp/send', data),
};
