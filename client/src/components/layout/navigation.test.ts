import { describe, expect, it } from 'vitest';
import { TenantCapability, type UserProfileDto } from '../../types';
import { getAccess } from '../../utils/access';
import { getAllowedNavGroups, getAllowedPages, getNavEntryPath, getSectionTabs, isNavItemActive, isNavSection, showsPlanNotice } from './navigation';

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
  it('shows management in a few entries and configuration as one', () => {
    const owner = getAllowedNavGroups(getAccess(user('TENANT_ADMIN'), EVERYTHING), EVERYTHING);
    const management = owner.find((group) => group.title === 'Gestión')!;

    expect(management.entries.map((entry) => entry.label)).toEqual(['Catálogo', 'Inventario', 'Compras', 'Clientes', 'Reportes', 'Configuración']);
    // The diagnosis counted 22 entries: an owner with every module now has the work of the day plus six. Compras is
    // its own entry since Phase 18, so Inventario keeps a single row of tabs.
    expect(owner.flatMap((group) => group.entries).length).toBeLessThanOrEqual(15);
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

    expect(getSectionTabs('/purchases', owner, RESTAURANT)?.pages.map((page) => page.label)).toEqual(['Compras', 'Proveedores']);
    // Inventario keeps a single row of tabs: its own.
    expect(getSectionTabs('/inventory', owner, RESTAURANT)).toBeNull();
    expect(getSectionTabs('/campaigns', campaignsOnly, RESTAURANT)).toBeNull();
    expect(getSectionTabs('/sell', owner, RESTAURANT)).toBeNull();
  });

  it('tells the catalog of services from the workshop board', () => {
    const owner = getAccess(user('TENANT_ADMIN'), EVERYTHING);

    expect(getSectionTabs('/services', owner, EVERYTHING)?.label).toBe('Catálogo');
    expect(getSectionTabs('/services/board', owner, EVERYTHING)).toBeNull();
    expect(getSectionTabs('/products/42/edit', owner, EVERYTHING)?.label).toBe('Catálogo');
  });

  it('lets the global search reach every page, inside sections too', () => {
    const pages = getAllowedPages(getAccess(user('TENANT_ADMIN'), RESTAURANT), RESTAURANT).map((page) => page.path);

    expect(pages).toEqual(expect.arrayContaining(['/sell', '/purchases', '/suppliers', '/campaigns', '/settings/whatsapp', '/audit']));
    // Without the services module there is no services catalog to go to.
    expect(pages).not.toContain('/services');
  });

  it('shows the sales and the settlements of the platforms as the two tabs of Reportes, for whoever reads the reports', () => {
    const reader = getAccess(user('STAFF', ['ViewReports']), RESTAURANT);
    const tabs = (pathname: string) => getSectionTabs(pathname, reader, RESTAURANT);

    expect(tabs('/reports')?.pages.map((page) => page.label)).toEqual(['Ventas', 'Liquidaciones']);
    expect(tabs('/reports')?.label).toBe('Reportes');
    // A settlement opens under its tab, and Ventas is only its own page.
    const settlement = tabs('/reports/settlements/7f1c')!;
    expect(settlement.pages.filter((page) => isNavItemActive(page, '/reports/settlements/7f1c')).map((page) => page.label)).toEqual(['Liquidaciones']);
    expect(settlement.pages.filter((page) => isNavItemActive(page, '/reports')).map((page) => page.label)).toEqual(['Ventas']);
  });

  it('adds the global invoice to Reportes for owners and admins only, with each one under its tab', () => {
    const admin = getAccess(user('TENANT_ADMIN'), RESTAURANT);
    const tabs = getSectionTabs('/reports/global-invoices/7f1c', admin, RESTAURANT)!;

    expect(tabs.pages.map((page) => page.label)).toEqual(['Ventas', 'Liquidaciones', 'Factura global']);
    expect(tabs.pages.filter((page) => isNavItemActive(page, '/reports/global-invoices/7f1c')).map((page) => page.label))
      .toEqual(['Factura global']);
    // Who reads the reports does not issue invoices: the page is not theirs (the API refuses it too).
    const reader = getAccess(user('STAFF', ['ViewReports']), RESTAURANT);
    expect(getAllowedPages(reader, RESTAURANT).map((page) => page.path)).not.toContain('/reports/global-invoices');
  });

  it('lets the global search find the settlements by what people call them', () => {
    const pages = getAllowedPages(getAccess(user('TENANT_ADMIN'), RESTAURANT), RESTAURANT);
    const found = (words: string) => pages
      .filter((page) => `${page.label} ${page.keywords ?? ''}`.toLowerCase().includes(words))
      .map((page) => page.label);

    expect(found('liquidaciones')).toEqual(['Liquidaciones']);
    expect(found('depósitos')).toEqual(['Liquidaciones']);
    // Retenciones hangs from Liquidaciones (Fase 22J).
    expect(found('retenciones')).toEqual(['Liquidaciones']);
    // The CFDI of withholdings, since the Fase 28A the business's own invoices, and since the 28B its global invoice.
    expect(found('cfdi')).toEqual(['Liquidaciones', 'Factura global', 'Facturación']);
    expect(found('público en general')).toEqual(['Factura global']);
    expect(found('csd')).toEqual(['Facturación']);
    expect(found('reportes')).toEqual(['Ventas']);
  });
});

describe('plan notice', () => {
  it('shows in full where the owner looks after the business, not where the team works', () => {
    expect(showsPlanNotice('/dashboard')).toBe(true);
    // Every page of Configuración, whatever its path.
    for (const settingsPage of ['/branches', '/employees', '/settings/whatsapp', '/ai-agent', '/audit', '/setup']) {
      expect(showsPlanNotice(settingsPage)).toBe(true);
    }
    // Mi negocio says it in its plan card.
    expect(showsPlanNotice('/settings/business')).toBe(false);
    for (const workScreen of ['/sell', '/tables', '/kitchen', '/orders', '/cash', '/inventory', '/products', '/platform']) {
      expect(showsPlanNotice(workScreen)).toBe(false);
    }
    expect(showsPlanNotice('/settings-old')).toBe(false);
  });
});
