// The TienditApp API as the visual scenes see it: answers from data.mjs through Playwright request interception.
// - /api/v1/**  -> the routes below (and each scene file's own); a call without a route fails the scene. A route answers
//   JSON, or a file with `body` and `contentType` (the photos of a service, /api/v1/files/**)
// - /hubs/**    -> 404 (SignalR does not connect; the screens work without live updates)
// - /uploads/** -> 404, and /visual-img/** -> the demo pictures in ./img (product photos, logo)
// - any other host -> aborted, so a run never depends on the network
import { readFileSync, existsSync } from 'node:fs';
import path from 'node:path';
import { buildData, NOW_ISO } from './data.mjs';
import { phase2Routes } from './routes-p2.mjs';
import { phase3Routes } from './routes-p3.mjs';
import { phase4Routes } from './routes-p4.mjs';
import { phase4eRoutes } from './routes-p5.mjs';
import { phase5aRoutes } from './routes-p6.mjs';
import { phase5eRoutes } from './routes-p7.mjs';
import { phase22eRoutes } from './routes-p8.mjs';
import { phase24aRoutes } from './routes-p9.mjs';
import { phase24bRoutes } from './routes-p10.mjs';
import { phase25Routes } from './routes-p11.mjs';
import { phase24dRoutes } from './routes-p12.mjs';

const HERE = path.dirname(new URL(import.meta.url).pathname);
export { NOW_ISO };

const b64url = (obj) => Buffer.from(JSON.stringify(obj)).toString('base64url');

/** Unsigned JWT: the client only reads `exp` and `tenant_id` from it (api/axios.ts readTokenClaims). */
export function makeFakeJwt(data) {
  const payload = {
    sub: data.USER_ID,
    email: data.OWNER_EMAIL,
    name: data.OWNER_EMAIL,
    tenant_id: data.TENANT_ID,
    role: 'TenantAdmin',
    tenant_role: 'Owner',
    permission: data.profile.permissions,
    capabilities: String(data.tenant.capabilities),
    iat: Math.floor(new Date(NOW_ISO).getTime() / 1000),
    exp: Math.floor(Date.UTC(2030, 0, 1) / 1000),
  };
  return `${b64url({ alg: 'none', typ: 'JWT' })}.${b64url(payload)}.`;
}

function paginate(items, q, defaultSize = 10) {
  const pageNumber = Math.max(1, parseInt(q.get('pageNumber') ?? '1', 10) || 1);
  const pageSize = Math.max(1, parseInt(q.get('pageSize') ?? String(defaultSize), 10) || defaultSize);
  const totalCount = items.length;
  const totalPages = Math.ceil(totalCount / pageSize);
  return {
    items: items.slice((pageNumber - 1) * pageSize, pageNumber * pageSize),
    pageNumber,
    totalPages,
    totalCount,
    hasPreviousPage: pageNumber > 1,
    hasNextPage: pageNumber < totalPages,
  };
}

const bool = (q, key) => (q.has(key) ? q.get(key) === 'true' : undefined);
const num = (q, key) => (q.has(key) && q.get(key) !== '' ? Number(q.get(key)) : undefined);
const includesCI = (value, search) => (value ?? '').toString().toLowerCase().includes(search);
const stripProduct = ({ _itemType, ...rest }) => rest;

function localHhmmss(iso) {
  const d = new Date(new Date(iso).getTime() - 6 * 3600 * 1000); // CDMX wall clock
  return d.toISOString().slice(11, 19);
}

const cents = (x) => Math.round(x * 100);
function roundHalfEvenDiv(numerator, denominator) {
  // numerator/denominator rounded to an integer with banker's rounding (inputs are integers).
  const q = Math.floor(numerator / denominator);
  const r = numerator - q * denominator;
  if (r * 2 > denominator) return q + 1;
  if (r * 2 < denominator) return q;
  return q % 2 === 0 ? q : q + 1;
}

