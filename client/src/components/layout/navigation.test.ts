import { describe, expect, it } from 'vitest';
import { TenantCapability, type UserProfileDto } from '../../types';
import { getAccess } from '../../utils/access';
import { findNavSection, getAllowedNavGroups, getAllowedPages, getNavEntryPath, getSectionTabs, isNavSection } from './navigation';

const RESTAURANT = TenantCapability.Food | TenantCapability.Inventory | TenantCapability.Kitchen | TenantCapability.Retail;
const EVERYTHING = RESTAURANT | TenantCapability.Service | TenantCapability.Appointments;

function user(role: string, permissions: string[] = []): UserProfileDto {
  return {
    id: 'u1',
    email: 'persona@demo.com',
    firstName: 'Persona',
    lastName: 'Demo',
    fullName: 'Persona Demo',
    phoneNumberConfirmed: true,
    emailConfirmed: true,
    role,
    permissions,
    tenantId: 't1',
    isActive: true,
    twoFactorEnabled: false,
    createdAt: '2026-10-05T00:00:00Z',
  };
}

const entryLabels = (role: string, permissions: string[], capabilities: number) =>
  getAllowedNavGroups(getAccess(user(role, permissions), capabilities), capabilities)
    .flatMap((group) => group.entries.map((entry) => entry.label));

describe('navigation', () => {
  it('shows management as four entries and configuration as one', () => {
    const owner = getAllowedNavGroups(getAccess(user('TENANT_ADMIN'), EVERYTHING), EVERYTHING);
    const management = owner.find((group) => group.title === 'Gestión')!;

    expect(management.entries.map((entry) => entry.label)).toEqual(['Catálogo', 'Inventario', 'Clientes', 'Reportes', 'Configuración']);
    // The diagnosis counted 22 entries: an owner with every module now has the work of the day plus five.
    expect(owner.flatMap((group) => group.entries).length).toBeLessThanOrEqual(14);
  });

  it('keeps only the pages a person can open in each section', () => {
    const labels = entryLabels('STAFF', ['ManageOrders', 'ManageCustomers'], RESTAURANT);

    expect(labels).toContain('Clientes');
    expect(labels).not.toContain('Catálogo');
    expect(labels).not.toContain('Configuración');
  });

  it('opens a section on the first page the person can open', () => {
    const groups = getAllowedNavGroups(getAccess(user('STAFF', ['ManageCampaigns']), RESTAURANT), RESTAURANT);
    const clients = groups.flatMap((group) => group.entries).find((entry) => entry.label === 'Clientes')!;

    expect(isNavSection(clients) && clients.pages.map((page) => page.label)).toEqual(['Campañas']);
    expect(getNavEntryPath(clients)).toBe('/campaigns');
  });

  it('shows the sibling pages as tabs only when there is more than one', () => {
    const owner = getAccess(user('TENANT_ADMIN'), RESTAURANT);
    const campaignsOnly = getAccess(user('STAFF', ['ManageCampaigns']), RESTAURANT);

    expect(getSectionTabs('/purchases', owner, RESTAURANT)?.pages.map((page) => page.label)).toEqual(['Existencias', 'Compras', 'Proveedores']);
    expect(getSectionTabs('/campaigns', campaignsOnly, RESTAURANT)).toBeNull();
    expect(getSectionTabs('/sell', owner, RESTAURANT)).toBeNull();
  });

  it('tells the catalog of services from the workshop board', () => {
    expect(findNavSection('/services')?.label).toBe('Catálogo');
    expect(findNavSection('/services/board')).toBeNull();
    expect(findNavSection('/products/42/edit')?.label).toBe('Catálogo');
  });

  it('lets the global search reach every page, inside sections too', () => {
    const pages = getAllowedPages(getAccess(user('TENANT_ADMIN'), RESTAURANT), RESTAURANT).map((page) => page.path);

    expect(pages).toEqual(expect.arrayContaining(['/sell', '/purchases', '/suppliers', '/campaigns', '/settings/whatsapp', '/audit']));
    // Without the services module there is no services catalog to go to.
    expect(pages).not.toContain('/services');
  });
});
