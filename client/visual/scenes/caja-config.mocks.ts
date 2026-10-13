import { asOption, type DemoData, type SceneOptions } from '../fixtures';

// BranchServiceMode as the contract numbers it.
const DELIVERY_ONLY = 1;
import { at } from '../mocks/data.mjs';

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;

// CashSessionStatus, CashTransactionType and OrderPaymentMethod as the contract numbers them.
const OPEN = 0;
const CLOSED = 1;
const SALE = 0;
const EXPENSE = 2;
const WITHDRAWAL = 3;
const DEPOSIT = 4;
const CASH = 0;
const CARD = 1;

const CARLOS = 'carlos.mendez@cafelaesquina.mx';
const DIEGO = 'diego.herrera@cafelaesquina.mx';

interface CountLine {
  denomination: number;
  quantity: number;
}

interface Movement {
  id: string;
  cashSessionId: string;
  transactionType: number;
  amount: number;
  occurredAt: string;
  paymentMethod?: number;
  inventoryLocationId: string;
  inventoryLocationName: string;
  reference?: string;
  notes?: string;
  createdBy: string;
}

interface Session {
  id: string;
  openedAt: string;
  openedBy: string;
  openingAmount: number;
  status: number;
  inflows: number;
  outflows: number;
  nonCashInflows: number;
  expectedBalance: number;
  isMine: boolean;
  branchId: string;
  branchName: string;
  inventoryLocationId: string;
  inventoryLocationName: string;
  cashCount: CountLine[];
  closedAt?: string;
  closedBy?: string;
  closingAmount?: number;
  expectedClosingAmount?: number;
  closingDifference?: number;
  closingNotes?: string;
  notes?: string;
}

interface Branch {
  id: string;
  name: string;
}

/** The cash a count adds up to, in cents (coins of $0.50 included). */
function countTotal(lines: CountLine[]) {
  return lines.reduce((sum, line) => sum + Math.round(line.denomination * 100) * line.quantity, 0) / 100;
}

/**
 * Yesterday's closed drawers and the one Carlos opened today at Coyoacán, beside the owner's own drawer of the demo
 * data. Each closed one tells a different ending: short with a note and the count by denomination, over by a little,
 * and exact with only the total typed.
 */
