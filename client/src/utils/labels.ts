import { DeliveryCourier, OrderChannel } from '../types';
import type {
  OrderPaymentMethod,
  OrderStatus,
  OrderType,
  PlatformCollectedBy,
  PurchaseOrderStatus,
  ServiceOrderPieceStatus,
} from '../types';

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
  // Charged by Rappi, Uber Eats or DiDi Food in their app: it never enters a drawer.
  5: 'Plataforma',
};

const PAYMENT_METHOD_BY_NAME: Record<string, OrderPaymentMethod> = {
  Cash: 0,
  Card: 1,
  Transfer: 2,
  DigitalWallet: 3,
  Other: 4,
  Platform: 5,
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

const ORDER_CHANNEL_BY_NAME: Record<string, OrderChannel> = {
  Counter: 0,
  WhatsApp: 1,
  Online: 2,
  Rappi: 3,
  UberEats: 4,
  DidiFood: 5,
};

/** Rappi, Uber Eats and DiDi Food, in the order of the enum (OrderChannelRules.DeliveryPlatforms); utils/deliveryPlatforms builds on it. */
export const DELIVERY_PLATFORM_CHANNELS = [OrderChannel.Rappi, OrderChannel.UberEats, OrderChannel.DidiFood] as const;

/** Who charged an order of a delivery platform; the platform is named by its channel when it is known. */
const COLLECTED_BY_LABELS: Record<PlatformCollectedBy, string> = {
  0: 'La plataforma',
  1: 'El negocio',
};

const COLLECTED_BY_BY_NAME: Record<string, PlatformCollectedBy> = {
  Platform: 0,
  Business: 1,
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

/** Label of an order channel given as its number (REST) or its name (SignalR payloads). */
export function getChannelLabel(channel: OrderChannel | string | null | undefined): string {
  return lookup(channel, ORDER_CHANNEL_LABELS, ORDER_CHANNEL_BY_NAME);
}

/**
 * Who charged an order of a delivery platform: the platform by its name («Rappi») when the order's channel is given,
 * or «El negocio» when the courier paid the business.
 */
export function getCollectedByLabel(
  collectedBy: PlatformCollectedBy | string | null | undefined,
  channel?: OrderChannel | null,
): string {
  const label = lookup(collectedBy, COLLECTED_BY_LABELS, COLLECTED_BY_BY_NAME);
  const byThePlatform = label === COLLECTED_BY_LABELS[0];
  const platform = DELIVERY_PLATFORM_CHANNELS.find((candidate) => candidate === channel);
  return byThePlatform && platform !== undefined ? ORDER_CHANNEL_LABELS[platform] : label;
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
