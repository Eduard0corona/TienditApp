// Phase 3 endpoints for the fake backend: CFDI preview, option groups, tables, open tabs and kitchen rounds.
import { NOW_ISO } from './data.mjs';

const ago = (minutes) => new Date(new Date(NOW_ISO).getTime() - minutes * 60000).toISOString();
const r2 = (value) => Math.round(value * 100) / 100;

function seedPhase3(data) {
  if (data.p3) return data.p3;
  const latte = data.products.find((p) => p.name === 'Latte');
  if (latte) {
    latte.optionGroups = [
      { id: 'g-size', name: 'Tamaño', minSelections: 1, maxSelections: 1, options: [
        { id: 'o-ch', name: 'Chico', priceDelta: -5, isAvailable: true },
        { id: 'o-md', name: 'Mediano', priceDelta: 0, isAvailable: true },
        { id: 'o-gr', name: 'Grande', priceDelta: 12, isAvailable: true },
      ] },
      { id: 'g-milk', name: 'Leche', minSelections: 0, maxSelections: 1, options: [
        { id: 'o-des', name: 'Deslactosada', priceDelta: 0, isAvailable: true },
        { id: 'o-alm', name: 'Almendra', priceDelta: 8, isAvailable: true },
        { id: 'o-ave', name: 'Avena', priceDelta: 8, isAvailable: false },
      ] },
      { id: 'g-extra', name: 'Extras', minSelections: 0, maxSelections: 2, options: [
        { id: 'o-shot', name: 'Shot extra', priceDelta: 10, isAvailable: true },
        { id: 'o-crema', name: 'Crema batida', priceDelta: 7, isAvailable: true },
        { id: 'o-canela', name: 'Canela', priceDelta: 0, isAvailable: true },
      ] },
    ];
  }
  const line = (id, round, name, quantity, unitPrice, options = [], notes) => ({
    id, productId: id, productName: name, quantity, returnedQuantity: 0, unitPrice, totalPrice: r2(quantity * unitPrice),
    notes, taxRate: 16, isTaxExempt: false, unit: 0, satProductCode: '01010101', satUnitCode: 'H87', taxObject: 2, round,
    options: options.map((name, index) => ({ optionId: `${id}-o${index}`, groupName: 'Opciones', optionName: name, priceDelta: 0 })),
  });
  const tab = (id, number, table, tableId, openedMinutes, items, tickets, paid = 0) => {
    const total = r2(items.reduce((sum, item) => sum + item.totalPrice, 0));
    const tax = r2(total * 16 / 116);
    return {
      id, orderNumber: `ORD-${String(number).padStart(6, '0')}`, businessType: 1, orderType: 0, status: 2, canCancel: paid === 0,
      allowedTransitions: [], customerName: 'Público en general', tableNumber: table, pricesIncludeTax: true, channel: 0,
      taxBreakdown: [{ ratePercent: 16, isExempt: false, taxableBase: r2(total - tax), taxAmount: tax, total }],
      subTotal: r2(total - tax), tax, total, paidAmount: paid, balance: r2(total - paid), refundedAmount: 0, netPaidAmount: paid,
      createdAt: ago(openedMinutes), items, statusHistory: [], servicePieces: [], payments: [], refunds: [],
      isTab: true, isOpenTab: true, diningTableId: tableId, kitchenTickets: tickets,
    };
  };
  const t4 = tab('tab-4', 351, 'Mesa 4', 't-4', 52, [
    line('l1', 1, 'Chilaquiles verdes', 2, 85, [], 'Uno sin crema'),
    line('l2', 1, 'Latte', 2, 67, ['Grande']),
    line('l3', 2, 'Concha de vainilla', 2, 18),
    line('l4', 2, 'Café americano', 1, 35),
  ], [
    { id: 'k-4-1', round: 1, status: 3, sentAt: ago(52), readyAt: ago(40), servedAt: ago(38) },
    { id: 'k-4-2', round: 2, status: 2, sentAt: ago(12), readyAt: ago(2) },
  ]);
  const t7 = tab('tab-7', 352, 'Terraza 2', 't-7', 18, [
    line('l5', 1, 'Frappé moka', 3, 65),
    line('l6', 1, 'Torta de jamón', 1, 70, [], 'Sin chipotle'),
  ], [
    { id: 'k-7-1', round: 1, status: 1, sentAt: ago(18) },
  ]);
  const barra = tab('tab-barra', 353, 'Barra - Juan', null, 35, [line('l7', 1, 'Cold brew', 2, 55)], [
    { id: 'k-b-1', round: 1, status: 3, sentAt: ago(35), servedAt: ago(25) },
  ], 50);
  data.orders.push(t4, t7, barra);
  const table = (id, name, area, seats, sortOrder, openTab) => ({ id, name, area, seats, sortOrder, isActive: true, openTab });
  const summary = (order) => ({
    orderId: order.id, orderNumber: order.orderNumber, customerName: order.customerName, tableNumber: order.tableNumber,
    total: order.total, balance: order.balance, openedAt: order.createdAt, rounds: order.kitchenTickets.length,
    roundsInKitchen: order.kitchenTickets.filter((t) => t.status < 2).length, roundsReady: order.kitchenTickets.filter((t) => t.status === 2).length,
  });
  data.p3 = {
    room: {
      tables: [
        table('t-1', 'Mesa 1', 'Salón', 2, 1, null), table('t-2', 'Mesa 2', 'Salón', 4, 2, null), table('t-3', 'Mesa 3', 'Salón', 4, 3, null),
        table('t-4', 'Mesa 4', 'Salón', 6, 4, summary(t4)), table('t-5', 'Mesa 5', 'Salón', 4, 5, null),
        table('t-6', 'Terraza 1', 'Terraza', 2, 6, null), table('t-7', 'Terraza 2', 'Terraza', 4, 7, summary(t7)), table('t-8', 'Terraza 3', 'Terraza', 4, 8, null),
      ],
      tabsWithoutTable: [summary(barra)],
    },
    tickets: [
      { id: 'k-7-1', orderId: t7.id, orderNumber: t7.orderNumber, customerName: t7.customerName, tableNumber: 'Terraza 2', round: 1, status: 1, sentAt: ago(18), startedAt: ago(15), items: t7.items },
      { id: 'k-4-2', orderId: t4.id, orderNumber: t4.orderNumber, customerName: t4.customerName, tableNumber: 'Mesa 4', round: 2, status: 2, sentAt: ago(12), readyAt: ago(2), items: t4.items.filter((i) => i.round === 2) },
    ],
  };
  return data.p3;
}

