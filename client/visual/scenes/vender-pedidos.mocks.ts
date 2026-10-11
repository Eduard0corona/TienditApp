import { asOption, type DemoData, type SceneOptions } from '../fixtures';
import { NOW_ISO } from '../mocks/data.mjs';

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;

interface SaleItemRequest {
  productId: string;
  quantity: number;
  notes?: string;
  optionIds?: string[];
}

interface SalePaymentRequest {
  amount: number;
  method: number;
  reference?: string;
  tip?: number;
  cashReceived?: number;
}

interface SaleRequest {
  orderType?: number;
  customerName?: string;
  customerPhone?: string;
  customerNotes?: string;
  tableNumber?: string;
  channel?: number;
  items?: SaleItemRequest[];
  payments?: SalePaymentRequest[];
}

interface ProductOption {
  id: string;
  name: string;
  priceDelta: number;
}

const cents = (value: number) => Math.round(value * 100);
const money = (valueInCents: number) => valueInCents / 100;

/** numerator / denominator rounded to an integer, halves to even (how the API rounds the IVA). */
function halfEven(numerator: number, denominator: number) {
  const quotient = Math.floor(numerator / denominator);
  const rest = numerator - quotient * denominator;
  if (rest * 2 !== denominator) return rest * 2 > denominator ? quotient + 1 : quotient;
  return quotient % 2 === 0 ? quotient : quotient + 1;
}

/**
 * A counter sale as the API records it. The shared POST /orders numbers sales with a counter that lives in the worker
 * (the folio would change with the order the scenes run in) and drops the variants and extras, so the sale ticket
 * would not match the cart. Here the folio follows the demo's last one and each line keeps its options.
 */
function createSale(data: DemoData, body: SaleRequest) {
  data.vpSales = (data.vpSales ?? 0) + 1;
  const orderNumber = `ORD-${String(348 + data.vpSales).padStart(6, '0')}`;
  const id = `venta-${orderNumber.toLowerCase()}`;

  const items = (body.items ?? []).map((line, index) => {
    const product = data.products.find((candidate: { id: string }) => candidate.id === line.productId);
    const groups: Array<{ name: string; options: ProductOption[] }> = product.optionGroups ?? [];
    const options = groups.flatMap((group) => group.options
      .filter((option) => (line.optionIds ?? []).includes(option.id))
      .map((option) => ({ optionId: option.id, groupName: group.name, optionName: option.name, priceDelta: option.priceDelta })));
    const unitPriceCents = cents(product.priceAmount) + options.reduce((sum, option) => sum + cents(option.priceDelta), 0);
    const totalCents = Math.round(unitPriceCents * line.quantity);
    return {
      id: `${id}-linea-${index + 1}`, productId: product.id, productName: product.name, quantity: line.quantity,
      returnedQuantity: 0, cancelledQuantity: 0, unitPrice: money(unitPriceCents), totalPrice: money(totalCents),
      chargedPrice: money(totalCents), notes: line.notes ?? null, taxRate: product.isTaxExempt ? 0 : (product.taxRate ?? data.tenant.defaultTaxRate),
      isTaxExempt: !!product.isTaxExempt, unit: product.unit, satProductCode: '01010101', satUnitCode: 'H87', taxObject: 2,
      round: 1, options,
    };
  });

  // One tax line per rate, as OrderTaxCalculator groups them.
  const pricesIncludeTax: boolean = data.tenant.pricesIncludeTax;
  const groups = new Map<string, { ratePercent: number; isExempt: boolean; amountCents: number }>();
  for (const item of items) {
    const key = item.isTaxExempt ? 'exempt' : String(item.taxRate);
    const group = groups.get(key) ?? { ratePercent: item.taxRate, isExempt: item.isTaxExempt, amountCents: 0 };
    group.amountCents += cents(item.totalPrice);
    groups.set(key, group);
  }
  const taxBreakdown = [...groups.values()]
    .map((group) => {
      const basisPoints = Math.round(group.ratePercent * 100);
      const taxCents = group.isExempt || basisPoints === 0 ? 0
        : halfEven(group.amountCents * basisPoints, pricesIncludeTax ? 10_000 + basisPoints : 10_000);
      const baseCents = pricesIncludeTax ? group.amountCents - taxCents : group.amountCents;
      return { ratePercent: group.ratePercent, isExempt: group.isExempt, taxableBase: money(baseCents), taxAmount: money(taxCents), total: money(baseCents + taxCents) };
    })
    .sort((a, b) => Number(a.isExempt) - Number(b.isExempt) || b.ratePercent - a.ratePercent);
  const subTotalCents = taxBreakdown.reduce((sum, line) => sum + cents(line.taxableBase), 0);
  const taxCents = taxBreakdown.reduce((sum, line) => sum + cents(line.taxAmount), 0);
  const totalCents = subTotalCents + taxCents;

  const payments = (body.payments ?? []).map((payment, index) => {
    const tipCents = cents(payment.tip ?? 0);
    const received = payment.cashReceived ?? null;
    return {
      id: `${id}-pago-${index + 1}`, amount: payment.amount, method: payment.method, paidAt: NOW_ISO, reference: payment.reference ?? null,
      notes: null, tip: money(tipCents), cashReceived: received,
      change: received === null ? 0 : money(cents(received) - cents(payment.amount) - tipCents),
    };
  });
  const paidCents = payments.reduce((sum, payment) => sum + cents(payment.amount), 0);
  const isService = body.orderType === 1;
  const paid = items.length > 0 && paidCents >= totalCents;
  const status = paid && data.tenant.autoCloseCounterSales ? (isService ? 7 : 6) : 1;

  const order = {
    id, orderNumber, createdByBusiness: true, businessType: data.tenant.businessType, orderType: body.orderType ?? 0, status,
    customerName: body.customerName || 'Público en general', customerPhone: body.customerPhone ?? null,
    customerNotes: body.customerNotes ?? null, tableNumber: body.tableNumber ?? null, pricesIncludeTax, channel: body.channel ?? 0,
    subTotal: money(subTotalCents), tax: money(taxCents), total: money(totalCents), taxBreakdown,
    paidAmount: money(paidCents), balance: money(totalCents - paidCents), tipTotal: money(payments.reduce((sum, payment) => sum + cents(payment.tip), 0)),
    refundedAmount: 0, netPaidAmount: money(paidCents), canCancel: payments.length === 0, allowedTransitions: status === 1 ? [4, 2, 99] : [],
    createdAt: NOW_ISO, items, statusHistory: [], servicePieces: [], payments, refunds: [], itemCancellations: [],
    isTab: false, isOpenTab: false, kitchenTickets: [], stockWarnings: [],
  };
  data.orders.unshift(order);
  return order;
}

/** Vender: the sale the scenes charge, with a stable folio and its variants and extras. */
const sellRoutes: ExtraRoutes = (data, { ok }) => [
  ['POST', /^\/orders$/, (_match, _query, body: SaleRequest) => ok(createSale(data, body))],
];

export const SELL_ROUTES = asOption(sellRoutes);

/** A customer account (Mis pedidos): no business, only its own orders. */
export const CUSTOMER_PROFILE = {
  firstName: 'María Fernanda',
  lastName: 'López',
  fullName: 'María Fernanda López',
  email: 'mafer.lopez@gmail.com',
  role: 'Customer',
  tenantRole: null,
  permissions: [],
};