/** Same algorithm as OrderTaxCalculator: group by rate, exempt/0 % untaxed, rates in descending order. */
function computeTaxes(lines, pricesIncludeTax) {
  const groups = new Map();
  for (const line of lines) {
    const key = line.isTaxExempt ? 'exempt' : String(line.taxRate);
    const g = groups.get(key) ?? { ratePercent: line.isTaxExempt ? 0 : line.taxRate, isExempt: line.isTaxExempt, amountCents: 0 };
    g.amountCents += cents(line.totalPrice);
    groups.set(key, g);
  }
  const breakdown = [...groups.values()].map((g) => {
    const rateBp = Math.round(g.ratePercent * 100); // basis points
    let taxCents = 0;
    if (!g.isExempt && rateBp > 0) {
      taxCents = pricesIncludeTax ? roundHalfEvenDiv(g.amountCents * rateBp, 10000 + rateBp) : roundHalfEvenDiv(g.amountCents * rateBp, 10000);
    }
    const baseCents = pricesIncludeTax ? g.amountCents - taxCents : g.amountCents;
    return { ratePercent: g.ratePercent, isExempt: g.isExempt, taxableBase: baseCents / 100, taxAmount: taxCents / 100, total: (baseCents + taxCents) / 100 };
  }).sort((a, b) => (a.isExempt - b.isExempt) || (b.ratePercent - a.ratePercent));
  const subTotal = breakdown.reduce((s, l) => s + cents(l.taxableBase), 0) / 100;
  const tax = breakdown.reduce((s, l) => s + cents(l.taxAmount), 0) / 100;
  return { breakdown, subTotal, tax, total: Math.round((subTotal + tax) * 100) / 100 };
}

/** A stored payment as the API returns it: the tip and the cash handed over travel with it, the change is the rest. */
function mockPayment(id, pay) {
  const amount = Math.round(Number(pay?.amount ?? 0) * 100) / 100;
  const tip = Math.round(Number(pay?.tip ?? 0) * 100) / 100;
  const cashReceived = pay?.cashReceived ?? null;
  return {
    id, amount, method: pay?.method ?? 0, paidAt: NOW_ISO, reference: pay?.reference, notes: pay?.notes,
    tip, cashReceived, change: cashReceived === null ? 0 : Math.round((cashReceived - amount - tip) * 100) / 100,
  };
}

function createMockOrder(data, body) {
  const items = (body?.items ?? []).map((it, idx) => {
    const p = data.products.find((x) => x.id === it.productId);
    const rate = p.isTaxExempt ? 0 : (p.taxRate ?? data.tenant.defaultTaxRate);
    return {
      id: `item-${idx}`, productId: p.id, productName: p.name, quantity: it.quantity, unitPrice: p.priceAmount,
      totalPrice: Math.round(p.priceAmount * it.quantity * 100) / 100, notes: it.notes, taxRate: rate, isTaxExempt: !!p.isTaxExempt,
    };
  });
  const taxes = computeTaxes(items, data.tenant.pricesIncludeTax);
  if (body?.expectedTotal !== undefined && Math.abs(body.expectedTotal - taxes.total) > 0.001) {
    console.warn(`[mock] expectedTotal ${body.expectedTotal} != ${taxes.total}`);
  }
  const payments = (body?.payments ?? []).map((pay, idx) => mockPayment(`pay-${idx}`, pay));
  const paidAmount = Math.round(payments.reduce((s, x) => s + x.amount, 0) * 100) / 100;
  // Counted per scene (data is built for each one): a folio that depends on the scenes before it would change by worker.
  data.mockOrderSeq = (data.mockOrderSeq ?? 400) + 1;
  const seq = data.mockOrderSeq;
  const isService = body?.orderType === 1;
  const paid = paidAmount >= taxes.total && items.length > 0;
  const status = paid && data.tenant.autoCloseCounterSales ? (isService ? 7 : 6) : 1;
  const order = {
    id: `mock-order-${seq}`, createdByBusiness: true, orderNumber: `ORD-${String(seq).padStart(6, '0')}`,
    businessType: 0, orderType: body?.orderType ?? 0, status, canCancel: payments.length === 0, allowedTransitions: status === 1 ? [4, 2, 99] : [],
    customerName: body?.customerName || 'Público en general', customerPhone: body?.customerPhone, customerNotes: body?.customerNotes, tableNumber: body?.tableNumber,
    pricesIncludeTax: data.tenant.pricesIncludeTax, taxBreakdown: taxes.breakdown, subTotal: taxes.subTotal, tax: taxes.tax, total: taxes.total,
    paidAmount, balance: Math.round((taxes.total - paidAmount) * 100) / 100, createdAt: NOW_ISO, items, statusHistory: [], servicePieces: [], payments,
    tipTotal: Math.round(payments.reduce((s, x) => s + x.tip, 0) * 100) / 100,
  };
  data.orders.unshift(order);
  return order;
}