export function phase3Routes(data, { ok, notFound }) {
  const p3 = () => seedPhase3(data);
  p3();
  return [
    ['GET', /^\/reports\/sales-by-branch$/, (m, q) => ok(salesByBranch(data, q))],
    ['GET', /^\/platform\/overview$/, () => ok(platform().overview)],
    ['GET', /^\/platform\/plans$/, () => ok(platform().plans)],
    ['GET', /^\/platform\/tenants$/, () => ok({ items: platform().tenants, pageNumber: 1, totalPages: 1, totalCount: platform().tenants.length, hasPreviousPage: false, hasNextPage: false })],
    ['GET', /^\/platform\/tenants\/[^/]+\/subscription$/, () => ok(platform().detail)],
    ['GET', /^\/auth\/my-tenant\/subscription$/, () => ok(platform().mine)],
    ['POST', /^\/branches\/[^/]+\/default$/, () => ok(null)],
    ['GET', /^\/tables$/, () => ok(p3().room)],
    ['PUT', /^\/auth\/my-tenant\/theme$/, (m, q, body) => {
      data.tenant.theme = body?.theme ?? 0;
      return ok({ theme: data.tenant.theme });
    }],
    ['PUT', /^\/auth\/my-tenant\/online-menu$/, (m, q, body) => {
      data.tenant.onlineMenuMode = body?.mode ?? 0;
      return ok({ mode: data.tenant.onlineMenuMode, slug: data.tenant.slug });
    }],
    ['GET', /^\/public\/menu\/([^/]+)$/, () => {
      const wanted = ['calientes', 'frias', 'tortas', 'pan'];
      const cats = data.categories.filter((c) => c.isActive !== false);
      return ok({
        businessName: data.tenant.name, slug: data.tenant.slug, logoUrl: null, contactPhone: '5512345678', currency: 'MXN',
        pricesIncludeTax: true, defaultTaxRate: 16, acceptsOrders: true, theme: data.tenant.theme,
        categories: cats.map((c) => ({
          id: c.id, name: c.name,
          products: data.products.filter((p) => p.categoryId === c.id && p.isAvailable && p._itemType === 'Product').map((p) => ({
            id: p.id, name: p.name, description: p.description, price: p.priceAmount, compareAtPrice: p.compareAtPrice ?? null,
            taxRate: p.taxRate, isTaxExempt: p.isTaxExempt, imageUrl: null, unit: p.unit, isFeatured: p.isFeatured,
            availableNow: !p.availableFromTime, allergens: p.allergens ?? null, optionGroups: p.optionGroups ?? [],
          })),
        })).filter((c) => c.products.length > 0),
      });
    }],
    ['POST', /^\/public\/menu\/([^/]+)\/orders$/, (m, q, body) => ok({
      orderNumber: 'ORD-000360', total: body?.expectedTotal ?? 0, confirmed: false, businessName: data.tenant.name, contactPhone: '5512345678',
    })],
    ['GET', /^\/kitchen\/tickets$/, () => ok(p3().tickets)],
    ['GET', /^\/orders\/([^/]+)\/cfdi-draft$/, (m, q) => {
      const order = data.orders.find((o) => o.id === m[1]);
      if (!order) return notFound('Orden');
      const receiver = q.get('rfc')
        ? { rfc: q.get('rfc'), name: (q.get('name') ?? '').toUpperCase(), fiscalRegime: q.get('fiscalRegime'), postalCode: q.get('postalCode'), cfdiUse: q.get('cfdiUse') }
        : { rfc: 'XAXX010101000', name: 'PUBLICO EN GENERAL', fiscalRegime: '616', postalCode: '06700', cfdiUse: 'S01' };
      const concepts = order.items.map((item, index) => {
        const base = Math.round((item.totalPrice / 1.16) * 100) / 100;
        return {
          orderItemId: item.id, productCode: index === 0 ? '50201706' : '01010101', unitCode: 'H87', description: item.productName,
          quantity: item.quantity, unitValue: Math.round((base / item.quantity) * 1e6) / 1e6, amount: base, taxObject: '02',
          transfer: { base, tax: '002', factorType: 'Tasa', rateOrQuota: 0.16, amount: Math.round((item.totalPrice - base) * 100) / 100 },
        };
      });
      const subTotal = Math.round(concepts.reduce((s, c) => s + c.amount, 0) * 100) / 100;
      const tax = Math.round(concepts.reduce((s, c) => s + c.transfer.amount, 0) * 100) / 100;
      return ok({
        orderId: order.id, orderNumber: order.orderNumber, version: '4.0', voucherType: 'I', currency: 'MXN', exportation: '01',
        issuedAtPostalCode: '06700', paymentForm: order.balance > 0 ? '99' : '01', paymentMethod: order.balance > 0 ? 'PPD' : 'PUE',
        issuer: { rfc: 'CLU201005AB1', name: 'CAFE LUNA', fiscalRegime: '626', postalCode: null, cfdiUse: null },
        receiver, isGeneralPublic: !q.get('rfc'), concepts,
        transfers: [{ base: subTotal, tax: '002', factorType: 'Tasa', rateOrQuota: 0.16, amount: tax }],
        subTotal, totalTransferred: tax, total: Math.round((subTotal + tax) * 100) / 100, orderTotal: order.total, difference: 0,
        isReadyToStamp: true, blockers: [],
        warnings: [
          ...(q.get('rfc') ? [] : ['Sin datos fiscales del cliente la venta va a público en general y se factura en la factura global del periodo.']),
          'Un concepto usa la clave genérica 01010101: asígnale su clave del SAT en el producto.',
        ],
      });
    }],
  ];
}

