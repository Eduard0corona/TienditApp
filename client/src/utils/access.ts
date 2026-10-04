import { TenantCapability, type UserProfileDto } from '../types';
import { COMMERCE_CAPABILITIES, hasAnyCapability, hasCapability } from './tenantCapabilities';

/**
 * What the signed-in user can open, mirroring the API: TenantResolutionMiddleware checks the permission
 * code and the module (capability) of every route, and each controller lists the roles that may call it.
 * The menu, the routes, the landing page and the action buttons read this one matrix so nobody is
 * offered something the API will reject. It is a navigation aid: the API still authorizes each request.
 */
export interface Access {
  isAdmin: boolean;
  /** DashboardController: counter roles + ViewReports. */
  dashboard: boolean;
  /** ReportsController: counter roles + ViewReports. */
  reports: boolean;
  /** AuditController: admins. */
  audit: boolean;
  catalog: boolean;
  inventory: boolean;
  cash: boolean;
  /** Order list and detail (GET): every employee role + ManageOrders. */
  orders: boolean;
  /** Create orders, register payments, change status and cancel: counter roles + ManageOrders. */
  sell: boolean;
  /** KitchenController: every employee role + ManageServiceBoard + Kitchen module. */
  kitchen: boolean;
  /** TablesController and open tabs: counter roles + ManageOrders + Food module. */
  tables: boolean;
  serviceBoard: boolean;
  customers: boolean;
  appointments: boolean;
  employees: boolean;
  /** WhatsAppChatController: counter roles + ManageWhatsApp. */
  chat: boolean;
  whatsAppSettings: boolean;
  aiAgent: boolean;
  businessSettings: boolean;
  /** Refund and void paid sales: owners, admins and staff with ProcessRefunds. */
  refunds: boolean;
  /** WhatsApp campaigns, templates, appointment reminders and opt-outs: owners, admins and ManageCampaigns. */
  campaigns: boolean;
}

export type AccessArea = Exclude<keyof Access, 'isAdmin'>;

function normalizeRole(role: string | null | undefined): string {
  return (role ?? '').replace(/[\s_-]/g, '').toLowerCase();
}

/** SUPER_ADMIN and TENANT_ADMIN are not restricted by permission codes in the middleware. */
export function isAdminUser(user: UserProfileDto | null): boolean {
  const role = normalizeRole(user?.role);
  return role === 'tenantadmin' || role === 'superadmin';
}

export function hasUserPermission(user: UserProfileDto | null, permission: string): boolean {
  if (!user) return false;
  if (isAdminUser(user)) return true;
  const expected = permission.toLowerCase();
  return (user.permissions ?? []).some((granted) => granted.toLowerCase() === expected);
}

export function getAccess(user: UserProfileDto | null, capabilities: number | null): Access {
  const role = normalizeRole(user?.role);
  const isAdmin = role === 'tenantadmin' || role === 'superadmin';
  // Counter roles: the ones OrdersController accepts to sell and charge.
  const isCounterRole = isAdmin || role === 'staff';
  const isEmployeeRole = isCounterRole || role === 'kitchen';
  const can = (permission: string) => hasUserPermission(user, permission);
  const commerce = hasAnyCapability(capabilities, COMMERCE_CAPABILITIES);

  return {
    isAdmin,
    dashboard: commerce && isCounterRole && can('ViewReports'),
    reports: commerce && isCounterRole && can('ViewReports'),
    audit: commerce && isAdmin,
    catalog: commerce && isCounterRole && can('ManageCatalog'),
    inventory: hasCapability(capabilities, TenantCapability.Inventory) && isCounterRole && can('ManageInventory'),
    cash: commerce && isCounterRole && can('ManageCash'),
    orders: commerce && isEmployeeRole && can('ManageOrders'),
    sell: commerce && isCounterRole && can('ManageOrders'),
    kitchen: hasCapability(capabilities, TenantCapability.Kitchen) && isEmployeeRole && can('ManageServiceBoard'),
    tables: hasCapability(capabilities, TenantCapability.Food) && isCounterRole && can('ManageOrders'),
    serviceBoard: hasCapability(capabilities, TenantCapability.Service) && isEmployeeRole && can('ManageServiceBoard'),
    customers: commerce && isCounterRole && can('ManageCustomers'),
    appointments: hasCapability(capabilities, TenantCapability.Appointments) && isCounterRole && can('ManageAppointments'),
    employees: commerce && isAdmin,
    chat: commerce && isCounterRole && can('ManageWhatsApp'),
    whatsAppSettings: commerce && isAdmin,
    aiAgent: commerce && isAdmin,
    businessSettings: isAdmin,
    refunds: commerce && isCounterRole && can('ManageOrders') && can('ProcessRefunds'),
    campaigns: commerce && isCounterRole && can('ManageCampaigns'),
  };
}

/**
 * First page for the user: owners and admins start at the dashboard, everyone else at the screen of
 * their job (sell, kitchen, service board...). Every path returned here is allowed by getAccess, so the
 * route guards never bounce back.
 */
export function getLandingPath(user: UserProfileDto | null, capabilities: number | null): string {
  const access = getAccess(user, capabilities);
  if (access.isAdmin && access.dashboard) return '/dashboard';
  if (access.sell) return '/sell';
  if (access.kitchen) return '/kitchen';
  if (access.serviceBoard) return '/services/board';
  if (access.orders) return '/orders';
  if (access.cash) return '/cash';
  if (access.appointments) return '/appointments';
  if (access.inventory) return '/inventory';
  if (access.dashboard) return '/dashboard';
  return '/profile';
}