/** Rappi, Uber Eats and DiDi Food as GET /delivery-platforms answers them to a business that never saved one. */
function unconfiguredPlatforms() {
  return {
    platforms: [3, 4, 5].map((channel) => ({
      channel, isConfigured: false, isActive: false, commissionPercent: null, commissionBase: 0, commissionVatPercent: 16,
      isrWithholdingPercent: 2.5, vatWithholdingPercent: 50, revision: 0, changedAt: null, changedBy: null, example: null,
    })),
  };
}

// Hour, Session and Service (UnitType): a service is never in an order of a delivery platform.
const SERVICE_UNITS = [50, 51, 52];

/**
 * A product of the list for an order of a delivery platform, as GET /products?priceChannel= prices it: the app's price
 * where the business set one (without the menu's offer, which is the menu's) and what each option adds in the app.
 * `prices` is `data.platformPrices[channel]`: `{ products: { [productId]: price }, options: { [optionId]: delta } }`.
 */
function withPlatformPrices(product, prices) {
  if (!prices) return product;
  const price = prices.products[product.id];
  return {
    ...product,
    ...(price === undefined ? {} : { priceAmount: price, compareAtPrice: null, hasDiscount: false }),
    optionGroups: product.optionGroups?.map((group) => ({
      ...group,
      options: group.options.map((option) => (option.id in prices.options ? { ...option, priceDelta: prices.options[option.id] } : option)),
    })),
  };
}

/** Catálogo › Precios en apps: the products (no services) by category and name, with their menu's and app's prices. */
function platformPriceList(data, channel) {
  const prices = data.platformPrices[channel] ?? { products: {}, options: {} };
  const categories = new Map(data.categories.map((category) => [category.id, category]));
  return data.products
    .filter((product) => !SERVICE_UNITS.includes(product.unit))
    .map((product) => ({ product, category: categories.get(product.categoryId) }))
    .sort((a, b) => a.category.sortOrder - b.category.sortOrder || a.category.name.localeCompare(b.category.name, 'es')
      || a.product.name.localeCompare(b.product.name, 'es'))
    .map(({ product, category }) => ({
      productId: product.id, name: product.name, categoryId: category.id, categoryName: category.name, unit: product.unit,
      isAvailable: product.isAvailable, menuPrice: product.priceAmount, platformPrice: prices.products[product.id] ?? null,
      optionGroups: (product.optionGroups ?? []).map((group) => ({
        id: group.id, name: group.name,
        options: group.options.map((option) => ({
          id: option.id, name: option.name, isAvailable: option.isAvailable, menuPriceDelta: option.priceDelta,
          platformPriceDelta: prices.options[option.id] ?? null,
        })),
      })),
    }));
}

/** PUT /delivery-platforms/{channel}/prices: each price sent replaces the saved one; null goes back to the menu's. */
function savePlatformPrices(data, channel, body) {
  const prices = (data.platformPrices[channel] ??= { products: {}, options: {} });
  const set = (map, key, value) => {
    if (value === null) delete map[key];
    else map[key] = value;
  };
  for (const product of body?.products ?? []) {
    set(prices.products, product.productId, product.price);
    for (const option of product.options ?? []) set(prices.options, option.optionId, option.priceDelta);
  }
}

/** Sales by channel of a period without orders of any channel: the demo shows its sales by branch, not by channel. */
function emptySalesByChannel(q) {
  const totals = {
    channel: null, isDeliveryPlatform: false, paidOrders: 0, paymentsAmount: 0, refundsAmount: 0, netSales: 0, platformCollected: 0,
    businessCollected: 0, commission: 0, commissionTax: 0, isrWithheld: 0, vatWithheld: 0, expectedSettlement: 0,
  };
  return { from: q.get('from') || '2026-10-01', to: q.get('to') || '2026-10-04', channels: [], totals };
}

