import type { Schemas } from '../api/typedClient';

export type EmployeeRolePreset = Schemas['EmployeeRolePresetDto'];
export type EmployeeRecord = Schemas['EmployeeDto'];

/** UserRole of the API (account role). */
export const AppRole = { TenantAdmin: 1, Staff: 2, Kitchen: 3 } as const;
/** TenantUserRole of the API (role in this business). */
export const BusinessRole = { Owner: 0, Admin: 1, Staff: 2 } as const;

export const CUSTOM_ROLE_KEY = 'custom';
const MANAGER_KEY = 'manager';

export function appRoleFromName(name: string): number {
  if (name === 'TenantAdmin') return AppRole.TenantAdmin;
  if (name === 'Kitchen') return AppRole.Kitchen;
  return AppRole.Staff;
}

export function isManagerPreset(preset: EmployeeRolePreset | undefined): boolean {
  return preset?.key === MANAGER_KEY;
}

function sameCodes(a: readonly string[], b: readonly string[]): boolean {
  if (a.length !== b.length) return false;
  const set = new Set(a.map((code) => code.toLowerCase()));
  return b.every((code) => set.has(code.toLowerCase()));
}

/**
 * The named role (Gerente, Cajero, Mesero…) whose account role and permissions match exactly, or the custom role.
 * Owners and admins administer the business whatever their permissions say.
 */
export function matchPreset(
  presets: EmployeeRolePreset[],
  appRole: number,
  isManager: boolean,
  permissions: readonly string[],
): string {
  if (isManager) return MANAGER_KEY;
  const match = presets.find((preset) => !isManagerPreset(preset)
    && preset.appRole === appRole
    && sameCodes(preset.permissions, permissions));
  return match?.key ?? CUSTOM_ROLE_KEY;
}

/** How the team list names the role of a person: Dueño, Gerente, a named role, Personalizado or Sin acceso. */
export function describeEmployeeRole(employee: EmployeeRecord, presets: EmployeeRolePreset[]): string {
  if (employee.tenantRole === 'Owner') return 'Dueño';
  const isManager = employee.tenantRole === 'Admin';
  if (!isManager && employee.permissions.length === 0) return 'Sin acceso';
  const key = matchPreset(presets, appRoleFromName(employee.appRole), isManager, employee.permissions);
  return presets.find((preset) => preset.key === key)?.name ?? 'Personalizado';
}

/** Full invitation link for an app-relative path returned by the API. */
export function invitationLink(path: string): string {
  return new URL(path, window.location.origin).toString();
}

/** wa.me link with the invitation, to the phone written for the person when there is one. */
export function whatsAppInvitationLink(link: string, businessName: string, firstName: string, phone?: string | null): string {
  const text = `Hola ${firstName}, te invito a ${businessName} en TienditApp. Elige tu contraseña aquí: ${link}`;
  const digits = (phone ?? '').replace(/\D/g, '');
  // Ten digits are a Mexican number without its country code.
  const to = digits.length === 10 ? `52${digits}` : digits;
  return `https://wa.me/${to}?text=${encodeURIComponent(text)}`;
}
