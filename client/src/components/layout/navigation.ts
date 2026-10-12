import { TenantCapability } from '../../types';
import type { Access, AccessArea } from '../../utils/access';
import { hasCapability } from '../../utils/tenantCapabilities';

export interface NavItem {
  label: string;
  path: string;
  icon: string;
  area: AccessArea;
  /** Words people may search for in the global search besides the label. */
  keywords?: string;
  /** Only when the business has this module (on top of the access area). */
  capability?: number;
  /** Active only on this exact path (a sibling route shares the prefix). */
  exact?: boolean;
}

/** Several pages under one menu entry: the menu shows the section once and each page shows its siblings as tabs. */
export interface NavSection {
  label: string;
  icon: string;
  pages: NavItem[];
}

export type NavEntry = NavItem | NavSection;

export interface NavGroup {
  title: string;
  entries: NavEntry[];
}

export function isNavSection(entry: NavEntry): entry is NavSection {
  return 'pages' in entry;
}

/**
 * The menu by job: what is done every day first, then management, with what is configured once in a single
 * entry. The menu, the section tabs and the global search read this list; access comes from getAccess.
 */
export const NAV_GROUPS: NavGroup[] = [
  {
    title: 'Operación diaria',
    entries: [
      { label: 'Inicio', path: '/dashboard', icon: 'ri-home-4-line', area: 'dashboard', keywords: 'resumen ventas hoy dashboard' },
      { label: 'Vender', path: '/sell', icon: 'ri-shopping-basket-2-line', area: 'sell', keywords: 'nueva venta cobrar punto de venta caja registradora' },
      { label: 'Mesas', path: '/tables', icon: 'ri-restaurant-line', area: 'tables', keywords: 'cuentas abiertas comandas rondas salón' },
      { label: 'Pedidos', path: '/orders', icon: 'ri-file-list-3-line', area: 'orders', keywords: 'ordenes órdenes tickets folios' },
      { label: 'Cocina', path: '/kitchen', icon: 'ri-restaurant-2-line', area: 'kitchen', keywords: 'comandas preparar' },
      { label: 'Taller', path: '/services/board', icon: 'ri-tools-line', area: 'serviceBoard', keywords: 'tablero de servicios piezas reparaciones lavado' },
      { label: 'Agenda', path: '/appointments', icon: 'ri-calendar-schedule-line', area: 'appointments', keywords: 'citas reservaciones' },
      { label: 'Mensajes', path: '/whatsapp/chat', icon: 'ri-chat-3-line', area: 'chat', keywords: 'whatsapp chat conversaciones' },
      { label: 'Caja', path: '/cash', icon: 'ri-safe-2-line', area: 'cash', keywords: 'corte turno efectivo gastos retiros' },
      { label: 'Mis pedidos', path: '/mis-pedidos', icon: 'ri-shopping-bag-3-line', area: 'myOrders', keywords: 'compras historial pedir otra vez' },
    ],
  },
  {
    title: 'Gestión',
    entries: [
      {
        label: 'Catálogo',
        icon: 'ri-shopping-bag-line',
        pages: [
          { label: 'Productos', path: '/products', icon: 'ri-shopping-bag-line', area: 'catalog', keywords: 'catálogo precios menú' },
          { label: 'Servicios', path: '/services', icon: 'ri-service-line', area: 'catalog', capability: TenantCapability.Service, exact: true, keywords: 'catálogo de servicios' },
          { label: 'Categorías', path: '/categories', icon: 'ri-list-check-2', area: 'catalog' },
          { label: 'Precios en apps', path: '/platform-prices', icon: 'ri-takeaway-line', area: 'catalog', keywords: 'rappi uber eats didi food plataformas de reparto precio en la app' },
        ],
      },
      // Inventario has one row of tabs (supplies, stock, movements…); buying from suppliers is its own section.
      { label: 'Inventario', path: '/inventory', icon: 'ri-archive-drawer-line', area: 'inventory', keywords: 'existencias insumos stock entradas salidas historial kardex recetas ubicaciones alertas' },
      {
        label: 'Compras',
        icon: 'ri-shopping-cart-2-line',
        pages: [
          { label: 'Compras', path: '/purchases', icon: 'ri-shopping-cart-2-line', area: 'inventory', keywords: 'órdenes de compra' },
          { label: 'Proveedores', path: '/suppliers', icon: 'ri-truck-line', area: 'inventory' },
        ],
      },
      {
        label: 'Clientes',
        icon: 'ri-team-line',
        pages: [
          { label: 'Clientes', path: '/customers', icon: 'ri-team-line', area: 'customers' },
          { label: 'Campañas', path: '/campaigns', icon: 'ri-megaphone-line', area: 'campaigns', keywords: 'promociones mensajes masivos whatsapp recordatorios de citas plantillas bajas' },
        ],
      },
      { label: 'Reportes', path: '/reports', icon: 'ri-bar-chart-box-line', area: 'reports', keywords: 'utilidad ventas del mes' },
      {
        label: 'Configuración',
        icon: 'ri-settings-3-line',
        pages: [
          { label: 'Negocio', path: '/settings/business', icon: 'ri-store-2-line', area: 'businessSettings', keywords: 'mi negocio empresa datos fiscales horarios iva impuestos tickets módulos plan logotipo' },
          { label: 'Sucursales', path: '/branches', icon: 'ri-store-3-line', area: 'businessSettings', keywords: 'tiendas locales puntos de venta matriz' },
          { label: 'Equipo y permisos', path: '/employees', icon: 'ri-user-settings-line', area: 'employees', keywords: 'equipo empleados usuarios roles invitaciones' },
          { label: 'WhatsApp', path: '/settings/whatsapp', icon: 'ri-whatsapp-line', area: 'whatsAppSettings' },
          { label: 'Respuestas automáticas', path: '/ai-agent', icon: 'ri-robot-2-line', area: 'aiAgent', keywords: 'agente ia bot' },
          { label: 'Seguridad y actividad', path: '/audit', icon: 'ri-shield-check-line', area: 'audit', keywords: 'auditoría bitácora actividad' },
          { label: 'Primeros pasos', path: '/setup', icon: 'ri-rocket-2-line', area: 'businessSettings', keywords: 'arranque guiado plantillas configuración inicial' },
        ],
      },
    ],
  },
  {
    title: 'Plataforma',
    entries: [
      { label: 'Negocios', path: '/platform', icon: 'ri-building-4-line', area: 'platform', exact: true, keywords: 'consola tenants suscripciones cobranza suspendidos' },
      { label: 'Planes', path: '/platform/plans', icon: 'ri-price-tag-3-line', area: 'platform', keywords: 'precios módulos límites prueba gratis' },
    ],
  },
];

