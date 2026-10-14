import { OrderChannel, PlatformCollectedBy } from '../../types';
import { isDeliveryPlatform, normalizePlatformFolio } from '../../utils/deliveryPlatforms';
import type { SaleAppointment } from './sellModel';

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
    sale.push([draft.channel, normalizePlatformFolio(draft.externalOrderNumber), draft.collectedBy]);
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
      && !draft.delivery && !draft.appointment;
    if (isEmpty) window.localStorage.removeItem(storageKey(tenantId));
    else window.localStorage.setItem(storageKey(tenantId), JSON.stringify(draft));
  } catch {
    // Storage blocked (private mode): the draft only lives in this screen.
  }
}
