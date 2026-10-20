import {
  DeliveryCourier,
  OrderChannel,
  OrderPaymentMethod,
  OrderStatus,
  PlatformCollectedBy,
  ServiceOrderPieceStatus,
} from '../types';
import type { OrderType, PurchaseOrderStatus } from '../types';

// Single catalog of the Spanish labels shown for backend enums. REST responses carry enums as numbers
// and SignalR payloads (and some report dictionaries) carry their C# names, so each label is reachable
// by both keys: the name goes to its number through the generated enum, which the contract test keeps as the server's.

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
  // Charged by Rappi, Uber Eats or DiDi Food in their app: it never enters a drawer.
  5: 'Plataforma',
  // Paid online into the business's Mercado Pago account (Fase 24I): it never enters a drawer either.
  6: 'En línea',
  // Paid with what the customer left in their favor before (Fase 26D): it entered a drawer when they left it.
  7: 'Saldo a favor',
};

/** The couriers a business asks for the trip of an order sent to an address (Fase 22C). */
export const DELIVERY_COURIER_LABELS: Record<DeliveryCourier, string> = {
  [DeliveryCourier.Paquetenvia]: 'Paquetenvia',
};

/** Where the order came from, as the server names each channel (OrderChannelRules.DisplayName). */
export const ORDER_CHANNEL_LABELS: Record<OrderChannel, string> = {
  0: 'Mostrador',
  1: 'WhatsApp',
  2: 'En línea',
  3: 'Rappi',
  4: 'Uber Eats',
  5: 'DiDi Food',
};

/** Rappi, Uber Eats and DiDi Food, in the order of the enum (OrderChannelRules.DeliveryPlatforms); utils/deliveryPlatforms builds on it. */
export const DELIVERY_PLATFORM_CHANNELS = [OrderChannel.Rappi, OrderChannel.UberEats, OrderChannel.DidiFood] as const;

/** Who charged an order of a delivery platform; the platform is named by its channel when it is known. */
const COLLECTED_BY_LABELS: Record<PlatformCollectedBy, string> = {
  0: 'La plataforma',
  1: 'El negocio',
};

export const SERVICE_PIECE_STATUS_LABELS: Record<ServiceOrderPieceStatus, string> = {
  0: 'Aceptada',
  1: 'En progreso',
  2: 'Completada',
  99: 'Cancelada',
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
  return lookup(status, ORDER_STATUS_LABELS, OrderStatus);
}

export function getPaymentMethodLabel(method: OrderPaymentMethod | string | null | undefined): string {
  return lookup(method, PAYMENT_METHOD_LABELS, OrderPaymentMethod);
}

/** Label of an order channel given as its number (REST) or its name (SignalR payloads). */
export function getChannelLabel(channel: OrderChannel | string | null | undefined): string {
  return lookup(channel, ORDER_CHANNEL_LABELS, OrderChannel);
}

/**
 * Who charged an order of a delivery platform: the platform by its name («Rappi») when the order's channel is given,
 * or «El negocio» when the courier paid the business.
 */
export function getCollectedByLabel(
  collectedBy: PlatformCollectedBy | string | null | undefined,
  channel?: OrderChannel | null,
): string {
  const label = lookup(collectedBy, COLLECTED_BY_LABELS, PlatformCollectedBy);
  const byThePlatform = label === COLLECTED_BY_LABELS[0];
  const platform = DELIVERY_PLATFORM_CHANNELS.find((candidate) => candidate === channel);
  return byThePlatform && platform !== undefined ? ORDER_CHANNEL_LABELS[platform] : label;
}

export function getServicePieceStatusLabel(status: ServiceOrderPieceStatus | string | null | undefined): string {
  return lookup(status, SERVICE_PIECE_STATUS_LABELS, ServiceOrderPieceStatus);
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
  1: { label: 'Enviada', badge: 'ta-chip-confirmed' },
  2: { label: 'Recepción parcial', badge: 'bg-info' },
  3: { label: 'Recibida', badge: 'bg-success' },
  4: { label: 'Cerrada sin el resto', badge: 'bg-dark' },
  99: { label: 'Cancelada', badge: 'bg-danger' },
};

export function getPurchaseOrderStatusLabel(status: PurchaseOrderStatus): string {
  return PURCHASE_ORDER_STATUS[status]?.label ?? String(status);
}

/** Badge class of a purchase order status. */
export function getPurchaseOrderStatusBadge(status: PurchaseOrderStatus): string {
  return PURCHASE_ORDER_STATUS[status]?.badge ?? 'bg-secondary';
}