// Sales by branch: two branches with a weekday pattern; the last day is today (computed live).
function salesByBranch(data, q) {
  const to = q.get('to') || '2026-10-04';
  const from = q.get('from') || '2026-10-01';
  const branchId = q.get('branchId');
  const roma = data.locations[0];
  const coy = data.locations[1];
  const days = [];
  const totals = { roma: zero(), coy: zero() };
  for (let d = new Date(from + 'T12:00:00Z'); d <= new Date(to + 'T12:00:00Z'); d.setUTCDate(d.getUTCDate() + 1)) {
    const iso = d.toISOString().slice(0, 10);
    const weekday = d.getUTCDay();
    const factor = [1.35, 0.8, 0.85, 0.9, 1, 1.25, 1.5][weekday];
    const r = fig(Math.round(4200 * factor), Math.round(52 * factor));
    const c = fig(Math.round(2600 * factor), Math.round(37 * factor));
    add(totals.roma, r); add(totals.coy, c);
    const parts = branchId === coy.id ? [c] : branchId === roma.id ? [r] : [r, c];
    const net = parts.reduce((s, x) => s + x.netSales, 0);
    days.push({ date: iso, netSales: net, paymentsAmount: parts.reduce((s, x) => s + x.paymentsAmount, 0), refundsAmount: parts.reduce((s, x) => s + x.refundsAmount, 0), paidOrders: parts.reduce((s, x) => s + x.paidOrders, 0), ordersCreated: parts.reduce((s, x) => s + x.ordersCreated, 0) });
  }
  const branches = [
    { branchId: roma.id, branchName: roma.name, figures: finish(totals.roma) },
    { branchId: coy.id, branchName: coy.name, figures: finish(totals.coy) },
  ].filter((b) => !branchId || b.branchId === branchId);
  const all = zero();
  branches.forEach((b) => add(all, b.figures));
  return { from, to, totals: finish(all), branches, days, snapshotDays: Math.max(0, days.length - 1), liveDays: 1 };
}
function zero() { return { ordersCreated: 0, paidOrders: 0, paymentsCount: 0, paymentsAmount: 0, cashAmount: 0, cardAmount: 0, transferAmount: 0, walletAmount: 0, otherAmount: 0, platformAmount: 0, refundsCount: 0, refundsAmount: 0, netSales: 0, averageTicket: 0 }; }
function fig(amount, tickets) {
  const refunds = Math.round(amount * 0.015);
  return { ordersCreated: tickets + 2, paidOrders: tickets, paymentsCount: tickets + 3, paymentsAmount: amount, cashAmount: Math.round(amount * 0.45), cardAmount: Math.round(amount * 0.4), transferAmount: Math.round(amount * 0.1), walletAmount: 0, otherAmount: amount - Math.round(amount * 0.45) - Math.round(amount * 0.4) - Math.round(amount * 0.1), platformAmount: 0, refundsCount: refunds > 0 ? 1 : 0, refundsAmount: refunds, netSales: amount - refunds, averageTicket: 0 };
}
function add(target, x) { for (const k of Object.keys(target)) if (k !== 'averageTicket') target[k] += x[k]; }
function finish(x) { return { ...x, averageTicket: x.paidOrders > 0 ? Math.round((x.netSales / x.paidOrders) * 100) / 100 : 0 }; }

