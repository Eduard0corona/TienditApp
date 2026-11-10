import type { ProductDto } from '../../types';
import { OrderChannel, PlatformCollectedBy } from '../../types';
import { isDeliveryPlatform, normalizePlatformFolio, normalizePlatformOrderId } from '../../utils/deliveryPlatforms';
import { getSelectionError } from '../../utils/productOptions';
import {
  COUNTER_ORIGIN,
  MAX_QUANTITY_PER_LINE,
  NO_SALE_DETAILS,
  type CartLine,
  type SaleAppointment,
  type SaleDetails,
  type ToCartLine,
} from './sellModel';

// The sale being built survives a reload or a trip to another screen (per browser and business). It is
// a convenience: storage can be unavailable and the sale still works.

export interface SellDraftLine {
  productId: string;
  quantity: number;
  notes: string;
  /** Variants and extras chosen for the line. */
  optionIds?: string[];
}

/**
 * A charge sent for the sale of the draft and not confirmed yet. A retry, also after a reload, sends its key again
 * and the server answers with the sale it already recorded instead of recording another one.
 */
export interface PendingCharge {
  /** The sale's idempotency key (`CreateOrderRequest.idempotencyKey`). */
  key: string;
  /** The sale it was sent for (`describeSale`): another cart is another sale, with a key of its own. */
  sale: string;
}

export interface SellDraft {
  lines: SellDraftLine[];
  customerName: string;
  customerPhone: string;
  tableNumber: string;
  orderNotes: string;
  /** The counter or the delivery platform the order came from (a chat is never kept: it comes with its link). */
  channel: OrderChannel;
  /** The folio of the platform's app, as typed. */
  externalOrderNumber: string;
  /** The platform's long id of the order, as typed; missing in drafts from before. */
  externalOrderId?: string;
  collectedBy: PlatformCollectedBy;
  /** Where an order sent to the customer's address goes, as typed; null (or missing, before) when it does not. */
  delivery?: SellDraftDelivery | null;
  /** The appointment the sale charges (Fase 22E); null (or missing, before) for any other sale. */
  appointment?: SaleAppointment | null;
  pendingCharge?: PendingCharge;
}

export interface SellDraftDelivery {
  postalCode: string;
  addressLine: string;
  references: string;
  /** «Paga con», as typed (Fase 22D). */
  paysWith?: string;
}

const storageKey = (tenantId: string) => `tienditapp.sell.draft.v1.${tenantId}`;

/**
 * What the server receives of the sale (its lines, its customer and, for an order of a delivery platform, the platform,
 * the folio and who charged), to tell whether a cart is still the one charged: another platform or another folio is
 * another sale, with a key of its own. A counter sale reads as it always did, so a key kept before stays valid.
 */
export function describeSale(draft: SellDraft): string {
  const sale: unknown[] = [
    draft.lines.map((line) => [line.productId, line.quantity, line.notes.trim(), line.optionIds ?? []]),
    [draft.customerName, draft.customerPhone, draft.tableNumber, draft.orderNotes].map((value) => value.trim()),
  ];
  if (isDeliveryPlatform(draft.channel)) {
    // The long id only when typed: an order described before it existed keeps its description (and its pending charge).
    const longId = normalizePlatformOrderId(draft.externalOrderId ?? '');
    sale.push([draft.channel, normalizePlatformFolio(draft.externalOrderNumber), draft.collectedBy, ...(longId ? [longId] : [])]);
  }
  // Another address is another order: its zone, minimum and fee can differ.
  if (draft.delivery) {
    const paysWith = draft.delivery.paysWith?.trim();
    // Only when typed: a sale described before «paga con» existed keeps the same description (and its pending charge).
    sale.push([
      'delivery',
      ...[draft.delivery.postalCode, draft.delivery.addressLine, draft.delivery.references].map((value) => value.trim()),
      ...(paysWith ? [paysWith] : []),
    ]);
  }
  // Charging another appointment is another sale (and charging none is too).
  if (draft.appointment) sale.push(['appointment', draft.appointment.id]);
  return JSON.stringify(sale);
}

export function readSellDraft(tenantId: string): SellDraft | null {
  try {
    const raw = window.localStorage.getItem(storageKey(tenantId));
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Partial<SellDraft>;
    const charge = parsed.pendingCharge;
    return {
      lines: Array.isArray(parsed.lines)
        ? parsed.lines
          .filter((line) => typeof line?.productId === 'string' && Number.isFinite(line.quantity) && line.quantity > 0)
          .map((line) => ({
            ...line,
            optionIds: Array.isArray(line.optionIds) ? line.optionIds.filter((id) => typeof id === 'string') : [],
          }))
        : [],
      customerName: typeof parsed.customerName === 'string' ? parsed.customerName : '',
      customerPhone: typeof parsed.customerPhone === 'string' ? parsed.customerPhone : '',
      tableNumber: typeof parsed.tableNumber === 'string' ? parsed.tableNumber : '',
      orderNotes: typeof parsed.orderNotes === 'string' ? parsed.orderNotes : '',
      // Only the counter or a platform: anything else (a draft from before, or edited by hand) is a counter sale.
      channel: isDeliveryPlatform(parsed.channel) ? parsed.channel : OrderChannel.Counter,
      externalOrderNumber: typeof parsed.externalOrderNumber === 'string' ? parsed.externalOrderNumber : '',
      externalOrderId: typeof parsed.externalOrderId === 'string' ? parsed.externalOrderId : undefined,
      collectedBy: parsed.collectedBy === PlatformCollectedBy.Business ? PlatformCollectedBy.Business : PlatformCollectedBy.Platform,
      delivery: readDelivery(parsed.delivery),
      appointment: readAppointment(parsed.appointment),
      pendingCharge: typeof charge?.key === 'string' && typeof charge.sale === 'string'
        ? { key: charge.key, sale: charge.sale }
        : undefined,
    };
  } catch {
    return null;
  }
}

