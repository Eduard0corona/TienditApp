import api from './axios';
import type {
  AuthResponse,
  LoginRequest,
  TwoFactorLoginRequest,
  ApiMessage,
  RegisterBusinessRequest,
  RegisterCustomerRequest,
  MyTenantDto,
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

  resendPhoneConfirmation: (phoneNumber: string) =>
    api.post<ApiMessage>('/auth/resend-phone-confirmation', { phoneNumber }),

  forgotPassword: (email: string) =>
    api.post<ApiMessage>('/auth/forgot-password', { email }),

  resetPassword: (userId: string, token: string, newPassword: string) =>
    api.post<ApiMessage>('/auth/reset-password', { userId, token, newPassword }),

  registerBusiness: (data: RegisterBusinessRequest) =>
    api.post<AuthResponse>('/auth/register/business', data),

  registerCustomer: (data: RegisterCustomerRequest) =>
    api.post<AuthResponse>('/auth/register/customer', data),

  getMyTenants: () =>
    api.get<MyTenantDto[]>('/me/tenants'),

  switchTenant: (tenantId: string) =>
    api.post<AuthResponse>('/auth/switch-tenant', { tenantId }),
};