function shiftHistory(today: Session | null, roma: Branch, coyoacan: Branch) {
  const place = (branch: Branch) => ({
    branchId: branch.id, branchName: branch.name, inventoryLocationId: branch.id, inventoryLocationName: branch.name,
  });
  const closed = (id: string, openedBy: string, branch: Branch, openedAt: string, closedAt: string, amounts: {
    opening: number; inflows: number; outflows: number; nonCash: number; count: CountLine[]; counted?: number; notes?: string;
  }): Session => {
    const expected = amounts.opening + amounts.inflows - amounts.outflows;
    const counted = amounts.counted ?? countTotal(amounts.count);
    return {
      id, openedAt, openedBy, openingAmount: amounts.opening, status: CLOSED, inflows: amounts.inflows,
      outflows: amounts.outflows, nonCashInflows: amounts.nonCash, expectedBalance: expected, isMine: openedBy === today?.openedBy,
      ...place(branch), cashCount: amounts.count, closedAt, closedBy: openedBy, closingAmount: counted,
      expectedClosingAmount: expected, closingDifference: Math.round((counted - expected) * 100) / 100, closingNotes: amounts.notes,
    };
  };

  const sessions: Session[] = [
    {
      id: 'turno-coyoacan-hoy', openedAt: at(8, 0), openedBy: CARLOS, openingAmount: 800, status: OPEN, inflows: 1240.5,
      outflows: 150, nonCashInflows: 960, expectedBalance: 1890.5, isMine: false, ...place(coyoacan), cashCount: [],
      notes: 'Fondo de la barra',
    },
    ...(today ? [today] : []),
    closed('turno-roma-ayer-tarde', today?.openedBy ?? 'mariana.ortiz@cafelaesquina.mx', roma, at(15, 0, -1), at(21, 10, -1), {
      opening: 1500, inflows: 4870, outflows: 300, nonCash: 3920, notes: 'Propina en efectivo que no se registró',
      count: [{ denomination: 500, quantity: 10 }, { denomination: 200, quantity: 4 }, { denomination: 100, quantity: 2 },
        { denomination: 50, quantity: 1 }, { denomination: 20, quantity: 2 }],
    }),
    closed('turno-coyoacan-ayer', DIEGO, coyoacan, at(8, 0, -1), at(14, 30, -1), {
      opening: 800, inflows: 1985, outflows: 0, nonCash: 1210, count: [], counted: 2785,
    }),
    closed('turno-roma-ayer-manana', CARLOS, roma, at(7, 0, -1), at(15, 0, -1), {
      opening: 1500, inflows: 3260, outflows: 180, nonCash: 2140, notes: 'Faltaron $50: un cambio mal dado',
      count: [{ denomination: 500, quantity: 6 }, { denomination: 200, quantity: 5 }, { denomination: 100, quantity: 4 },
        { denomination: 50, quantity: 2 }, { denomination: 20, quantity: 1 }, { denomination: 10, quantity: 1 }],
    }),
  ];

  const movement = (session: Session, index: number, type: number, amount: number, occurredAt: string, extra: Partial<Movement> = {}): Movement => ({
    id: `${session.id}-mov-${index}`, cashSessionId: session.id, transactionType: type, amount, occurredAt,
    inventoryLocationId: session.inventoryLocationId, inventoryLocationName: session.inventoryLocationName, createdBy: session.openedBy, ...extra,
  });
  const byId = Object.fromEntries(sessions.map((session) => [session.id, session]));
  const morning = byId['turno-roma-ayer-manana'];
  const coyToday = byId['turno-coyoacan-hoy'];
  const movements: Record<string, Movement[]> = {
    [morning.id]: [
      movement(morning, 1, SALE, 232, at(14, 40, -1), { paymentMethod: CARD, reference: 'ORD-000316' }),
      movement(morning, 2, SALE, 185.6, at(12, 15, -1), { paymentMethod: CASH, reference: 'ORD-000309' }),
      movement(morning, 3, EXPENSE, -180, at(10, 30, -1), { reference: 'Hielo (6 bolsas)', notes: 'Pago a proveedor de hielo' }),
      movement(morning, 4, SALE, 98.6, at(8, 20, -1), { paymentMethod: CASH, reference: 'ORD-000302' }),
      movement(morning, 5, DEPOSIT, 300, at(7, 10, -1), { reference: 'Cambio', notes: 'Monedas para cambio' }),
    ],
    [coyToday.id]: [
      movement(coyToday, 1, WITHDRAWAL, -150, at(12, 0), { reference: 'Pago de pan', notes: 'Panificadora La Espiga' }),
      movement(coyToday, 2, SALE, 81.2, at(9, 5), { paymentMethod: CASH, reference: 'ORD-000329' }),
    ],
  };
  return { sessions, movements };
}

/**
 * Caja: the shift history and the movements of each drawer. The shared routes answer one list for every drawer (and a
 * history from another business); these answer by drawer, so a closed shift shows its own movements and its cut.
 */
const cashRoutes: ExtraRoutes = (data, { ok, paginate }) => {
  const today: Session | null = data.cashSession;
  const [roma, coyoacan] = data.locations as Branch[];
  const { sessions, movements } = shiftHistory(today, roma, coyoacan);
  const todayId = today?.id;
  return [
    ['GET', /^\/delivery-zones$/, () => ok(data.deliveryZones ?? deliveryZones())],
    ['GET', /^\/cash\/sessions$/, (_match, query) => {
      let rows = sessions;
      const from = query.get('fromDate');
      const to = query.get('toDate');
      if (from) rows = rows.filter((session) => session.openedAt >= from);
      if (to) rows = rows.filter((session) => session.openedAt <= to);
      if (query.get('branchId')) rows = rows.filter((session) => session.branchId === query.get('branchId'));
      if (query.get('onlyWithDifference') === 'true') rows = rows.filter((session) => !!session.closingDifference);
      return ok(paginate(rows, query, 20));
    }],
    ['GET', /^\/cash\/transactions$/, (_match, query) => {
      const sessionId = query.get('cashSessionId');
      // Today's drawer (also once it is closed in the scene) moves what the demo data recorded today.
      let rows: Movement[] = sessionId && sessionId !== todayId ? movements[sessionId] ?? [] : data.cashTx;
      const search = (query.get('search') ?? '').toLowerCase();
      if (search) {
        rows = rows.filter((row) => [row.reference, row.notes, row.createdBy].some((value) => (value ?? '').toLowerCase().includes(search)));
      }
      return ok(paginate(rows, query, 10));
    }],
    // A platform admin belongs to no business: the platform console is theirs, not the business's.
    ['GET', /^\/me\/access$/, () => (data.profile.role === SUPER_ADMIN_PROFILE.role
      ? ok({ userId: data.USER_ID, role: 'SUPER_ADMIN', tenantRole: null, tenantId: data.TENANT_ID, isMember: false, isTenantManager: true,
        isPlatformAdmin: true, permissions: [], capabilities: data.tenant.capabilities })
      : undefined)],
  ];
};