function readDelivery(value: unknown): SellDraftDelivery | undefined {
  if (!value || typeof value !== 'object') return undefined;
  const { postalCode, addressLine, references, paysWith } = value as Partial<SellDraftDelivery>;
  return {
    postalCode: typeof postalCode === 'string' ? postalCode : '',
    addressLine: typeof addressLine === 'string' ? addressLine : '',
    references: typeof references === 'string' ? references : '',
    paysWith: typeof paysWith === 'string' ? paysWith : '',
  };
}

function readAppointment(value: unknown): SaleAppointment | undefined {
  if (!value || typeof value !== 'object') return undefined;
  const { id, customerName, description, visitFee, zoneName } = value as Partial<SaleAppointment>;
  if (typeof id !== 'string' || !id) return undefined;
  return {
    id,
    customerName: typeof customerName === 'string' ? customerName : '',
    description: typeof description === 'string' ? description : '',
    visitFee: typeof visitFee === 'number' && Number.isFinite(visitFee) && visitFee > 0 ? visitFee : null,
    zoneName: typeof zoneName === 'string' ? zoneName : null,
  };
}

export function writeSellDraft(tenantId: string, draft: SellDraft) {
  try {
    const isEmpty = draft.lines.length === 0 && !draft.customerName && !draft.customerPhone
      && !draft.tableNumber && !draft.orderNotes && !isDeliveryPlatform(draft.channel) && !draft.externalOrderNumber
      && !draft.externalOrderId
      && !draft.delivery && !draft.appointment;
    if (isEmpty) window.localStorage.removeItem(storageKey(tenantId));
    else window.localStorage.setItem(storageKey(tenantId), JSON.stringify(draft));
  } catch {
    // Storage blocked (private mode): the draft only lives in this screen.
  }
}

/** The sale on screen as the device keeps it. */
export function toDraft(cart: CartLine[], details: SaleDetails): SellDraft {
  return {
    lines: cart.map((line) => ({ productId: line.productId, quantity: line.quantity, notes: line.notes, optionIds: line.optionIds })),
    ...details,
  };
}

/**
 * Where the sale starts before the catalog is in: an order of a delivery platform brought back from the device sells from
 * the whole catalog from its first load (its lines come back with it once the catalog is in).
 */
export function initialDetails(draft: SellDraft | null): SaleDetails {
  return draft && isDeliveryPlatform(draft.channel)
    ? {
      ...NO_SALE_DETAILS,
      channel: draft.channel,
      externalOrderNumber: draft.externalOrderNumber,
      externalOrderId: draft.externalOrderId ?? '',
      collectedBy: draft.collectedBy,
    }
    : { ...NO_SALE_DETAILS, delivery: draft?.delivery ?? null };
}

/**
 * The details of a sale brought back from the device, and whether they start in sight. One of a platform the cashier took
 * to the counter before the catalog loaded stays at the counter. The name for the bag of a platform's order is in sight
 * with its folio: behind the link there are only notes.
 */
export function restoreDetails(draft: SellDraft, toCounter: boolean): { details: SaleDetails; showDetails: boolean } {
  return {
    details: {
      customerName: draft.customerName,
      customerPhone: draft.customerPhone,
      tableNumber: draft.tableNumber,
      orderNotes: draft.orderNotes,
      channel: draft.channel,
      externalOrderNumber: draft.externalOrderNumber,
      externalOrderId: draft.externalOrderId ?? '',
      collectedBy: draft.collectedBy,
      delivery: draft.delivery ?? null,
      appointment: draft.appointment ?? null,
      ...(toCounter ? COUNTER_ORIGIN : {}),
    },
    showDetails: isDeliveryPlatform(draft.channel) && !toCounter
      ? Boolean(draft.orderNotes)
      : Boolean(draft.customerName || draft.customerPhone || draft.tableNumber || draft.orderNotes),
  };
}

/**
 * The lines of a sale brought back from the device, as the catalog has them now. Options that changed since the draft was
 * saved drop the line instead of selling something else, and so does a product no longer sold.
 */
export function linesFromDraft(lines: SellDraftLine[], catalog: ReadonlyMap<string, ProductDto>, toCartLine: ToCartLine): CartLine[] {
  return lines.flatMap((line) => {
    const product = catalog.get(line.productId);
    const optionIds = line.optionIds ?? [];
    if (!product || getSelectionError(product.optionGroups, optionIds)) return [];
    return [toCartLine(product, Math.min(line.quantity, MAX_QUANTITY_PER_LINE), line.notes, optionIds)];
  });
}

/** The ticket with the catalog read again (a price changed): the current prices, and what is no longer sold drops. */
export function refreshLines(cart: CartLine[], catalog: ReadonlyMap<string, ProductDto>, toCartLine: ToCartLine): CartLine[] {
  return cart.flatMap((line) => {
    const product = catalog.get(line.productId);
    if (!product || getSelectionError(product.optionGroups, line.optionIds)) return [];
    return [{ ...toCartLine(product, line.quantity, line.notes, line.optionIds), showNotes: line.showNotes }];
  });
}
