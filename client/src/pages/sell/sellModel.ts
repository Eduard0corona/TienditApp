import type { OrderDto, ProductDto } from '../../types';
import { OrderChannel, PlatformCollectedBy, UnitType } from '../../types';

// Same limits as the API (OrderLimits).
export const MAX_QUANTITY_PER_LINE = 10_000;
export const MAX_LINES = 200;
// The largest page the API serves; a bigger catalog is searched on the server.
export const CATALOG_PAGE_SIZE = 500;
// How often Vender tries to replace the catalog saved on the device while the server does not answer.
export const CATALOG_RETRY_MS = 60_000;

export interface CartLine {
  /** Product plus its options: the same product with other options is another line. */
  key: string;
  productId: string;
  optionIds: string[];
  /** "Grande, Almendra". */
  optionsLabel: string;
  name: string;
  unitPrice: number;
  quantity: number;
  notes: string;
  showNotes: boolean;
  isService: boolean;
  ratePercent: number;
  isExempt: boolean;
  unit?: UnitType;
  /** Sold by weight, volume, length or time: the quantity admits three decimals. */
  fractional: boolean;
}

/** A product with variants or extras waiting for the options and quantity. */
export interface OptionsRequest {
  product: ProductDto;
  optionIds: string[];
  quantityText: string;
}

/** Where the order goes when it is not a counter sale: a new tab or a round of an open one. */
export type TabTarget =
  | { kind: 'new'; tableId?: string; label: string }
  | { kind: 'round'; orderId: string; label: string };

export function readTabTarget(params: URLSearchParams): TabTarget | null {
  const orderId = params.get('tab');
  if (orderId) return { kind: 'round', orderId, label: params.get('label')?.trim() || 'la cuenta' };
  const tableId = params.get('table') ?? undefined;
  const label = params.get('label')?.trim();
  if (tableId || label) return { kind: 'new', tableId, label: label || 'Mesa' };
  return null;
}

/** A weighed or measured product waiting for its quantity. */
export interface MeasureRequest {
  product: ProductDto;
  text: string;
  /** The product is already in the ticket: the quantity replaces the one there. */
  replacing: boolean;
}

/** Quick picks of the quantity dialog for each unit. */
export function getQuickQuantities(unit?: UnitType): number[] {
  switch (unit) {
    case UnitType.G:
    case UnitType.Ml:
      return [100, 250, 500, 1000];
    case UnitType.Hour:
      return [0.5, 1, 1.5, 2];
    default:
      return [0.25, 0.5, 1, 2];
  }
}

export interface CompletedSale {
  order: OrderDto;
  /** Saved without charging (a WhatsApp order paid on delivery). */
  unpaid?: boolean;
  /** Charged without connection: kept on the device until it reaches the server. */
  offline?: boolean;
}

/** The customer a link fills in (Vender opened with ?phone=&name=), from the customer card or a chat. */
export interface CustomerPrefill {
  phone: string;
  name?: string;
}

export function readCustomerPrefill(params: URLSearchParams): CustomerPrefill | null {
  const phone = params.get('phone')?.trim();
  if (!phone) return null;
  return { phone, name: params.get('name')?.trim() || undefined };
}

/** The chat a sale was started from (?conversation= besides the customer): only then is it a WhatsApp order. */
export interface ChatOrigin extends CustomerPrefill {
  conversationId: string;
}

export function readChatOrigin(params: URLSearchParams): ChatOrigin | null {
  const customer = readCustomerPrefill(params);
  const conversationId = params.get('conversation')?.trim();
  if (!customer || !conversationId) return null;
  return { ...customer, conversationId };
}

export type CategoryFilter = 'all' | 'featured' | string;

/** Where an order sent to the customer's address goes (Fase 22C), as typed: the server normalizes it. */
export interface SaleDelivery {
  postalCode: string;
  addressLine: string;
  references: string;
  /** The bill the customer will pay the courier with, as typed (Fase 22D); missing in drafts from before. */
  paysWith?: string;
}

export const NO_DELIVERY_ADDRESS: SaleDelivery = { postalCode: '', addressLine: '', references: '' };

/**
 * The appointment of the agenda the sale charges (Fase 22E), opened with «Cobrar» (Vender with ?appointment=): the server
 * completes it with the sale and adds its visit charge to the total.
 */
export interface SaleAppointment {
  id: string;
  customerName: string;
  /** «Corte · lun 5 oct, 10:00», for the ticket's banner. */
  description: string;
  /** What the visit at the customer's address was agreed for; null at the business or without a charge. */
  visitFee: number | null;
  zoneName: string | null;
}

/** The appointment a link asks to charge (?appointment=), read once. */
export function readAppointmentId(params: URLSearchParams): string | null {
  return params.get('appointment')?.trim() || null;
}

/** Who the sale is for, where it goes and where it came from: all optional at the counter. */
export interface SaleDetails {
  /** At the counter, the customer; in an order of a delivery platform, the name for the bag. */
  customerName: string;
  customerPhone: string;
  tableNumber: string;
  orderNotes: string;
  /** The counter, or the delivery platform whose app took the order (Rappi, Uber Eats, DiDi Food). */
  channel: OrderChannel;
  /** The folio of the platform's app, as typed: the server normalizes it. */
  externalOrderNumber: string;
  /** The platform's long id of the order, as typed and optional: its statements name the order by it. */
  externalOrderId: string;
  /** Who charged an order of a platform: the platform in its app (by default) or the business, when the courier pays. */
  collectedBy: PlatformCollectedBy;
  /** Sent to the customer's address with the zone that reaches its postal code; null when it is picked up or eaten here. */
  delivery: SaleDelivery | null;
  /** The appointment it charges; null (or missing in drafts from before) for any other sale. */
  appointment?: SaleAppointment | null;
}

/** Where a counter sale comes from: no platform, no folio. */
export const COUNTER_ORIGIN: Pick<SaleDetails, 'channel' | 'externalOrderNumber' | 'externalOrderId' | 'collectedBy'> = {
  channel: OrderChannel.Counter,
  externalOrderNumber: '',
  externalOrderId: '',
  collectedBy: PlatformCollectedBy.Platform,
};

export const NO_SALE_DETAILS: SaleDetails = {
  customerName: '', customerPhone: '', tableNumber: '', orderNotes: '', ...COUNTER_ORIGIN, delivery: null, appointment: null,
};

const SERVICE_UNITS: ReadonlyArray<number> = [UnitType.Hour, UnitType.Session, UnitType.Service];

export function isServiceProduct(product: ProductDto): boolean {
  return product.unit !== undefined && SERVICE_UNITS.includes(product.unit);
}

// Quick feedback only; the API normalizes and validates the number.
export function isPlausiblePhone(value: string): boolean {
  const digits = value.replace(/\D/g, '');
  return digits.length >= 8 && digits.length <= 15;
}
