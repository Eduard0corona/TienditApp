import type { OrderDto, OrderStatus, OrderType } from '../../types';
import {
  OrderChannel,
  OrderPaymentMethod,
  OrderStatus as OrderStatusConst,
  OrderType as OrderTypeConst,
  PlatformCollectedBy,
  TenantCapability,
} from '../../types';
import { hasAnyCapability, hasCapability } from '../../utils/tenantCapabilities';
import { isDeliveryPlatform } from '../../utils/deliveryPlatforms';
import { formatInBusinessZone } from '../../utils/dates';
import {
  ORDER_CHANNEL_LABELS,
  SERVICE_PIECE_STATUS_LABELS,
  getOrderStatusLabel,
  getPaymentMethodLabel,
} from '../../utils/labels';

// What Pedidos needs besides drawing: the options of its filters, how an order reads in the list and in its detail, and
// the kinds of order of the business. Nothing here holds React state, so the list, the detail and the tests read the same;
// only the dates depend on the zone of the business (utils/dates).

const ALL_STATUSES: OrderStatus[] = [0, 1, 2, 3, 4, 5, 6, 7, 99];

const toStatusOptions = (statuses: OrderStatus[]) =>
  statuses.map((value) => ({ value, label: getOrderStatusLabel(value) }));

export const STATUS_OPTIONS = toStatusOptions(ALL_STATUSES);

export const ORDER_TYPE_STATUS_OPTIONS: Record<OrderType, Array<{ value: OrderStatus; label: string }>> = {
  [OrderTypeConst.Retail]: toStatusOptions([0, 1, 2, 3, 6, 99]),
  [OrderTypeConst.Service]: toStatusOptions([0, 4, 5, 7, 99]),
};

// Where the order came from; the counter is the usual one and carries no badge in the list. The API says where the
// business receives orders (Fase 29Q): a paused channel stays, so its orders are still found.
export function getChannelOptions(channels: OrderChannel[]) {
  return channels.map((value) => ({ value, label: ORDER_CHANNEL_LABELS[value] }));
}

/**
 * What a charge, refund or void answers does not say which quote the order came from (Fase 29E); it never changes, so the
 * order keeps the one it had.
 */
export function withItsQuote(updated: OrderDto, previous: OrderDto): OrderDto {
  return updated.quote ? updated : { ...updated, quote: previous.quote, workAppointments: previous.workAppointments };
}

/** The platform charged the order in its app: nothing is left to collect from the courier. */
export function chargedByPlatform(order: OrderDto): boolean {
  return isDeliveryPlatform(order.channel) && order.platformOrder?.collectedBy === PlatformCollectedBy.Platform;
}

/** Sent out with the courier and not delivered yet (Fase 22D). */
export function isOnTheWay(order: OrderDto): boolean {
  return !!order.delivery?.dispatchedAt
    && order.status !== OrderStatusConst.Delivered
    && order.status !== OrderStatusConst.Completed
    && order.status !== OrderStatusConst.Cancelled;
}

/** A payment's method; what a platform charged reads with the folio of its app: «Plataforma · BC953». */
export function getPaymentMethodText(order: OrderDto, payment: OrderDto['payments'][number]): string {
  const label = getPaymentMethodLabel(payment.method);
  if (payment.method !== OrderPaymentMethod.Platform) return label;
  const folio = order.platformOrder?.externalOrderNumber ?? payment.reference;
  return folio ? `${label} · ${folio}` : label;
}

/** The kinds of order of the business, by the rule the server lists them with: sales (products or food), services or both (both while unknown). */
export function getOrderTypes(capabilities: number | null): OrderType[] {
  const sells = hasAnyCapability(capabilities, TenantCapability.Retail | TenantCapability.Food);
  const serves = hasCapability(capabilities, TenantCapability.Service);
  if (sells && !serves) return [OrderTypeConst.Retail];
  if (serves && !sells) return [OrderTypeConst.Service];
  return [OrderTypeConst.Retail, OrderTypeConst.Service];
}

export const SERVICE_PIECE_STATUS_OPTIONS = ([0, 1, 2, 99] as const).map((value) => ({
  value,
  label: SERVICE_PIECE_STATUS_LABELS[value],
}));

/** Where a tracked piece of a service order can go: accepted to in progress, in progress to completed, or cancelled before it is completed. */
export function getPieceAllowedTransitions(status: number): number[] {
  switch (status) {
    case 0: return [1, 99];
    case 1: return [2, 99];
    default: return [];
  }
}

export function toLocalDateTime(date: string) {
  return formatInBusinessZone(date, {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}