/**
 * The fields the contract requires that the demo's drawer and day cut leave out (CashSessionDto.isMine, cashCount and
 * its branch; the tips and cash-only figures of DailyCashCutDto). Without them the drawer reads as somebody else's.
 * The day had tips: $120.00, $70.00 of them in cash, which the cash-only net includes.
 */
function completeCashData(data: DemoData) {
  const [roma] = data.locations as Branch[];
  if (data.cashSession) {
    Object.assign(data.cashSession, {
      isMine: true, cashCount: [], branchId: roma.id, branchName: roma.name, inventoryLocationId: roma.id, inventoryLocationName: roma.name,
    });
  }
  const cut = data.dailyCut;
  Object.assign(cut, {
    tipsTotal: 120,
    cashTipsTotal: 70,
    cashManualIncomeTotal: cut.manualIncomeTotal,
    cashManualExpenseTotal: cut.manualExpenseTotal,
    netCashOnlyFlow: Math.round((cut.netCashOnlyFlow + 70) * 100) / 100,
  });
}

/** Nobody has opened their drawer yet: Mi caja asks for the opening float. */
function withoutDrawer(data: DemoData) {
  completeCashData(data);
  data.cashSession = null;
}

/**
 * Mi negocio as a business that has filled it in: its hours (two openings on Saturday, closed on Sunday) and its logo.
 * The logo is the demo picture (/uploads would answer 404).
 */
function businessProfile(data: DemoData) {
  completeCashData(data);
  const weekday = [1, 2, 3, 4, 5].map((day) => ({ day, opens: '07:00', closes: '21:00' }));
  data.tenant.openingHours = [...weekday, { day: 6, opens: '08:00', closes: '14:00' }, { day: 6, opens: '16:00', closes: '22:00' }];
  data.tenant.timeZone = 'America/Mexico_City';
  const logo = '/visual-img/logo.svg';
  data.tenant.logoUrl = logo;
  data.tenant.logoImages = { original: logo, large: logo, medium: logo, small: logo, thumbnail: logo };
}

/** Mi negocio with the online menu taking orders: its link, its QR and one QR per table. */
function businessWithOnlineMenu(data: DemoData) {
  businessProfile(data);
  data.tenant.onlineMenuMode = 2;
}

// OrderChannel, PlatformCommissionBase, PlatformCollectedBy and OrderStatus as the contract numbers them.
const RAPPI = 3;
const UBER_EATS = 4;
const DIDI_FOOD = 5;
const TOTAL_WITH_TAX = 0;
const SUBTOTAL_WITHOUT_TAX = 1;
const BY_THE_PLATFORM = 0;
const BY_THE_BUSINESS = 1;
const DELIVERED = 6;
const CANCELLED = 99;

const MARIANA = 'mariana.ortiz@cafelaesquina.mx';

/**
 * Plataformas de reparto as the API answers it: Rappi takes orders (30 % on the total), Uber Eats was set up and turned
 * off (27 % on the subtotal) and DiDi Food was never saved. Each example is the server's, on $1,000 with IVA included.
 */