export function isNavItemAllowed(item: NavItem, access: Access, capabilities: number | null): boolean {
  return access[item.area] && (item.capability === undefined || hasCapability(capabilities, item.capability));
}

/**
 * The groups with only what the person can open: a section keeps the pages they can open and disappears with
 * none; empty groups are dropped.
 */
export function getAllowedNavGroups(access: Access, capabilities: number | null): NavGroup[] {
  return NAV_GROUPS
    .map((group) => ({
      ...group,
      entries: group.entries.flatMap((entry): NavEntry[] => {
        if (!isNavSection(entry)) return isNavItemAllowed(entry, access, capabilities) ? [entry] : [];
        const pages = entry.pages.filter((page) => isNavItemAllowed(page, access, capabilities));
        return pages.length > 0 ? [{ ...entry, pages }] : [];
      }),
    }))
    .filter((group) => group.entries.length > 0);
}

/** Every page the person can open, sections included (the global search goes straight to any of them). */
export function getAllowedPages(access: Access, capabilities: number | null): NavItem[] {
  return getAllowedNavGroups(access, capabilities).flatMap((group) =>
    group.entries.flatMap((entry) => (isNavSection(entry) ? entry.pages : [entry])));
}

/** Where a menu entry leads: a section opens the first of its pages the person can open. */
export function getNavEntryPath(entry: NavEntry): string {
  return isNavSection(entry) ? entry.pages[0].path : entry.path;
}

export function isNavEntryActive(entry: NavEntry, pathname: string): boolean {
  return isNavSection(entry) ? entry.pages.some((page) => isNavItemActive(page, pathname)) : isNavItemActive(entry, pathname);
}

/** The section of the current page with the pages the person can open, when there is more than one to switch to. */
export function getSectionTabs(pathname: string, access: Access, capabilities: number | null): NavSection | null {
  for (const group of getAllowedNavGroups(access, capabilities)) {
    for (const entry of group.entries) {
      if (isNavSection(entry) && entry.pages.length > 1 && isNavEntryActive(entry, pathname)) return entry;
    }
  }
  return null;
}

export function isNavItemActive(item: NavItem, pathname: string): boolean {
  if (item.exact) return pathname === item.path;
  return pathname === item.path || pathname.startsWith(`${item.path}/`);
}

/**
 * Where the past-due notice of the plan shows in full: where the owner looks after the business (Inicio and the pages
 * of Configuración, the section of Mi negocio), not the screens the team works on, where the bell carries it. Mi
 * negocio says it in its plan card.
 */
export function showsPlanNotice(pathname: string): boolean {
  if (pathname === '/dashboard') return true;
  if (pathname === '/settings/business') return false;
  const settings = NAV_GROUPS.flatMap((group) => group.entries)
    .find((entry): entry is NavSection => isNavSection(entry) && entry.pages.some((page) => page.path === '/settings/business'));
  return !!settings?.pages.some((page) => isNavItemActive(page, pathname));
}
