// Orders sent to the customer's address (Fase 22C). Mirrors the API (DeliveryZone, Order.ArrangeDelivery): the active
// zone of the business that reaches the postal code, its minimum and its «gratis desde» compared with what the products
// add up to, and the fee charged as one more concept with the business's IVA. It is an estimate for the customer and the
// cashier: the server decides the fee and rejects the order if the total it computes differs from the one shown.
import { formatMoney } from './money';
import { estimateOrderTotals, type OrderTotalsEstimate, type TotalsLine } from './orderTotals';
import { isPostalCode } from './postalCodes';

/** What a zone (or the public quote of a postal code) says about a delivery. */
export interface DeliveryTerms {
  fee: number;
  minimumOrder: number;
  freeDeliveryFrom?: number | null;
}

/** A zone of the business as Vender reads it (Configuración › Zonas de entrega). */
export interface DeliveryZoneTerms extends DeliveryTerms {
  name: string;
  isActive: boolean;
  postalCodes: string[];
}

export const ADDRESS_LINE_MAX_LENGTH = 300;
export const REFERENCES_MAX_LENGTH = 500;
/** The name of an address a customer account keeps (AccountAddress.MaxLabelLength). */
export const ADDRESS_LABEL_MAX_LENGTH = 60;
export const ADDRESS_LINE_REQUIRED_MESSAGE = 'Escribe a dónde va el pedido: calle, número y colonia.';
/** The server's words (HomeDeliveryRules.OnlyProductsMessage). */
export const DELIVERY_ONLY_PRODUCTS_MESSAGE = 'Solo los productos se mandan a domicilio.';

/** The postal code as the server keeps it: without spaces around. */
export function normalizePostalCode(text: string): string {
  return text.trim();
}

/** The active zone that reaches the postal code, or null (a postal code is in one zone of the business at most). */
export function findDeliveryZone<T extends DeliveryZoneTerms>(zones: readonly T[], postalCode: string): T | null {
  const code = normalizePostalCode(postalCode);
  if (!isPostalCode(code)) return null;
  return zones.find((zone) => zone.isActive && zone.postalCodes.includes(code)) ?? null;
}

/** What the delivery of an order with `productsTotal` in products costs: free from what the zone says. */
export function deliveryFeeFor(terms: DeliveryTerms, productsTotal: number): number {
  return terms.freeDeliveryFrom != null && productsTotal >= terms.freeDeliveryFrom ? 0 : terms.fee;
}

export function meetsMinimum(terms: DeliveryTerms, productsTotal: number): boolean {
  return productsTotal >= terms.minimumOrder;
}

/** The server's words (HomeDeliveryRules.NotCoveredMessage). */
export function notCoveredMessage(postalCode: string): string {
  return `Por ahora no llegamos al ${postalCode}.`;
}

/** The server's words (HomeDeliveryRules.BelowMinimumMessage). */
export function belowMinimumMessage(postalCode: string, minimumOrder: number): string {
  return `El pedido mínimo para el ${postalCode} es de ${formatMoney(minimumOrder)}.`;
}

/**
 * «Envío $35.00 · gratis desde $400.00 · pedido mínimo $150.00», for the customer or the cashier. The fee goes like the
 * prices of the business: «más IVA» when they do not include it (the order adds it); the minimum and «gratis desde» are
 * what the customer pays in products.
 */
export function describeDeliveryTerms(terms: DeliveryTerms, pricesIncludeTax = true): string {
  const parts = [terms.fee > 0 ? `Envío ${formatMoney(terms.fee)}${pricesIncludeTax ? '' : ' más IVA'}` : 'Envío gratis'];
  if (terms.fee > 0 && terms.freeDeliveryFrom != null) parts.push(`gratis desde ${formatMoney(terms.freeDeliveryFrom)}`);
  if (terms.minimumOrder > 0) parts.push(`pedido mínimo ${formatMoney(terms.minimumOrder)}`);
  return parts.join(' · ');
}

export interface DeliveryEstimate extends OrderTotalsEstimate {
  /** What the products add up to, as the zone's minimum and «gratis desde» read it. */
  productsTotal: number;
  /** The fee the order would charge: 0 when free. */
  fee: number;
}

/**
 * The totals of an order sent to an address: the products, and the fee as one more line at the business's IVA rate
 * (the server charges it with TenantSalesSettings.DefaultTaxRate and the order's tax policy).
 */
export function estimateDeliveryTotals(
  lines: TotalsLine[],
  pricesIncludeTax: boolean,
  terms: DeliveryTerms,
  defaultTaxRate: number,
): DeliveryEstimate {
  const productsTotal = estimateOrderTotals(lines, pricesIncludeTax).total;
  const fee = deliveryFeeFor(terms, productsTotal);
  const withFee = fee > 0
    ? [...lines, { unitPrice: fee, quantity: 1, ratePercent: defaultTaxRate, isExempt: false }]
    : lines;
  return { ...estimateOrderTotals(withFee, pricesIncludeTax), productsTotal, fee };
}
