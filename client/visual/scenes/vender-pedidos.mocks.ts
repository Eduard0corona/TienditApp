import { asOption, type DemoData, type SceneOptions } from '../fixtures';
import { NOW_ISO, at, withRappiPrices } from '../mocks/data.mjs';

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
  /** The folio of a delivery platform's app, normalized by Vender (Rappi, Uber Eats or DiDi Food only). */
  externalOrderNumber?: string;
  /** Who charged an order of a platform: the platform (0, also when omitted) or the business (1). */
  collectedBy?: number;
  items?: SaleItemRequest[];
  payments?: SalePaymentRequest[];
  /** Sent to the customer's address (Fase 22C): the zone that reaches its postal code charges its fee. */
  delivery?: { postalCode: string; addressLine: string; references?: string; paysWith?: number };
}

/** A delivery zone of the business as GET /delivery-zones answers it. */
interface DeliveryZone {
  id: string;
  name: string;
  fee: number;
  minimumOrder: number;
  freeDeliveryFrom: number | null;
  isActive: boolean;
  revision: number;
  postalCodes: string[];
}

// OrderChannel and OrderPaymentMethod as the contract numbers them.
const RAPPI = 3;
const DELIVERY_PLATFORMS = [3, 4, 5];
const PLATFORM_PAYMENT = 5;

interface ProductOption {
  id: string;
  name: string;
  priceDelta: number;
}