function deliveryPlatforms() {
  const fiscal = { commissionVatPercent: 16, isrWithholdingPercent: 2.5, vatWithholdingPercent: 50 };
  return {
    platforms: [
      {
        channel: RAPPI, isConfigured: true, isActive: true, commissionPercent: 30, commissionBase: TOTAL_WITH_TAX, ...fiscal, revision: 2,
        changedAt: at(10, 5, -3), changedBy: MARIANA,
        example: { orderTotal: 1000, orderVatPercent: 16, commissionAndTax: 348, withheld: 90.52, expectedSettlement: 561.48 },
      },
      {
        channel: UBER_EATS, isConfigured: true, isActive: false, commissionPercent: 27, commissionBase: SUBTOTAL_WITHOUT_TAX, ...fiscal,
        revision: 3, changedAt: at(18, 40, -1), changedBy: MARIANA,
        example: { orderTotal: 1000, orderVatPercent: 16, commissionAndTax: 270, withheld: 90.52, expectedSettlement: 639.48 },
      },
      {
        channel: DIDI_FOOD, isConfigured: false, isActive: false, commissionPercent: null, commissionBase: TOTAL_WITH_TAX, ...fiscal,
        revision: 0, changedAt: null, changedBy: null, example: null,
      },
    ],
  };
}

interface ChannelRow {
  channel: number | null;
  isDeliveryPlatform: boolean;
  paidOrders: number;
  paymentsAmount: number;
  refundsAmount: number;
  netSales: number;
  platformCollected: number;
  businessCollected: number;
  commission: number;
  commissionTax: number;
  isrWithheld: number;
  vatWithheld: number;
  expectedSettlement: number;
}

/**
 * Sales by channel of the month so far: the counter, WhatsApp and the online menu, and the platforms with what they
 * charged, their commission and what they should deposit. One Rappi order was voided and one was paid by its courier.
 */
function salesByChannel(from: string, to: string) {
  const row = (patch: Partial<ChannelRow>): ChannelRow => ({
    channel: null, isDeliveryPlatform: false, paidOrders: 0, paymentsAmount: 0, refundsAmount: 0, netSales: 0, platformCollected: 0,
    businessCollected: 0, commission: 0, commissionTax: 0, isrWithheld: 0, vatWithheld: 0, expectedSettlement: 0, ...patch,
  });
  const channels = [
    row({ channel: 0, paidOrders: 186, paymentsAmount: 31420, refundsAmount: 470, netSales: 30950 }),
    row({ channel: 1, paidOrders: 21, paymentsAmount: 4380, netSales: 4380 }),
    row({ channel: 2, paidOrders: 12, paymentsAmount: 2260, netSales: 2260 }),
    row({
      channel: RAPPI, isDeliveryPlatform: true, paidOrders: 9, paymentsAmount: 3500, refundsAmount: 350, netSales: 3150,
      platformCollected: 2800, businessCollected: 350, commission: 945, commissionTax: 151.2, isrWithheld: 60.34, vatWithheld: 193.1,
      expectedSettlement: 1450.36,
    }),
    row({
      channel: UBER_EATS, isDeliveryPlatform: true, paidOrders: 4, paymentsAmount: 1240, netSales: 1240, platformCollected: 1240,
      commission: 288.62, commissionTax: 46.18, isrWithheld: 26.72, vatWithheld: 85.52, expectedSettlement: 792.96,
    }),
  ];
  // The sum of the rows, in cents, as the server adds them.
  const sum = (key: Exclude<keyof ChannelRow, 'channel' | 'isDeliveryPlatform'>) =>
    channels.reduce((total, item) => total + Math.round(item[key] * 100), 0) / 100;
  const totals = row({
    paidOrders: sum('paidOrders'), paymentsAmount: sum('paymentsAmount'), refundsAmount: sum('refundsAmount'), netSales: sum('netSales'),
    platformCollected: sum('platformCollected'), businessCollected: sum('businessCollected'), commission: sum('commission'),
    commissionTax: sum('commissionTax'), isrWithheld: sum('isrWithheld'), vatWithheld: sum('vatWithheld'),
    expectedSettlement: sum('expectedSettlement'),
  });
  return { from, to, channels, totals };
}

