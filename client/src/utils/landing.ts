import { TenantCapability, type UserProfileDto } from '../types';
import { COMMERCE_CAPABILITIES, hasAnyCapability, hasCapability } from './tenantCapabilities';

// Same rule as the sidebar: tenant and super admins see every module. The API still validates each call.
export function hasUserPermission(user: UserProfileDto | null, permission: string): boolean {
  if (!user) return false;
  if (user.role === 'TenantAdmin' || user.role === 'SuperAdmin') return true;
  const expected = permission.toLowerCase();
  return (user.permissions ?? []).some((granted) => granted.toLowerCase() === expected);
}

/**
 * First page the user can open. Roles without reports (e.g. kitchen staff) used to land on the
 * dashboard, whose data they are not allowed to read.
 */
export function getLandingPath(user: UserProfileDto | null, capabilities: number | null): string {
  const commerce = hasAnyCapability(capabilities, COMMERCE_CAPABILITIES);
  if (commerce && hasUserPermission(user, 'ViewReports')) return '/dashboard';
  if (hasCapability(capabilities, TenantCapability.Kitchen) && hasUserPermission(user, 'ManageServiceBoard')) return '/kitchen';
  if (hasCapability(capabilities, TenantCapability.Service) && hasUserPermission(user, 'ManageServiceBoard')) return '/services/board';
  if (commerce && hasUserPermission(user, 'ManageOrders')) return '/orders/cart';
  if (commerce && hasUserPermission(user, 'ManageCash')) return '/cash';
  if (hasCapability(capabilities, TenantCapability.Appointments) && hasUserPermission(user, 'ManageAppointments')) return '/appointments';
  if (hasCapability(capabilities, TenantCapability.Inventory) && hasUserPermission(user, 'ManageInventory')) return '/inventory';
  return '/profile';
}