/** The prices set in the app of a delivery platform (`data.platformPrices` by channel, as mocks/data.mjs describes it). */
interface PlatformPrices {
  products: Record<string, number>;
  options: Record<string, number>;
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
 * would not match the cart. Here the folio follows the demo's last one and each line keeps its options. An order of a
 * delivery platform charges the prices set in its app, where there are, as Vender showed them.
 */
function createSale(data: DemoData, body: SaleRequest) {
  data.vpSales = (data.vpSales ?? 0) + 1;
  const orderNumber = `ORD-${String(348 + data.vpSales).padStart(6, '0')}`;
  const id = `venta-${orderNumber.toLowerCase()}`;
  const appPrices: PlatformPrices = (DELIVERY_PLATFORMS.includes(body.channel ?? 0) && data.platformPrices[body.channel ?? 0])
    || { products: {}, options: {} };

  const items = (body.items ?? []).map((line, index) => {
    const product = data.products.find((candidate: { id: string }) => candidate.id === line.productId);
    const groups: Array<{ name: string; options: ProductOption[] }> = product.optionGroups ?? [];
    const options = groups.flatMap((group) => group.options
      .filter((option) => (line.optionIds ?? []).includes(option.id))
      .map((option) => ({
        optionId: option.id, groupName: group.name, optionName: option.name, priceDelta: appPrices.options[option.id] ?? option.priceDelta,
      })));
    const unitPriceCents = cents(appPrices.products[product.id] ?? product.priceAmount)
      + options.reduce((sum, option) => sum + cents(option.priceDelta), 0);
    const totalCents = Math.round(unitPriceCents * line.quantity);
    return {
      id: `${id}-linea-${index + 1}`, productId: product.id, productName: product.name, quantity: line.quantity,
      returnedQuantity: 0, cancelledQuantity: 0, unitPrice: money(unitPriceCents), totalPrice: money(totalCents),
      chargedPrice: money(totalCents), notes: line.notes ?? null, taxRate: product.isTaxExempt ? 0 : (product.taxRate ?? data.tenant.defaultTaxRate),
      isTaxExempt: !!product.isTaxExempt, unit: product.unit, satProductCode: '01010101', satUnitCode: 'H87', taxObject: 2,
      round: 1, options,
    };
  });

  // One tax line per rate, as OrderTaxCalculator groups them; the delivery fee is one more amount at the business's rate.
  const pricesIncludeTax: boolean = data.tenant.pricesIncludeTax;
  const breakdownOf = (amounts: Array<{ taxRate: number; isTaxExempt: boolean; cents: number }>) => {
    const groups = new Map<string, { ratePercent: number; isExempt: boolean; amountCents: number }>();
    for (const amount of amounts) {
      const key = amount.isTaxExempt ? 'exempt' : String(amount.taxRate);
      const group = groups.get(key) ?? { ratePercent: amount.taxRate, isExempt: amount.isTaxExempt, amountCents: 0 };
      group.amountCents += amount.cents;
      groups.set(key, group);
    }
    return [...groups.values()]
      .map((group) => {
        const basisPoints = Math.round(group.ratePercent * 100);
        const taxCents = group.isExempt || basisPoints === 0 ? 0
          : halfEven(group.amountCents * basisPoints, pricesIncludeTax ? 10_000 + basisPoints : 10_000);
        const baseCents = pricesIncludeTax ? group.amountCents - taxCents : group.amountCents;
        return { ratePercent: group.ratePercent, isExempt: group.isExempt, taxableBase: money(baseCents), taxAmount: money(taxCents), total: money(baseCents + taxCents) };
      })
      .sort((a, b) => Number(a.isExempt) - Number(b.isExempt) || b.ratePercent - a.ratePercent);
  };
  const itemAmounts = items.map((item) => ({ taxRate: item.taxRate, isTaxExempt: item.isTaxExempt, cents: cents(item.totalPrice) }));
  const productsCents = breakdownOf(itemAmounts).reduce((sum, line) => sum + cents(line.total), 0);
  const zone = body.delivery
    ? deliveryZonesOf(data).find((candidate) => candidate.isActive && candidate.postalCodes.includes(body.delivery!.postalCode))
    : undefined;
  const feeCents = zone ? (zone.freeDeliveryFrom !== null && productsCents >= cents(zone.freeDeliveryFrom) ? 0 : cents(zone.fee)) : 0;
  const taxBreakdown = breakdownOf(feeCents > 0
    ? [...itemAmounts, { taxRate: data.tenant.defaultTaxRate, isTaxExempt: false, cents: feeCents }]
    : itemAmounts);
  const subTotalCents = taxBreakdown.reduce((sum, line) => sum + cents(line.taxableBase), 0);
  const taxCents = taxBreakdown.reduce((sum, line) => sum + cents(line.taxAmount), 0);
  const totalCents = subTotalCents + taxCents;

  // An order of a delivery platform: when the platform charged it, the server records its «Plataforma» payment itself,
  // by the total and with the folio as its reference; when the business does, it is charged as at the counter.
  const isPlatform = DELIVERY_PLATFORMS.includes(body.channel ?? 0);
  const collectedBy = isPlatform ? body.collectedBy ?? 0 : null;
  const platformPayments = collectedBy === 0
    ? [{
      id: `${id}-pago-1`, amount: money(totalCents), method: PLATFORM_PAYMENT, paidAt: NOW_ISO,
      reference: body.externalOrderNumber ?? null, notes: null, tip: 0, cashReceived: null, change: 0,
    }]
    : null;
  const payments = platformPayments ?? (body.payments ?? []).map((payment, index) => {
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
  // An order sent to an address leaves after the kitchen: paid, it is not closed yet.
  const status = paid && data.tenant.autoCloseCounterSales && !zone ? (isService ? 7 : 6) : 1;

  const order = {
    id, orderNumber, createdByBusiness: true, businessType: data.tenant.businessType, orderType: body.orderType ?? 0, status,
    customerName: body.customerName || 'Público en general', customerPhone: body.customerPhone ?? null,
    customerNotes: body.customerNotes ?? null, tableNumber: body.tableNumber ?? null, pricesIncludeTax, channel: body.channel ?? 0,
    subTotal: money(subTotalCents), tax: money(taxCents), total: money(totalCents), taxBreakdown,
    paidAmount: money(paidCents), balance: money(totalCents - paidCents), tipTotal: money(payments.reduce((sum, payment) => sum + cents(payment.tip), 0)),
    refundedAmount: 0, netPaidAmount: money(paidCents), canCancel: payments.length === 0, allowedTransitions: status === 1 ? [4, 2, 99] : [],
    createdAt: NOW_ISO, items, statusHistory: [], servicePieces: [], payments, refunds: [], itemCancellations: [],
    isTab: false, isOpenTab: false, kitchenTickets: [], stockWarnings: [],
    platformOrder: isPlatform ? { externalOrderNumber: body.externalOrderNumber ?? '', collectedBy } : null,
    delivery: zone && body.delivery
      ? {
        postalCode: body.delivery.postalCode, addressLine: body.delivery.addressLine, references: body.delivery.references ?? null,
        zoneName: zone.name, fee: money(feeCents), feeTaxRate: data.tenant.defaultTaxRate,
        courier: null, courierFolio: null, trackingUrl: null, shipmentRegisteredAt: null, shipmentRegisteredBy: null,
        paysWith: body.delivery.paysWith ?? null, dispatchedAt: null, dispatchedBy: null, cashToCollect: null, awaitsCourierCash: false,
      }
      : null,
  };
  data.orders.unshift(order);
  return order;
}

/** Vender: the sale the scenes charge, with a stable folio and its variants and extras. */
const sellRoutes: ExtraRoutes = (data, { ok }) => [
  ['POST', /^\/orders$/, (_match, _query, body: SaleRequest) => ok(createSale(data, body))],
];

export const SELL_ROUTES = asOption(sellRoutes);

/** The business takes Rappi orders: Vender offers it as an origin and Pedidos filters by it. */
const rappiRoutes: ExtraRoutes = (data, helpers) => [
  ['GET', /^\/delivery-platforms\/channels$/, () => helpers.ok([{ channel: RAPPI, isActive: true }])],
  ...sellRoutes(data, helpers),
];

export const RAPPI_ROUTES = asOption(rappiRoutes);

/** The business charges more in the Rappi app than in its menu (see withRappiPrices). */
export const RAPPI_PRICES_DATA = asOption(withRappiPrices);

/**
 * Two Rappi orders of today the platform charged in its app: one already delivered, for Juan, and one the kitchen has
 * not started. They are added in these scenes only, so the shared lists of today stay as they are.
 */
function withRappiOrders(data: DemoData) {
  const product = (name: string) => data.products.find((candidate: { name: string }) => candidate.name === name).id;
  const delivered = createSale(data, {
    channel: RAPPI, externalOrderNumber: '152390', collectedBy: 0, customerName: 'Juan',
    items: [{ productId: product('Chilaquiles verdes'), quantity: 2 }, { productId: product('Café americano'), quantity: 2 }],
  });
  delivered.createdAt = at(12, 40);
  delivered.payments.forEach((payment: { paidAt: string }) => { payment.paidAt = at(12, 40); });
  const waiting = createSale(data, {
    channel: RAPPI, externalOrderNumber: '152395', collectedBy: 0,
    items: [{ productId: product('Torta de jamón'), quantity: 1, notes: 'Sin chipotle' }, { productId: product('Agua de jamaica'), quantity: 1 }],
  });
  Object.assign(waiting, { status: 1, allowedTransitions: [4, 2, 99], createdAt: at(13, 15) });
  waiting.payments.forEach((payment: { paidAt: string }) => { payment.paidAt = at(13, 15); });
  // The kitchen sees the one it has not started, with its folio for the bag.
  data.kitchenOrders.push({
    id: waiting.id, orderNumber: waiting.orderNumber, channel: RAPPI, externalOrderNumber: '152395', status: waiting.status,
    customerName: waiting.customerName, tableNumber: null, customerNotes: null, createdAt: waiting.createdAt, items: waiting.items,
  });
}

export const RAPPI_ORDERS_DATA = asOption(withRappiOrders);

/**
 * The delivery zones of Café La Esquina (Fase 22B): Roma with a minimum of $150 and free delivery from $400, and
 * Condesa without a minimum. Kept in the data, so a sale and the zones listed agree.
 */
function deliveryZonesOf(data: DemoData): DeliveryZone[] {
  data.vpZones ??= [
    { id: 'zona-roma', name: 'Roma', fee: 35, minimumOrder: 150, freeDeliveryFrom: 400, isActive: true, revision: 1, postalCodes: ['06700', '06760'] },
    { id: 'zona-condesa', name: 'Condesa', fee: 45, minimumOrder: 0, freeDeliveryFrom: null, isActive: true, revision: 1, postalCodes: ['06140'] },
  ];
  return data.vpZones;
}

/** Normalizes a folio as the server does (PlatformOrderNumber): without # or spaces, in upper case. */
const normalizeFolio = (folio: string) => folio.trim().replace(/^#/, '').replace(/\s+/g, '').toUpperCase();

/** Vender and Pedidos of a business with delivery zones: «A domicilio» in the ticket and the trip of Paquetenvia. */
const deliveryRoutes: ExtraRoutes = (data, helpers) => [
  ['GET', /^\/delivery-zones$/, () => helpers.ok(deliveryZonesOf(data))],
  ['PUT', /^\/orders\/([^/]+)\/delivery\/shipment$/, (match, _query, body: { courier: number; folio: string; trackingUrl?: string }) => {
    const order = data.orders.find((candidate: { id: string }) => candidate.id === match[1]);
    Object.assign(order.delivery ?? {}, {
      courier: body.courier, courierFolio: normalizeFolio(body.folio), trackingUrl: body.trackingUrl ?? null,
      shipmentRegisteredAt: NOW_ISO, shipmentRegisteredBy: 'mariana.ortiz@cafelaesquina.mx',
    });
    return { status: 204, json: null };
  }],
  // Fase 22D: the order goes out with the courier, who collects its balance in cash or nothing.
  ['PUT', /^\/orders\/([^/]+)\/delivery\/dispatch$/, (match, _query, body: DispatchBody) => {
    const order = data.orders.find((candidate: { id: string }) => candidate.id === match[1]);
    Object.assign(order.delivery ?? {}, {
      courier: body.courier, courierFolio: normalizeFolio(body.folio), trackingUrl: body.trackingUrl ?? null,
      shipmentRegisteredAt: order.delivery?.shipmentRegisteredAt ?? NOW_ISO,
      shipmentRegisteredBy: order.delivery?.shipmentRegisteredBy ?? 'mariana.ortiz@cafelaesquina.mx',
      dispatchedAt: order.delivery?.dispatchedAt ?? NOW_ISO, dispatchedBy: order.delivery?.dispatchedBy ?? 'mariana.ortiz@cafelaesquina.mx',
      cashToCollect: body.collectsCash ? order.balance : null, paysWith: body.collectsCash ? body.paysWith ?? null : null,
    });
    return { status: 204, json: null };
  }],
  ...sellRoutes(data, helpers),
];

/** What «Salió a entregar» sends (DispatchOrderRequest). */
interface DispatchBody {
  courier: number;
  folio: string;
  trackingUrl?: string;
  collectsCash: boolean;
  paysWith?: number;
}

export const DELIVERY_ROUTES = asOption(deliveryRoutes);

/**
 * Two orders of today sent to an address: chilaquiles to Roma, whose trip is already in Paquetenvia, and tortas to
 * Condesa, still without a courier and in the kitchen. Added in these scenes only.
 */
function withDeliveryOrders(data: DemoData) {
  const product = (name: string) => data.products.find((candidate: { name: string }) => candidate.name === name).id;
  const roma = createSale(data, {
    customerName: 'Sofía Ramírez', customerPhone: '5523456789',
    items: [{ productId: product('Chilaquiles verdes'), quantity: 2 }, { productId: product('Café americano'), quantity: 2 }],
    delivery: { postalCode: '06700', addressLine: 'Colima 145, depto. 3, Roma Norte', references: 'Portón negro, timbre 3' },
  });
  // Paid by card when it was taken, and in the kitchen.
  roma.payments = [{
    id: `${roma.id}-pago-1`, amount: roma.total, method: 1, paidAt: at(12, 50), reference: null, notes: null, tip: 0, cashReceived: null, change: 0,
  }];
  Object.assign(roma, { paidAmount: roma.total, balance: 0, netPaidAmount: roma.total, canCancel: false, status: 2, allowedTransitions: [3], createdAt: at(12, 50) });
  Object.assign(roma.delivery!, {
    courier: 1, courierFolio: 'PQ48213', trackingUrl: 'https://paquetenvia.mx/rastreo/PQ48213',
    shipmentRegisteredAt: at(13, 5), shipmentRegisteredBy: 'lucia.navarro@cafelaesquina.mx',
  });
  const condesa = createSale(data, {
    customerName: 'Diego Hernández', customerPhone: '5534567890',
    items: [{ productId: product('Torta de jamón'), quantity: 2 }, { productId: product('Agua de jamaica'), quantity: 2 }],
    delivery: { postalCode: '06140', addressLine: 'Ámsterdam 210, Condesa' },
  });
  Object.assign(condesa, { createdAt: at(13, 10) });
  data.kitchenOrders.push({
    id: condesa.id, orderNumber: condesa.orderNumber, channel: 0, externalOrderNumber: null, isDelivery: true, status: condesa.status,
    customerName: condesa.customerName, tableNumber: null, customerNotes: null, createdAt: condesa.createdAt, items: condesa.items,
  });
}

export const DELIVERY_ORDERS_DATA = asOption(withDeliveryOrders);

/**
 * Fase 22D, on top of the two orders above: molletes to Roma on their way with Paquetenvia, who collects $225 and takes
 * the change of $500, and tortas to Condesa it delivered and still owes ($215). Added in these scenes only.
 */
function withCourierOrders(data: DemoData) {
  withDeliveryOrders(data);
  const product = (name: string) => data.products.find((candidate: { name: string }) => candidate.name === name).id;
  const onTheWay = createSale(data, {
    customerName: 'Valeria Torres', customerPhone: '5545678901',
    items: [{ productId: product('Molletes con pico de gallo'), quantity: 2 }, { productId: product('Café americano'), quantity: 2 }],
    delivery: { postalCode: '06700', addressLine: 'Orizaba 87, Roma Norte', paysWith: 500 },
  });
  Object.assign(onTheWay, { status: 3, allowedTransitions: [6, 99], createdAt: at(12, 35) });
  Object.assign(onTheWay.delivery!, {
    courier: 1, courierFolio: 'PQ48219', trackingUrl: 'https://paquetenvia.mx/rastreo/PQ48219',
    shipmentRegisteredAt: at(13, 0), shipmentRegisteredBy: 'lucia.navarro@cafelaesquina.mx',
    dispatchedAt: at(13, 12), dispatchedBy: 'lucia.navarro@cafelaesquina.mx', cashToCollect: onTheWay.balance,
  });
  const owed = createSale(data, {
    customerName: 'Andrés Morales', customerPhone: '5556789012',
    items: [{ productId: product('Torta de jamón'), quantity: 2 }, { productId: product('Agua de jamaica'), quantity: 1 }],
    delivery: { postalCode: '06140', addressLine: 'Tamaulipas 66, Condesa' },
  });
  Object.assign(owed, { status: 6, allowedTransitions: [], canCancel: false, createdAt: at(12, 55) });
  Object.assign(owed.delivery!, {
    courier: 1, courierFolio: 'PQ48197', shipmentRegisteredAt: at(13, 0), shipmentRegisteredBy: 'lucia.navarro@cafelaesquina.mx',
    dispatchedAt: at(13, 2), dispatchedBy: 'lucia.navarro@cafelaesquina.mx', cashToCollect: owed.balance, awaitsCourierCash: true,
  });
}

export const COURIER_ORDERS_DATA = asOption(withCourierOrders);

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