/** The Rappi orders of the period, the newest first, with what each one copied when it was registered. */
function rappiOrders() {
  const order = (folio: string, number: number, registeredAt: string, total: number, amounts: {
    commissionAndTax: number; withheld: number; expectedSettlement: number;
  }, extra: { status?: number; collectedBy?: number; refundedAmount?: number } = {}) => ({
    orderId: `rappi-${folio}`, orderNumber: `ORD-${String(number).padStart(6, '0')}`, externalOrderNumber: folio, registeredAt,
    status: extra.status ?? DELIVERED, collectedBy: extra.collectedBy ?? BY_THE_PLATFORM, orderTotal: total, ...amounts,
    refundedAmount: extra.refundedAmount ?? 0,
  });
  return [
    order('152412', 341, at(12, 10), 420, { commissionAndTax: 146.16, withheld: 38.02, expectedSettlement: 235.82 }),
    order('152398', 329, at(20, 40, -1), 350, { commissionAndTax: 121.8, withheld: 31.68, expectedSettlement: 196.52 },
      { status: CANCELLED, refundedAmount: 350 }),
    order('152371', 318, at(14, 5, -1), 350, { commissionAndTax: 121.8, withheld: 0, expectedSettlement: -121.8 },
      { collectedBy: BY_THE_BUSINESS }),
    order('152355', 307, at(13, 30, -2), 610, { commissionAndTax: 212.28, withheld: 55.22, expectedSettlement: 342.5 }),
    order('152340', 296, at(19, 15, -3), 1000, { commissionAndTax: 348, withheld: 90.52, expectedSettlement: 561.48 }),
  ];
}

/** Mi negocio with the delivery platforms set up, beside Caja's routes. */
const platformsRoutes: ExtraRoutes = (data, helpers) => [
  ['GET', /^\/delivery-platforms$/, () => helpers.ok(deliveryPlatforms())],
  ...cashRoutes(data, helpers),
];

/** Reportes with sales by channel and the list of the Rappi orders, beside Caja's routes. */
const salesByChannelRoutes: ExtraRoutes = (data, helpers) => [
  ['GET', /^\/reports\/sales-by-channel$/, (_match, query) =>
    helpers.ok(salesByChannel(query.get('from') || '2026-10-01', query.get('to') || '2026-10-04'))],
  ['GET', /^\/reports\/delivery-platform-orders$/, (_match, query) =>
    helpers.ok(helpers.paginate(query.get('channel') === String(RAPPI) ? rappiOrders() : [], query, 20))],
  ...cashRoutes(data, helpers),
];

/**
 * The day's cut of a day with Rappi orders: $1,380.00 Rappi charged in its app (three orders, in "Cobrado" and in its
 * branch, in no cashier's row) and one voided order of $350.00 it gave back. The drawers do not change.
 */
function withPlatformInTheCut(data: DemoData) {
  completeCashData(data);
  const cut = data.dailyCut;
  const charged = 1380;
  const add = (amount: number) => Math.round((amount + charged) * 100) / 100;
  Object.assign(cut, {
    salesPaymentsTotal: add(cut.salesPaymentsTotal),
    salesPaymentsCount: cut.salesPaymentsCount + 3,
    platformTotal: charged,
    platformPaymentsCount: 3,
    platformRefundsTotal: 350,
  });
  for (const place of [...cut.byLocation, ...cut.byBranch]) {
    Object.assign(place, { paymentsCount: place.paymentsCount + 3, totalAmount: add(place.totalAmount) });
  }
}

/**
 * The delivery zones of Café La Esquina (Configuración › Zonas de entrega): Roma and Condesa with a minimum and free
 * delivery from $400, Juárez and Cuauhtémoc without a minimum, and Del Valle turned off, with more postal codes than a
 * row shows.
 */
function deliveryZones() {
  return [
    {
      id: 'zone-roma', name: 'Roma y Condesa', fee: 35, minimumOrder: 150, freeDeliveryFrom: 400, isActive: true, revision: 2,
      postalCodes: ['06100', '06140', '06700', '06760'],
    },
    {
      id: 'zone-juarez', name: 'Juárez y Cuauhtémoc', fee: 45, minimumOrder: 0, freeDeliveryFrom: null, isActive: true, revision: 1,
      postalCodes: ['06500', '06600'],
    },
    {
      id: 'zone-del-valle', name: 'Del Valle y Narvarte', fee: 60, minimumOrder: 250, freeDeliveryFrom: null, isActive: false, revision: 3,
      postalCodes: ['03020', '03023', '03100', '03103', '03104', '03300'],
    },
  ];
}

