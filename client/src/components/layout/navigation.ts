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

export interface NavGroup {
  title: string;
  items: NavItem[];
}

/**
 * The menu by job: what is done every day first, then management, then what is configured once. The
 * menu, the phone tab bar and the global search read this list; access comes from getAccess.
 */
export const NAV_GROUPS: NavGroup[] = [
  {
    title: 'Operación diaria',
    items: [
      { label: 'Inicio', path: '/dashboard', icon: 'ri-home-4-line', area: 'dashboard', keywords: 'resumen ventas hoy dashboard' },
      { label: 'Vender', path: '/sell', icon: 'ri-shopping-basket-2-line', area: 'sell', keywords: 'nueva venta cobrar punto de venta caja registradora' },
      { label: 'Mesas', path: '/tables', icon: 'ri-restaurant-line', area: 'tables', keywords: 'cuentas abiertas comandas rondas salón' },
      { label: 'Pedidos', path: '/orders', icon: 'ri-file-list-3-line', area: 'orders', keywords: 'ordenes órdenes tickets folios' },
      { label: 'Cocina', path: '/kitchen', icon: 'ri-restaurant-2-line', area: 'kitchen', keywords: 'comandas preparar' },
      { label: 'Taller', path: '/services/board', icon: 'ri-tools-line', area: 'serviceBoard', keywords: 'tablero de servicios piezas reparaciones lavado' },
      { label: 'Agenda', path: '/appointments', icon: 'ri-calendar-schedule-line', area: 'appointments', keywords: 'citas reservaciones' },
      { label: 'Mensajes', path: '/whatsapp/chat', icon: 'ri-chat-3-line', area: 'chat', keywords: 'whatsapp chat conversaciones' },
      { label: 'Caja', path: '/cash', icon: 'ri-safe-2-line', area: 'cash', keywords: 'corte turno efectivo gastos retiros' },
    ],
  },
  {
    title: 'Gestión',
    items: [
      { label: 'Productos', path: '/products', icon: 'ri-shopping-bag-line', area: 'catalog', keywords: 'catálogo precios menú' },
      { label: 'Servicios', path: '/services', icon: 'ri-service-line', area: 'catalog', capability: TenantCapability.Service, exact: true, keywords: 'catálogo de servicios' },
      { label: 'Categorías', path: '/categories', icon: 'ri-list-check-2', area: 'catalog' },
      { label: 'Inventario', path: '/inventory', icon: 'ri-archive-drawer-line', area: 'inventory', keywords: 'existencias insumos stock' },
      { label: 'Compras', path: '/purchases', icon: 'ri-shopping-cart-2-line', area: 'inventory', keywords: 'órdenes de compra' },
      { label: 'Proveedores', path: '/suppliers', icon: 'ri-truck-line', area: 'inventory' },
      { label: 'Clientes', path: '/customers', icon: 'ri-team-line', area: 'customers' },
      { label: 'Campañas', path: '/campaigns', icon: 'ri-megaphone-line', area: 'campaigns', keywords: 'promociones mensajes masivos whatsapp recordatorios de citas plantillas bajas' },
      { label: 'Reportes', path: '/reports', icon: 'ri-bar-chart-box-line', area: 'reports', keywords: 'utilidad ventas del mes' },
    ],
  },
  {
    title: 'Configuración',
    items: [
      { label: 'Mi negocio', path: '/settings/business', icon: 'ri-store-2-line', area: 'businessSettings', keywords: 'empresa datos fiscales iva impuestos módulos logotipo' },
      { label: 'Primeros pasos', path: '/setup', icon: 'ri-rocket-2-line', area: 'businessSettings', keywords: 'arranque guiado plantillas configuración inicial' },
      { label: 'Sucursales', path: '/branches', icon: 'ri-store-3-line', area: 'businessSettings', keywords: 'tiendas locales puntos de venta matriz' },
      { label: 'Equipo', path: '/employees', icon: 'ri-user-settings-line', area: 'employees', keywords: 'empleados usuarios permisos' },
      { label: 'WhatsApp', path: '/settings/whatsapp', icon: 'ri-whatsapp-line', area: 'whatsAppSettings' },
      { label: 'Respuestas automáticas', path: '/ai-agent', icon: 'ri-robot-2-line', area: 'aiAgent', keywords: 'agente ia bot' },
      { label: 'Auditoría', path: '/audit', icon: 'ri-shield-check-line', area: 'audit', keywords: 'bitácora actividad' },
    ],
  },
  {
    title: 'Plataforma',
    items: [
      { label: 'Negocios', path: '/platform', icon: 'ri-building-4-line', area: 'platform', exact: true, keywords: 'consola tenants suscripciones cobranza suspendidos' },
      { label: 'Planes', path: '/platform/plans', icon: 'ri-price-tag-3-line', area: 'platform', keywords: 'precios módulos límites prueba gratis' },
    ],
  },
];

export function isNavItemAllowed(item: NavItem, access: Access, capabilities: number | null): boolean {
  return access[item.area] && (item.capability === undefined || hasCapability(capabilities, item.capability));
}

/** The groups with only what the person can open; empty groups are dropped. */
export function getAllowedNavGroups(access: Access, capabilities: number | null): NavGroup[] {
  return NAV_GROUPS
    .map((group) => ({ ...group, items: group.items.filter((item) => isNavItemAllowed(item, access, capabilities)) }))
    .filter((group) => group.items.length > 0);
}

export function isNavItemActive(item: NavItem, pathname: string): boolean {
  if (item.exact) return pathname === item.path;
  return pathname === item.path || pathname.startsWith(`${item.path}/`);
}
