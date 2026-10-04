import type { OrderPaymentMethod, OrderStatus, OrderType, PurchaseOrderStatus, ServiceOrderPieceStatus } from '../types';

// Single catalog of the Spanish labels shown for backend enums. REST responses carry enums as numbers
// and SignalR payloads (and some report dictionaries) carry their C# names, so each label is reachable
// by both keys.

const ORDER_STATUS_LABELS: Record<OrderStatus, string> = {
  0: 'Pendiente',
  1: 'Confirmada',
  2: 'En preparación',
  3: 'Lista',
  4: 'Aceptada',
  5: 'En progreso',
  6: 'Entregada',
  7: 'Completada',
  99: 'Cancelada',
};

const ORDER_STATUS_BY_NAME: Record<string, OrderStatus> = {
  Pending: 0,
  Confirmed: 1,
  InPreparation: 2,
  Ready: 3,
  Accepted: 4,
  InProgress: 5,
  Delivered: 6,
  Completed: 7,
  Cancelled: 99,
};

export const ORDER_TYPE_LABELS: Record<OrderType, string> = {
  0: 'Venta',
  1: 'Servicio',
};

const PAYMENT_METHOD_LABELS: Record<OrderPaymentMethod, string> = {
  0: 'Efectivo',
  1: 'Tarjeta',
  2: 'Transferencia',
  3: 'Billetera digital',
  4: 'Otro',
};

const PAYMENT_METHOD_BY_NAME: Record<string, OrderPaymentMethod> = {
  Cash: 0,
  Card: 1,
  Transfer: 2,
  DigitalWallet: 3,
  Other: 4,
};

export const SERVICE_PIECE_STATUS_LABELS: Record<ServiceOrderPieceStatus, string> = {
  0: 'Aceptada',
  1: 'En progreso',
  2: 'Completada',
  99: 'Cancelada',
};

const SERVICE_PIECE_STATUS_BY_NAME: Record<string, ServiceOrderPieceStatus> = {
  Accepted: 0,
  InProgress: 1,
  Completed: 2,
  Cancelled: 99,
};

const CATALOG_ITEM_TYPE_LABELS: Record<string, string> = {
  Product: 'Producto',
  Service: 'Servicio',
};

export const BUSINESS_TYPE_LABELS: Record<number, string> = {
  0: 'Alimentos',
  1: 'Comercio',
  2: 'Servicios',
};

// Keyed without separators: the API returns both SUPER_ADMIN (claims) and TenantAdmin (enum names).
const ROLE_LABELS: Record<string, string> = {
  SUPERADMIN: 'Administrador de la plataforma',
  TENANTADMIN: 'Administrador',
  STAFF: 'Empleado',
  KITCHEN: 'Cocina',
  CUSTOMER: 'Cliente',
  OWNER: 'Dueño',
  ADMIN: 'Administrador',
};

function lookup<T extends number>(
  value: T | string | null | undefined,
  labels: Record<T, string>,
  byName: Record<string, T>,
): string {
  if (value === null || value === undefined || value === '') return '';
  if (typeof value === 'number') return labels[value] ?? String(value);
  const numeric = Number(value);
  if (Number.isInteger(numeric) && String(numeric) === value.trim()) {
    return labels[numeric as T] ?? value;
  }
  const byNameValue = byName[value];
  return byNameValue === undefined ? value : labels[byNameValue];
}

/** Label of an order status given as its number (REST) or its name (SignalR, report keys). */
export function getOrderStatusLabel(status: OrderStatus | string | null | undefined): string {
  return lookup(status, ORDER_STATUS_LABELS, ORDER_STATUS_BY_NAME);
}

export function getPaymentMethodLabel(method: OrderPaymentMethod | string | null | undefined): string {
  return lookup(method, PAYMENT_METHOD_LABELS, PAYMENT_METHOD_BY_NAME);
}

export function getServicePieceStatusLabel(status: ServiceOrderPieceStatus | string | null | undefined): string {
  return lookup(status, SERVICE_PIECE_STATUS_LABELS, SERVICE_PIECE_STATUS_BY_NAME);
}

/** Label of a platform role (SUPER_ADMIN, TenantAdmin...) or membership role (Owner, Admin, Staff). */
export function getRoleLabel(role: string | null | undefined): string {
  if (!role) return '';
  return ROLE_LABELS[role.replace(/[\s_-]/g, '').toUpperCase()] ?? role;
}

/**
 * Role shown for the signed-in user: the membership role (Dueño, Administrador, Empleado) except for
 * kitchen and platform accounts, whose platform role says more.
 */
export function getUserRoleLabel(role: string | null | undefined, tenantRole: string | null | undefined): string {
  const platformRole = (role ?? '').replace(/[\s_-]/g, '').toUpperCase();
  if (platformRole === 'KITCHEN' || platformRole === 'SUPERADMIN' || !tenantRole) return getRoleLabel(role);
  return getRoleLabel(tenantRole);
}

/** Label of a catalog item type sent as text by reports ("Product", "Service"). */
export function getCatalogItemTypeLabel(type: string | null | undefined): string {
  if (!type) return '';
  return CATALOG_ITEM_TYPE_LABELS[type] ?? type;
}

const PURCHASE_ORDER_STATUS: Record<PurchaseOrderStatus, { label: string; badge: string }> = {
  0: { label: 'Borrador', badge: 'bg-secondary' },
  1: { label: 'Enviada', badge: 'bg-primary' },
  2: { label: 'Recepción parcial', badge: 'bg-info' },
  3: { label: 'Recibida', badge: 'bg-success' },
  99: { label: 'Cancelada', badge: 'bg-danger' },
};

export function getPurchaseOrderStatusLabel(status: PurchaseOrderStatus): string {
  return PURCHASE_ORDER_STATUS[status]?.label ?? String(status);
}

/** Badge class of a purchase order status. */
export function getPurchaseOrderStatusBadge(status: PurchaseOrderStatus): string {
  return PURCHASE_ORDER_STATUS[status]?.badge ?? 'bg-secondary';
}