export const CAJA_ROUTES = asOption(cashRoutes);

/** An order Paquetenvia collects (CourierOrderDto). */
const courierOrder = (orderId: string, orderNumber: string, customerName: string, folio: string, dispatchedAt: string, amount: number,
  status: number, paysWith: number | null = null) => ({
  orderId, orderNumber, customerName, courierFolio: folio, dispatchedAt, status, cashToCollect: amount, paysWith, balance: amount,
});

/**
 * Fase 22D, Caja › Repartidores: Paquetenvia owes two orders it delivered today ($395), carries one on its way ($225, paid
 * with $500) and was settled yesterday and the day before. Kept in the data, so a settlement changes what is owed.
 */
function withCourierCash(data: DemoData) {
  completeCashData(data);
  data.courierOwed = [
    courierOrder('pq-1', 'ORD-000331', 'Andrés Morales', 'PQ48197', at(12, 5), 215, 6),
    courierOrder('pq-2', 'ORD-000336', 'Fernanda Ruiz', 'PQ48205', at(12, 40), 180, 6),
  ];
  data.courierOnTheWay = [courierOrder('pq-3', 'ORD-000341', 'Valeria Torres', 'PQ48219', at(13, 12), 225, 3, 500)];
  data.courierSettlements = [
    {
      id: 'liq-2', courier: 1, settledAt: at(22, 10, -1), settledBy: 'lucia.navarro@cafelaesquina.mx', method: 0,
      handedAmount: 485, ordersTotal: 530, adjustmentsTotal: -45, orderCount: 3, reference: null, notes: null,
    },
    {
      id: 'liq-1', courier: 1, settledAt: at(21, 40, -2), settledBy: 'mariana.ortiz@cafelaesquina.mx', method: 2,
      handedAmount: 312, ordersTotal: 342, adjustmentsTotal: -30, orderCount: 2, reference: 'SPEI 88123', notes: null,
    },
  ];
  data.courierDetails = {
    'liq-2': {
      settlement: data.courierSettlements[0],
      orders: [
        { orderId: 'o-1', orderNumber: 'ORD-000318', customerName: 'Sofía Ramírez', courierFolio: 'PQ48160', amount: 210 },
        { orderId: 'o-2', orderNumber: 'ORD-000321', customerName: 'Diego Hernández', courierFolio: 'PQ48168', amount: 175 },
        { orderId: 'o-3', orderNumber: 'ORD-000325', customerName: 'Luis Ortega', courierFolio: 'PQ48177', amount: 145 },
      ],
      adjustments: [
        { position: 1, kind: 1, description: null, amount: -35 },
        { position: 2, kind: 2, description: '3 cobros', amount: -10 },
      ],
    },
  };
  refreshReceivable(data);
}

/** What Paquetenvia owes and carries, from the orders in the data. */
function refreshReceivable(data: DemoData) {
  const sum = (orders: Array<{ balance: number }>) => orders.reduce((total, order) => total + order.balance, 0);
  data.courierReceivables = [{
    courier: 1,
    owedOrders: data.courierOwed.length,
    owedAmount: sum(data.courierOwed),
    oldestDispatchedAt: data.courierOwed[0]?.dispatchedAt ?? null,
    onTheWayOrders: data.courierOnTheWay.length,
    onTheWayAmount: sum(data.courierOnTheWay),
    lastSettledAt: data.courierSettlements[0]?.settledAt ?? null,
  }];
}