function buildRoutes(data, token) {
  const ok = (json) => ({ status: 200, json });
  const notFound = (what) => ({ status: 404, json: { error: `${what} no encontrado` } });
  const NOW_LOCAL = localHhmmss(NOW_ISO);

  return [
    ...phase24dRoutes(data, { ok, notFound }),
    ...phase25Routes(data, { ok, notFound }),
    ...phase24bRoutes(data, { ok, notFound }),
    ...phase24aRoutes(data, { ok, paginate }),
    ...phase22eRoutes(data, { ok, notFound }),
    ...phase5eRoutes(data, { ok, notFound }),
    ...phase5aRoutes(data, { ok, notFound }),
    ...phase4eRoutes(Object.assign(data, { token }), { ok }),
    ...phase4Routes(data, { ok, notFound, paginate, createMockOrder }),
    ...phase2Routes(data, { ok, notFound, paginate }),
    ...phase3Routes(data, { ok, notFound }),
    // ------------------------------------------------------------- auth / session
    ['POST', /^\/auth\/refresh-token$/, () => ok({ accessToken: token, expiresAt: '2030-01-01T00:00:00Z', requiresTwoFactor: false })],
    ['POST', /^\/auth\/login$/, () => ok({ accessToken: token, expiresAt: '2030-01-01T00:00:00Z', requiresTwoFactor: false, userId: data.USER_ID })],
    ['POST', /^\/auth\/logout$/, () => ok({ message: 'Sesión cerrada.' })],
    ['GET', /^\/auth\/profile$/, () => ok(data.profile)],
    ['GET', /^\/auth\/my-tenant$/, () => ok(data.tenant)],
    ['GET', /^\/auth\/my-tenant\/whatsapp$/, () => ok(data.whatsAppConfig)],
    ['GET', /^\/me\/tenants$/, () => ok(data.myTenants)],
    ['POST', /^\/auth\/switch-tenant$/, () => ok({ accessToken: token, requiresTwoFactor: false })],

    // ------------------------------------------------------------- dashboard / reports / audit
    ['GET', /^\/dashboard\/metrics$/, () => ok(data.dashboard)],
    ['GET', /^\/reports\/operational-summary$/, () => ok(data.report)],
    ['GET', /^\/reports\/sales-by-channel$/, (m, q) => ok(emptySalesByChannel(q))],
    ['GET', /^\/audit\/logs$/, (m, q) => {
      let rows = data.auditLogs;
      if (q.get('entityName')) rows = rows.filter((r) => includesCI(r.entityName, q.get('entityName').toLowerCase()));
      if (q.get('action')) rows = rows.filter((r) => includesCI(r.action, q.get('action').toLowerCase()));
      if (q.get('from')) rows = rows.filter((r) => r.timestamp >= q.get('from'));
      if (q.get('to')) rows = rows.filter((r) => r.timestamp <= q.get('to'));
      return ok(paginate(rows, q, 20));
    }],

    // ------------------------------------------------------------- catalog
    ['GET', /^\/categories$/, () => ok(data.categories)],
    ['GET', /^\/categories\/templates$/, () => ok(data.categoryTemplates)],
    ['GET', /^\/categories\/([^/]+)$/, (m) => {
      const c = data.categories.find((x) => x.id === m[1]);
      return c ? ok(c) : notFound('Categoría');
    }],
    ['GET', /^\/products$/, (m, q) => {
      let rows = data.products;
      if (q.get('itemType')) rows = rows.filter((p) => p._itemType === q.get('itemType'));
      if (q.get('categoryId')) rows = rows.filter((p) => p.categoryId === q.get('categoryId'));
      const productSearch = (q.get('search') ?? '').toLowerCase();
      if (productSearch) rows = rows.filter((p) => includesCI(p.name, productSearch) || includesCI(p.sku, productSearch) || includesCI(p.barcode, productSearch));
      const isAvailable = bool(q, 'isAvailable');
      if (isAvailable !== undefined) rows = rows.filter((p) => p.isAvailable === isAvailable);
      if (bool(q, 'onlyAvailableNow')) {
        rows = rows.filter((p) => !p.availableFromTime || !p.availableToTime
          || (p.availableFromTime <= NOW_LOCAL && NOW_LOCAL <= p.availableToTime));
      }
      rows = [...rows].sort((a, b) => a.sortOrder - b.sortOrder);
      const page = paginate(rows, q, 10);
      const prices = q.has('priceChannel') ? data.platformPrices[q.get('priceChannel')] : undefined;
      return ok({ ...page, items: page.items.map((p) => withPlatformPrices(stripProduct(p), prices)) });
    }],
    ['GET', /^\/products\/([^/]+)$/, (m) => {
      const p = data.products.find((x) => x.id === m[1]);
      return p ? ok(stripProduct(p)) : notFound('Producto');
    }],

    // ------------------------------------------------------------- customers
    ['GET', /^\/customers$/, (m, q) => {
      let rows = data.customers;
      const search = (q.get('search') ?? '').toLowerCase();
      if (search) rows = rows.filter((c) => includesCI(c.name, search) || includesCI(c.phone, search) || includesCI(c.email, search));
      const isActive = bool(q, 'isActive');
      if (isActive !== undefined) rows = rows.filter((c) => c.isActive === isActive);
      return ok(paginate(rows, q, 10));
    }],
    ['GET', /^\/customers\/([^/]+)$/, (m) => {
      const c = data.customers.find((x) => x.id === m[1]);
      return c ? ok(c) : notFound('Cliente');
    }],

    // ------------------------------------------------------------- orders / kitchen / service board
    ['GET', /^\/orders$/, (m, q) => {
      let rows = data.orders;
      const orderType = num(q, 'orderType');
      const status = num(q, 'status');
      if (orderType !== undefined) rows = rows.filter((o) => o.orderType === orderType);
      if (status !== undefined) rows = rows.filter((o) => o.status === status);
      const channel = num(q, 'channel');
      if (channel !== undefined) rows = rows.filter((o) => o.channel === channel);
      if (q.get('fromDate')) rows = rows.filter((o) => o.createdAt >= q.get('fromDate'));
      if (q.get('toDate')) rows = rows.filter((o) => o.createdAt <= q.get('toDate'));
      const orderSearch = (q.get('search') ?? '').toLowerCase();
      if (orderSearch) rows = rows.filter((o) => includesCI(o.orderNumber, orderSearch) || includesCI(o.customerName, orderSearch) || includesCI(o.customerPhone, orderSearch));
      return ok(paginate([...rows].sort((a, b) => b.createdAt.localeCompare(a.createdAt)), q, 10));
    }],
    ['GET', /^\/orders\/([^/]+)$/, (m) => {
      const o = data.orders.find((x) => x.id === m[1]);
      return o ? ok(o) : notFound('Orden');
    }],
    ['POST', /^\/orders$/, (m, q, body) => ok(createMockOrder(data, body))],
    ['POST', /^\/orders\/([^/]+)\/payments$/, (m, q, body) => {
      const o = data.orders.find((x) => x.id === m[1]);
      if (!o) return notFound('Orden');
      const payment = mockPayment(`pay-${o.payments.length + 1}-${o.id}`, body);
      const amount = payment.amount;
      o.payments.push(payment);
      o.tipTotal = Math.round(((o.tipTotal ?? 0) + payment.tip) * 100) / 100;
      o.paidAmount = Math.round((o.paidAmount + amount) * 100) / 100;
      o.balance = Math.round((o.total - o.paidAmount) * 100) / 100;
      o.canCancel = false;
      return ok(payment);
    }],
    ['GET', /^\/auth\/my-tenant\/setup$/, () => ok(data.setup)],
    ['GET', /^\/auth\/my-tenant\/special-days$/, () => ok(data.specialDays)],
    ['PUT', /^\/auth\/my-tenant\/sales-policy$/, (m, q, body) => {
      Object.assign(data.tenant, body);
      Object.assign(data.setup, { pricesIncludeTax: body.pricesIncludeTax, defaultTaxRate: body.defaultTaxRate });
      return ok(body);
    }],
    ['POST', /^\/auth\/my-tenant\/setup\/starter-catalog$/, (m, q, body) => {
      const tpl = data.setup.starterCatalogs.find((t) => t.id === body?.templateId);
      return tpl ? ok({ templateId: tpl.id, templateName: tpl.name, categoriesCreated: tpl.categories.length, productsCreated: tpl.productCount })
        : { status: 400, json: { error: 'Plantilla no encontrada.' } };
    }],
    ['POST', /^\/auth\/my-tenant\/setup\/complete$/, () => {
      data.tenant.onboardingCompletedAt = NOW_ISO;
      data.setup.completedAt = NOW_ISO;
      return { status: 204, json: null };
    }],
    ['GET', /^\/kitchen\/orders$/, () => ok(data.kitchenOrders)],
    // The demo business set up no delivery platform: Vender and Pedidos look as they do without them, and Mi negocio
    // offers the three to configure.
    ['GET', /^\/delivery-platforms\/channels$/, () => ok([])],
    // Nor any delivery zone: Vender offers no «A domicilio» and the agenda no visits (a scene file with zones sets
    // `data.deliveryZones`; Configuración › Zonas de entrega answers its own).
    ['GET', /^\/delivery-zones$/, () => ok(data.deliveryZones ?? [])],
    // Nor has a courier collected anything (Fase 22D): Caja shows no Repartidores tab.
    ['GET', /^\/courier-settlements\/receivable$/, () => ok(data.courierReceivables ?? [{
      courier: 1, owedOrders: 0, owedAmount: 0, oldestDispatchedAt: null, onTheWayOrders: 0, onTheWayAmount: 0, lastSettledAt: null,
    }])],
    ['GET', /^\/delivery-platforms$/, () => ok(unconfiguredPlatforms())],
    ['GET', /^\/delivery-platforms\/([345])\/prices$/, (m, q) => ok(paginate(platformPriceList(data, m[1]), q, 100))],
    ['PUT', /^\/delivery-platforms\/([345])\/prices$/, (m, q, body) => {
      savePlatformPrices(data, m[1], body);
      return { status: 204, json: null };
    }],
    ['GET', /^\/service-board\/pieces$/, (m, q) => {
      let rows = data.boardPieces;
      if (!bool(q, 'includeCompleted')) rows = rows.filter((p) => p.status !== 2);
      if (!bool(q, 'includeCancelled')) rows = rows.filter((p) => p.status !== 99);
      if (bool(q, 'onlyMine')) rows = rows.filter((p) => p.assignedToUserId === data.USER_ID);
      const status = num(q, 'status');
      if (status !== undefined) rows = rows.filter((p) => p.status === status);
      const search = (q.get('search') ?? '').toLowerCase();
      if (search) rows = rows.filter((p) => includesCI(p.orderNumber, search) || includesCI(p.customerName, search) || includesCI(p.itemName, search));
      return ok(paginate(rows, q, 300));
    }],

    // ------------------------------------------------------------- cash
    ['GET', /^\/cash\/current-session$/, () => ok(data.cashSession)],
    ['GET', /^\/cash\/transactions$/, (m, q) => {
      let rows = data.cashTx;
      const type = num(q, 'transactionType');
      if (type !== undefined) rows = rows.filter((t) => t.transactionType === type);
      return ok(paginate(rows, q, 10));
    }],
    ['GET', /^\/cash\/daily-cut$/, () => ok(data.dailyCut)],

    // ------------------------------------------------------------- inventory / branches
    ['GET', /^\/(inventory\/locations|branches)$/, (m, q) => {
      const isActive = bool(q, 'isActive');
      return ok(isActive === undefined ? data.locations : data.locations.filter((l) => l.isActive === isActive));
    }],
    ['GET', /^\/inventory\/supplies$/, (m, q) => {
      let rows = data.supplies;
      const isActive = bool(q, 'isActive');
      if (isActive !== undefined) rows = rows.filter((s) => s.isActive === isActive);
      if (bool(q, 'lowStockOnly')) rows = rows.filter((s) => s.isLowStock);
      const search = (q.get('search') ?? '').toLowerCase();
      if (search) rows = rows.filter((s) => includesCI(s.name, search) || includesCI(s.code, search));
      return ok(paginate(rows, q, 10));
    }],
    ['GET', /^\/inventory\/supplies\/([^/]+)$/, (m) => {
      const s = data.supplies.find((x) => x.id === m[1]);
      return s ? ok(s) : notFound('Insumo');
    }],
    ['GET', /^\/inventory\/stocks$/, (m, q) => {
      let rows = data.stockRows;
      if (q.get('inventoryLocationId')) rows = rows.filter((s) => s.inventoryLocationId === q.get('inventoryLocationId'));
      if (bool(q, 'lowStockOnly')) rows = rows.filter((s) => s.isLowStock);
      return ok(paginate(rows, q, 10));
    }],
    ['GET', /^\/inventory\/movements$/, (m, q) => {
      let rows = data.movements;
      const type = num(q, 'movementType');
      const reason = num(q, 'movementReason');
      if (type !== undefined) rows = rows.filter((x) => x.movementType === type);
      if (reason !== undefined) rows = rows.filter((x) => x.movementReason === reason);
      return ok(paginate(rows, q, 10));
    }],
    ['GET', /^\/inventory\/movements\/([^/]+)$/, (m) => {
      const mv = data.movements.find((x) => x.id === m[1]);
      return mv ? ok(mv) : notFound('Movimiento');
    }],
    ['GET', /^\/inventory\/kardex$/, (m, q) => {
      let rows = data.kardex;
      if (q.get('supplyItemId')) rows = rows.filter((k) => k.supplyItemId === q.get('supplyItemId'));
      if (q.get('inventoryLocationId')) rows = rows.filter((k) => k.inventoryLocationId === q.get('inventoryLocationId'));
      return ok(paginate(rows, q, 10));
    }],
    ['GET', /^\/inventory\/alerts$/, () => ok(data.alerts)],
    ['GET', /^\/inventory\/recipes\/products\/([^/]+)$/, (m) => {
      if (data.recipes[m[1]]) return ok(data.recipes[m[1]]);
      const p = data.products.find((x) => x.id === m[1]);
      return p ? ok({ productId: p.id, productName: p.name, productIsAvailable: p.isAvailable, lines: [] }) : notFound('Producto');
    }],

    // ------------------------------------------------------------- purchasing
    ['GET', /^\/suppliers$/, (m, q) => {
      let rows = data.suppliers;
      const isActive = bool(q, 'isActive');
      if (isActive !== undefined) rows = rows.filter((s) => s.isActive === isActive);
      const search = (q.get('search') ?? '').toLowerCase();
      if (search) rows = rows.filter((s) => includesCI(s.name, search) || includesCI(s.contactName, search));
      return ok(paginate(rows, q, 10));
    }],
    ['GET', /^\/suppliers\/([^/]+)$/, (m) => {
      const s = data.suppliers.find((x) => x.id === m[1]);
      return s ? ok(s) : notFound('Proveedor');
    }],
    ['GET', /^\/purchaseorders$/, (m, q) => {
      let rows = data.purchaseOrders;
      if (q.get('supplierId')) rows = rows.filter((p) => p.supplierId === q.get('supplierId'));
      const status = num(q, 'status');
      if (status !== undefined) rows = rows.filter((p) => p.status === status);
      return ok(paginate(rows, q, 10));
    }],
    ['GET', /^\/purchaseorders\/([^/]+)$/, (m) => {
      const p = data.purchaseOrders.find((x) => x.id === m[1]);
      return p ? ok(p) : notFound('Orden de compra');
    }],

    // ------------------------------------------------------------- appointments
    ['GET', /^\/appointments$/, (m, q) => {
      let rows = data.appointments;
      const status = num(q, 'status');
      if (status !== undefined) rows = rows.filter((a) => a.status === status);
      const search = (q.get('search') ?? '').toLowerCase();
      if (search) rows = rows.filter((a) => includesCI(a.customerName, search) || includesCI(a.serviceName, search));
      return ok(paginate(rows, q, 10));
    }],

    // ------------------------------------------------------------- employees / AI / WhatsApp
    ['GET', /^\/employees\/permission-options$/, () => ok(data.permissionOptions)],
    ['GET', /^\/employees$/, (m, q) => {
      let rows = data.employees;
      const search = (q.get('search') ?? '').toLowerCase();
      if (search) rows = rows.filter((e) => includesCI(`${e.firstName} ${e.lastName}`, search) || includesCI(e.email, search));
      if (q.get('appRole')) rows = rows.filter((e) => e.appRole === q.get('appRole'));
      if (q.get('tenantRole')) rows = rows.filter((e) => e.tenantRole === q.get('tenantRole'));
      if (q.get('status')) rows = rows.filter((e) => e.status === q.get('status'));
      return ok(paginate(rows, q, 10));
    }],
    ['GET', /^\/employees\/([^/]+)$/, (m) => {
      const e = data.employees.find((x) => x.tenantUserId === m[1]);
      return e ? ok(e) : notFound('Empleado');
    }],
    ['GET', /^\/ai-agents$/, (m, q) => {
      const isActive = bool(q, 'isActive');
      return ok(isActive === undefined ? data.aiAgents : data.aiAgents.filter((a) => a.isActive === isActive));
    }],
    ['GET', /^\/ai-agents\/([^/]+)$/, (m) => {
      const a = data.aiAgents.find((x) => x.id === m[1]);
      return a ? ok(a) : notFound('Agente');
    }],
    ['GET', /^\/whatsapp\/chat$/, (m, q) => {
      let rows = data.conversations;
      const search = (q.get('search') ?? '').toLowerCase();
      if (search) rows = rows.filter((c) => includesCI(c.customerName, search) || includesCI(c.customerPhone, search));
      return ok(paginate(rows, q, 20));
    }],
    ['GET', /^\/whatsapp\/chat\/([^/]+)\/messages$/, (m, q) => ok(paginate(data.messagesByConversation[m[1]] ?? [], q, 50))],
    ['PUT', /^\/whatsapp\/chat\/([^/]+)\/read$/, () => ok({ message: 'Conversación marcada como leída.' })],
    ['POST', /^\/whatsapp\/chat\/send$/, (m, q, body) => ok({
      id: `mock-${Date.now()}`, conversationId: body?.conversationId ?? data.conversations[0].id, direction: 'Outbound',
      body: body?.message ?? '', status: 'Sent', sentAt: NOW_ISO,
    })],
  ];
}

