import { apiRequest, type Schemas } from './typedClient';

export type Plan = Omit<Schemas['PlanDto'], 'modules'> & { modules: number };
// Modules are flags: the OpenAPI document lists the single values, the API takes and returns any combination.
type ModuleFlags = Schemas['TenantCapability'];
export type PlanInput = Omit<Schemas['CreatePlanCommand'], 'modules'> & { modules: number };
export type PlanUpdate = Omit<Schemas['UpdatePlanCommand'], 'modules'> & { modules: number };
export type PlatformTenant = Schemas['PlatformTenantDto'];
export type PlatformOverview = Schemas['PlatformOverviewDto'];
export type SubscriptionDetail = Schemas['SubscriptionDetailDto'];
export type SubscriptionStatus = Schemas['SubscriptionStatus'];
export type SubscriptionPaymentMethod = Schemas['SubscriptionPaymentMethod'];

export const SubscriptionStatusValue = {
  Trial: 0,
  Active: 1,
  PastDue: 2,
  Suspended: 3,
  Cancelled: 4,
} as const satisfies Record<string, SubscriptionStatus>;

export const SUBSCRIPTION_STATUS_LABELS: Record<SubscriptionStatus, string> = {
  0: 'En prueba',
  1: 'Al corriente',
  2: 'Pago vencido',
  3: 'Suspendido',
  4: 'Cancelado',
};

export const SUBSCRIPTION_STATUS_TONES: Record<SubscriptionStatus, string> = {
  0: 'info',
  1: 'success',
  2: 'warning',
  3: 'danger',
  4: 'secondary',
};

export const PAYMENT_METHOD_LABELS: Record<SubscriptionPaymentMethod, string> = {
  0: 'Transferencia',
  1: 'Tarjeta',
  2: 'Efectivo',
  3: 'Otro',
};

/** Platform console (platform admin only): businesses, plans and billing. */
export const platformApi = {
  getOverview: () => apiRequest('get', '/api/v1/platform/overview'),

  getTenants: (query: { search?: string; subscriptionStatus?: SubscriptionStatus; withoutPlan?: boolean; planId?: string; pageNumber?: number; pageSize?: number }) =>
    apiRequest('get', '/api/v1/platform/tenants', { query }),

  getTenantSubscription: (tenantId: string) =>
    apiRequest('get', '/api/v1/platform/tenants/{tenantId}/subscription', { path: { tenantId } }),

  assignPlan: (tenantId: string, planId: string, trialDays?: number) =>
    apiRequest('put', '/api/v1/platform/tenants/{tenantId}/plan', { path: { tenantId }, body: { planId, trialDays: trialDays ?? null } }),

  registerPayment: (tenantId: string, body: Schemas['RegisterSubscriptionPaymentRequest']) =>
    apiRequest('post', '/api/v1/platform/tenants/{tenantId}/payments', { path: { tenantId }, body }),

  extendTrial: (tenantId: string, days: number) =>
    apiRequest('post', '/api/v1/platform/tenants/{tenantId}/trial', { path: { tenantId }, body: { days } }),

  suspend: (tenantId: string, reason: string) =>
    apiRequest('post', '/api/v1/platform/tenants/{tenantId}/suspend', { path: { tenantId }, body: { reason } }),

  reactivate: (tenantId: string) =>
    apiRequest('post', '/api/v1/platform/tenants/{tenantId}/reactivate', { path: { tenantId } }),

  cancel: (tenantId: string, reason: string) =>
    apiRequest('post', '/api/v1/platform/tenants/{tenantId}/cancel', { path: { tenantId }, body: { reason } }),

  getPlans: async (): Promise<Plan[]> => apiRequest('get', '/api/v1/platform/plans'),

  createPlan: (body: PlanInput) =>
    apiRequest('post', '/api/v1/platform/plans', { body: { ...body, modules: body.modules as ModuleFlags } }),

  updatePlan: (id: string, body: PlanUpdate) =>
    apiRequest('put', '/api/v1/platform/plans/{id}', { path: { id }, body: { ...body, modules: body.modules as ModuleFlags } }),

  /** Plan and billing of the current business (owners and admins). */
  getMySubscription: () => apiRequest('get', '/api/v1/auth/my-tenant/subscription'),
};