/** Caja with what the couriers owe: the shared receivable reads `data.courierReceivables`. */
const courierRoutes: ExtraRoutes = (data, helpers) => [
  ['GET', /^\/courier-settlements\/pending-orders$/, () => helpers.ok({ owed: data.courierOwed, onTheWay: data.courierOnTheWay, truncated: false })],
  ['GET', /^\/courier-settlements$/, (_match, query) => helpers.ok(helpers.paginate(data.courierSettlements, query, 10))],
  // A settlement by its id, as the API's {id:guid} (never «receivable» nor «pending-orders», answered above and in the shared mocks).
  ['GET', /^\/courier-settlements\/(liq-[^/]+)$/, (match) => helpers.ok(data.courierDetails[match[1]])],
  ['POST', /^\/courier-settlements$/, (_match, _query, body: { orderIds: string[]; handedAmount: number; method: number;
    adjustments: Array<{ kind: number; description: string | null; amount: number }>; reference: string | null; notes: string | null }) => {
    const paid = data.courierOwed.filter((order: { orderId: string }) => body.orderIds.includes(order.orderId));
    const settlement = {
      id: 'liq-3', courier: 1, settledAt: '2026-10-04T19:25:00.000Z', settledBy: 'mariana.ortiz@cafelaesquina.mx', method: body.method,
      handedAmount: body.handedAmount, ordersTotal: paid.reduce((total: number, order: { balance: number }) => total + order.balance, 0),
      adjustmentsTotal: body.adjustments.reduce((total, adjustment) => total + adjustment.amount, 0), orderCount: paid.length,
      reference: body.reference, notes: body.notes,
    };
    const detail = {
      settlement,
      orders: paid.map((order: { orderId: string; orderNumber: string; customerName: string; courierFolio: string; balance: number }) => ({
        orderId: order.orderId, orderNumber: order.orderNumber, customerName: order.customerName, courierFolio: order.courierFolio, amount: order.balance,
      })),
      adjustments: body.adjustments.map((adjustment, index) => ({ position: index + 1, ...adjustment })),
    };
    data.courierOwed = data.courierOwed.filter((order: { orderId: string }) => !body.orderIds.includes(order.orderId));
    data.courierSettlements.unshift(settlement);
    data.courierDetails[settlement.id] = detail;
    refreshReceivable(data);
    return { status: 201, json: detail };
  }],
  ...cashRoutes(data, helpers),
];

export const REPARTIDORES_ROUTES = asOption(courierRoutes);
export const REPARTIDORES_DATA = asOption(withCourierCash);
/** The same, for whoever has not opened a drawer of their own today. */
export const REPARTIDORES_SIN_CAJA_DATA = asOption((data: DemoData) => {
  withCourierCash(data);
  withoutDrawer(data);
});
/** A business that has not set up any delivery zone yet. */
export const SIN_ZONAS_DATA = asOption((data: DemoData) => {
  completeCashData(data);
  data.deliveryZones = [];
});
export const CAJA_DATA = asOption(completeCashData);
/** The drawers of the file, with the main branch delivering only: the address of a kitchen without premises stays private. */
export const PRINCIPAL_SOLO_A_DOMICILIO_DATA = asOption((data: DemoData) => {
  completeCashData(data);
  data.locations.find((location: { isDefault: boolean }) => location.isDefault).serviceMode = DELIVERY_ONLY;
});
export const SIN_CAJA_DATA = asOption(withoutDrawer);
export const NEGOCIO_DATA = asOption(businessProfile);
export const MENU_EN_LINEA_DATA = asOption(businessWithOnlineMenu);
export const PLATAFORMAS_ROUTES = asOption(platformsRoutes);
export const VENTAS_POR_CANAL_ROUTES = asOption(salesByChannelRoutes);
export const CORTE_CON_PLATAFORMA_DATA = asOption(withPlatformInTheCut);

/** The platform admin (TienditApp's own team): no role in the business, the platform console. */
export const SUPER_ADMIN_PROFILE = {
  firstName: 'Equipo',
  lastName: 'TienditApp',
  fullName: 'Equipo TienditApp',
  email: 'soporte@tienditapp.mx',
  role: 'SuperAdmin',
  tenantRole: null,
  permissions: [],
};

/** A cashier (the Cajero preset): sells, charges and runs a drawer, but does not configure the business. */
export const CASHIER_PROFILE = {
  firstName: 'Carlos',
  lastName: 'Méndez',
  fullName: 'Carlos Méndez',
  email: CARLOS,
  role: 'Staff',
  tenantRole: 'Staff',
  permissions: ['ManageOrders', 'ChargeOrders', 'ManageCash', 'ManageCustomers', 'ManageWhatsApp', 'ManageAppointments'],
};
