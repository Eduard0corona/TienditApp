import { TenantCapability, type UserProfileDto } from '../types';
import { COMMERCE_CAPABILITIES, hasAnyCapability, hasCapability } from './tenantCapabilities';

/**
 * What the signed-in user can open, mirroring the API: each request declares the permission and the module it
 * needs (owners and admins of the business have every permission). The menu, the routes, the landing page and the
 * action buttons read this one matrix so nobody is offered something the API will reject. It is a navigation aid:
 * the API still authorizes each request.
 */
export interface Access {
  isAdmin: boolean;
  /** ViewReports. */
  dashboard: boolean;
  /** ViewReports. */
  reports: boolean;
  /** The business's activity: owners and admins. */
  audit: boolean;
  catalog: boolean;
  inventory: boolean;
  cash: boolean;
  /** Order list and detail: ManageOrders. */
  orders: boolean;
  /** Create orders, change status and cancel: ManageOrders. */
  sell: boolean;
  /** Register payments (in Vender, Pedidos and Mesas): ManageOrders + ChargeOrders. A waiter leaves them for the cashier. */
  charge: boolean;
  /** Settle what a courier collected (Caja › Repartidores): ManageCash + ChargeOrders, as the server asks. */
  settleCouriers: boolean;
  /** Kitchen board: ManageServiceBoard + Kitchen module. */
  kitchen: boolean;
  /** Tables and open tabs: ManageOrders + Food module. */
  tables: boolean;
  serviceBoard: boolean;
  customers: boolean;
  /** Who owes the business (fiado, Fase 26A): ManageCustomers or ChargeOrders, as the API asks. */
  receivables: boolean;
  appointments: boolean;
  employees: boolean;
  /** WhatsApp chat: ManageWhatsApp. */
  chat: boolean;
  whatsAppSettings: boolean;
  /** Automatic replies: ManageAiAgent. */
  aiAgent: boolean;
  businessSettings: boolean;
  /** Refund and void paid sales: ManageOrders + ProcessRefunds (owners and admins have both). */
  refunds: boolean;
  /** WhatsApp campaigns, templates, appointment reminders and opt-outs: ManageCampaigns. */
  campaigns: boolean;
  /** The business's reviews, to answer and report them (Fase 24H): owners and admins, as the API asks. */
  reviews: boolean;
  /** Platform console (businesses, plans and billing): the platform admin only. */
  platform: boolean;
  /** A customer account's own orders in every business (GET /me/orders). */
  myOrders: boolean;
}

export type AccessArea = Exclude<keyof Access, 'isAdmin'>;

function normalizeRole(role: string | null | undefined): string {
  return (role ?? '').replace(/[\s_-]/g, '').toLowerCase();
}

/** Owners and admins of this business (the role the server reports for it) and the platform admin have every permission. */
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
  const can = (permission: string) => hasUserPermission(user, permission);
  const commerce = hasAnyCapability(capabilities, COMMERCE_CAPABILITIES);

  return {
    isAdmin,
    dashboard: commerce && can('ViewReports'),
    reports: commerce && can('ViewReports'),
    audit: commerce && isAdmin,
    catalog: commerce && can('ManageCatalog'),
    inventory: hasCapability(capabilities, TenantCapability.Inventory) && can('ManageInventory'),
    cash: commerce && can('ManageCash'),
    orders: commerce && can('ManageOrders'),
    sell: commerce && can('ManageOrders'),
    charge: commerce && can('ManageOrders') && can('ChargeOrders'),
    settleCouriers: commerce && can('ManageCash') && can('ChargeOrders'),
    kitchen: hasCapability(capabilities, TenantCapability.Kitchen) && can('ManageServiceBoard'),
    tables: hasCapability(capabilities, TenantCapability.Food) && can('ManageOrders'),
    serviceBoard: hasCapability(capabilities, TenantCapability.Service) && can('ManageServiceBoard'),
    customers: commerce && can('ManageCustomers'),
    receivables: commerce && (can('ManageCustomers') || can('ChargeOrders')),
    appointments: hasCapability(capabilities, TenantCapability.Appointments) && can('ManageAppointments'),
    employees: commerce && isAdmin,
    chat: commerce && can('ManageWhatsApp'),
    whatsAppSettings: commerce && isAdmin,
    aiAgent: commerce && can('ManageAiAgent'),
    businessSettings: isAdmin,
    refunds: commerce && can('ManageOrders') && can('ProcessRefunds'),
    campaigns: commerce && can('ManageCampaigns'),
    reviews: isAdmin,
    platform: role === 'superadmin',
    myOrders: role === 'customer',
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
  if (access.myOrders) return '/explorar';
  return '/profile';
}
