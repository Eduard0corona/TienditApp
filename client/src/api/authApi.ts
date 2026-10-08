import api from './axios';
import type {
  AuthResponse,
  LoginRequest,
  TwoFactorLoginRequest,
  ApiMessage,
  RegisterBusinessRequest,
  RegisterCustomerRequest,
  MyTenantDto,
  BusinessType,
} from '../types';

export const authApi = {
  login: (data: LoginRequest) =>
    api.post<AuthResponse>('/auth/login', data),

  loginTwoFactor: (data: TwoFactorLoginRequest) =>
    api.post<AuthResponse>('/twofactor/login', data),

  logout: () =>
    api.post<ApiMessage>('/auth/logout'),

  refreshToken: () =>
    api.post<AuthResponse>('/auth/refresh-token'),

  confirmPhone: (userId: string, code: string) =>
    api.post<ApiMessage>('/auth/confirm-phone', { userId, code }),

  /** New SMS code for the account created at registration (identified by its user id). */
  resendPhoneConfirmation: (userId: string) =>
    api.post<ApiMessage>('/auth/resend-phone-confirmation', { userId }),

  confirmEmail: (userId: string, token: string) =>
    api.get<ApiMessage>('/auth/confirm-email', { params: { userId, token } }),

  resendConfirmationEmail: (email: string) =>
    api.post<ApiMessage>('/auth/resend-confirmation-email', { email }),

  forgotPassword: (email: string) =>
    api.post<ApiMessage>('/auth/forgot-password', { email }),

  resetPassword: (userId: string, token: string, newPassword: string) =>
    api.post<ApiMessage>('/auth/reset-password', { userId, token, newPassword }),

  /** Creates the account and emails the link that activates it; it opens no session. */
  registerBusiness: (data: RegisterBusinessRequest) =>
    api.post<ApiMessage>('/auth/register/business', data),

  /** Creates the account and emails the link that activates it; it opens no session. */
  registerCustomer: (data: RegisterCustomerRequest) =>
    api.post<ApiMessage>('/auth/register/customer', data),

  getMyTenants: () =>
    api.get<MyTenantDto[]>('/me/tenants'),

  switchTenant: (tenantId: string) =>
    api.post<AuthResponse>('/auth/switch-tenant', { tenantId }),

  /** Opens another business with this account, as its owner. */
  createBusiness: (data: { businessName: string; businessType: BusinessType }) =>
    api.post<MyTenantDto>('/me/tenants', data),
};