let platformData;
function platform() {
  if (platformData) return platformData;
  const plan = (id, code, name, price, modules, branches, users, trial, tenants, extra = {}) => ({
    id, code, name, description: null, monthlyPrice: price, currency: 'MXN', modules, maxBranches: branches, maxUsers: users,
    trialDays: trial, isDefaultForNewBusinesses: false, isActive: true, sortOrder: 0, tenantsCount: tenants, ...extra,
  });
  const plans = [
    plan('plan-emprende', 'EMPRENDE', 'Emprende', 249, 1 | 2 | 4, 1, 3, 14, 18, { isDefaultForNewBusinesses: true }),
    plan('plan-crece', 'CRECE', 'Crece', 549, 1 | 2 | 4 | 8 | 16 | 32, 3, 10, 14, 9),
    plan('plan-cadena', 'CADENA', 'Cadena', 1290, 1 | 2 | 4 | 8 | 16 | 32, 20, 60, 0, 2),
  ];
  const t = (id, name, slug, type, planIdx, status, trialEndsAt, paidThrough, branches, users, lastOrderAt, tenantStatus = 0, reason = null) => ({
    tenantId: id, name, slug, businessType: type, tenantStatus, contactEmail: slug + '@correo.mx', createdAt: '2026-06-01T15:00:00Z',
    planId: planIdx === null ? null : plans[planIdx].id, planName: planIdx === null ? null : plans[planIdx].name,
    subscriptionStatus: status, trialEndsAt, paidThrough, statusReason: reason, branchesCount: branches, usersCount: users, lastOrderAt,
  });
  const tenants = [
    t('t-1', 'Café La Esquina', 'cafe-la-esquina', 0, 1, 1, '2026-06-15T15:00:00Z', '2026-11-02T15:00:00Z', 2, 6, '2026-10-04T18:40:00Z'),
    t('t-2', 'Taquería El Güero', 'taqueria-el-guero', 0, 0, 2, '2026-09-28T15:00:00Z', null, 1, 3, '2026-10-03T23:10:00Z'),
    t('t-3', 'Estética Lupita', 'estetica-lupita', 2, 0, 0, '2026-10-12T15:00:00Z', null, 1, 2, '2026-10-04T16:00:00Z'),
    t('t-4', 'Ferretería Hidalgo', 'ferreteria-hidalgo', 1, 2, 3, '2026-05-01T15:00:00Z', '2026-09-01T15:00:00Z', 4, 14, '2026-09-08T19:00:00Z', 2, 'Sin pago después de los días de gracia.'),
    t('t-5', 'Panadería Doña Mary', 'panaderia-dona-mary', 0, null, null, null, null, 1, 2, '2026-10-04T13:30:00Z'),
  ];
  const overview = {
    totalTenants: 32, activeTenants: 29, tenantsWithoutAccess: 3, newTenantsLast30Days: 6, withoutPlan: 3, inTrial: 7, paying: 17,
    pastDue: 2, suspended: 2, cancelled: 1, monthlyRecurringRevenue: 12873, paymentsLast30Days: 14320, trialsEndingIn7Days: 2,
  };
  const detail = {
    hasPlan: true, plan: plans[0], status: 2, startedAt: '2026-09-14T15:00:00Z', trialEndsAt: '2026-09-28T15:00:00Z', paidThrough: null,
    suspendsAt: '2026-10-05T15:00:00Z', statusReason: null, suspendedByPlatform: false, branchesUsed: 1, usersUsed: 3, payments: [],
  };
  const mine = {
    hasPlan: true, plan: plans[1], status: 2, startedAt: '2026-06-01T15:00:00Z', trialEndsAt: '2026-06-15T15:00:00Z', paidThrough: '2026-10-02T15:00:00Z',
    suspendsAt: '2026-10-09T15:00:00Z', statusReason: null, suspendedByPlatform: false, branchesUsed: 2, usersUsed: 6,
    payments: [
      { id: 'pay-2', amount: 549, currency: 'MXN', months: 1, method: 0, reference: 'SPEI 99812', paidAt: '2026-09-02T17:00:00Z', periodStart: '2026-09-02T15:00:00Z', periodEnd: '2026-10-02T15:00:00Z', recordedBy: 'soporte' },
      { id: 'pay-1', amount: 1647, currency: 'MXN', months: 3, method: 1, reference: null, paidAt: '2026-06-02T17:00:00Z', periodStart: '2026-06-15T15:00:00Z', periodEnd: '2026-09-02T15:00:00Z', recordedBy: 'soporte' },
    ],
  };
  platformData = { plans, tenants, overview, detail, mine };
  return platformData;
}