/**
 * Answers the app's requests in `context` from the demo data. Options:
 * - `origin`: the address the app is served from (only its requests are answered);
 * - `profile`: changes to the signed-in user (role, permissions…) to see the app as someone else;
 * - `prepareData(data)`: changes to the data before the first request;
 * - `extraRoutes(data, helpers)`: routes of a scene file, tried before the shared ones.
 * Returns the data and the list of calls nothing answered: the fixture fails the scene when it is not empty.
 * @param {import('@playwright/test').BrowserContext} context
 */
export async function installMocks(context, opts = {}) {
  const origin = opts.origin;
  const data = buildData();
  if (opts.profile) Object.assign(data.profile, opts.profile);
  opts.prepareData?.(data);
  const token = makeFakeJwt(data);
  const routes = [
    ...(opts.extraRoutes?.(data, { ok: (json) => ({ status: 200, json }), paginate }) ?? []),
    ...buildRoutes(data, token),
  ];
  /** @type {string[]} */
  const unmocked = [];

  // Any other host is aborted: a run never depends on the network.
  await context.route((url) => url.origin !== origin, (route) => route.abort('blockedbyclient'));

  await context.route((url) => url.origin === origin && url.pathname.startsWith('/hubs/'), (route) => route.fulfill({
    status: 404, contentType: 'text/plain', body: 'SignalR no está en las capturas',
  }));

  await context.route((url) => url.origin === origin && url.pathname.startsWith('/uploads/'), (route) => route.fulfill({ status: 404, body: '' }));

  await context.route((url) => url.origin === origin && url.pathname.startsWith('/visual-img/'), (route) => {
    const file = path.join(HERE, 'img', path.basename(new URL(route.request().url()).pathname));
    if (!existsSync(file)) return route.fulfill({ status: 404, body: '' });
    return route.fulfill({ status: 200, contentType: 'image/svg+xml', body: readFileSync(file) });
  });

  await context.route((url) => url.origin === origin && url.pathname.startsWith('/api/'), async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const method = request.method();
    const apiPath = url.pathname.replace(/^\/api\/v1/, '');
    let body;
    try { body = request.postDataJSON(); } catch { body = undefined; }

    let result;
    for (const [m, re, handler] of routes) {
      if (m !== method) continue;
      const match = apiPath.match(re);
      if (match) { result = handler(match, url.searchParams, body); if (result) break; }
    }
    if (!result) {
      unmocked.push(`${method} ${apiPath}${url.search}`);
      result = { status: 501, json: { message: 'Sin respuesta simulada en client/visual/mocks.' } };
    }
    if (result.body !== undefined) {
      await route.fulfill({ status: result.status, contentType: result.contentType, body: result.body });
      return;
    }
    await route.fulfill({ status: result.status, contentType: 'application/json; charset=utf-8', body: JSON.stringify(result.json) });
  });

  return { data, token, unmocked };
}
