import { asOption, type SceneOptions } from '../fixtures';
import { at, uuid } from '../mocks/data.mjs';

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;

// OrderChannel, OrderStatus, PlatformCollectedBy, PlatformSettlementStatus and PlatformSettlementAdjustmentKind as the
// contract numbers them.
const RAPPI = 3;
const UBER_EATS = 4;
const DIDI_FOOD = 5;
const DELIVERED = 6;
const BY_THE_PLATFORM = 0;
const BY_THE_BUSINESS = 1;
const OPEN = 0;
const CLOSED = 1;
const VOIDED = 99;
const PROMOTION = 1;
const CLAIM = 2;
const FEE = 4;

export const UBER_OPEN_ID = uuid('settlement-uber-sep-21');
export const RAPPI_CLOSED_ID = uuid('settlement-rappi-sep-21');

/** Cents, as the server rounds them. */
const r2 = (amount: number) => Math.round(amount * 100) / 100;

/**
 * An order of a platform registered at a time of a past day: when the platform charged it, the 56.148 % of its total that
 * the platform deposits (30 % of commission, its IVA and the withholdings); when the courier paid the business, the
 * commission and its IVA the platform takes, in negative.
 */
function order(channel: number, folio: string, number: number, daysAgo: number, total: number, collectedBy = BY_THE_PLATFORM) {
  const registeredAt = at(14, 10 + (number % 40), -daysAgo);
  const net = collectedBy === BY_THE_PLATFORM ? r2(total * 0.56148) : r2(-total * 0.348);
  return {
    orderId: uuid(`platform-order-${channel}-${folio}`),
    orderNumber: `ORD-${String(number).padStart(6, '0')}`,
    externalOrderNumber: folio,
    registeredAt,
    registeredOn: registeredAt.slice(0, 10),
    inPeriod: true,
    status: DELIVERED,
    collectedBy,
    orderTotal: total,
    refundedAmount: 0,
    expectedNet: net,
    currentExpectedNet: net,
  };
}

type Order = ReturnType<typeof order>;

const sum = (amounts: number[]) => r2(amounts.reduce((total, amount) => total + amount, 0));

/** A settlement as the server answers it: its sums from its orders and adjustments. */
function settlement(fields: {
  id: string;
  channel: number;
  status: number;
  periodFrom: string;
  periodTo: string;
  depositedOn: string;
  depositedAmount: number;
  reference?: string;
  notes?: string;
  orders: Order[];
  adjustments: { kind: number; description?: string; amount: number }[];
  createdAt: string;
  closedAt?: string;
  voidedAt?: string;
  voidReason?: string;
}) {
  const ordersTotal = sum(fields.orders.map((each) => each.expectedNet));
  const adjustmentsTotal = sum(fields.adjustments.map((each) => each.amount));
  const expectedDeposit = r2(ordersTotal + adjustmentsTotal);
  return {
    id: fields.id,
    channel: fields.channel,
    status: fields.status,
    periodFrom: fields.periodFrom,
    periodTo: fields.periodTo,
    depositedOn: fields.depositedOn,
    depositedAmount: fields.depositedAmount,
    reference: fields.reference ?? null,
    notes: fields.notes ?? null,
    ordersTotal,
    adjustmentsTotal,
    expectedDeposit,
    difference: r2(fields.depositedAmount - expectedDeposit),
    orderCount: fields.orders.length,
    revision: fields.status === OPEN ? 2 : 3,
    createdAt: fields.createdAt,
    closedAt: fields.closedAt ?? null,
    voidedAt: fields.voidedAt ?? null,
    voidReason: fields.voidReason ?? null,
  };
}

// Uber Eats, the week of the 21st to the 27th of September, open: the deposit came $42.30 short of its orders and the two
// adjustments already written. A pending order of before waits to be included.
const UBER_ORDERS = [
  order(UBER_EATS, 'A7K2P', 201, 13, 420),
  order(UBER_EATS, 'C91XM', 214, 12, 610),
  order(UBER_EATS, 'D4QZ8', 226, 10, 350),
  order(UBER_EATS, 'F2M7T', 238, 9, 1000),
  order(UBER_EATS, 'H8N3R', 247, 8, 350, BY_THE_BUSINESS),
];
const UBER_EARLIER = order(UBER_EATS, 'J5W1B', 188, 15, 285);
const UBER_ADJUSTMENTS = [
  { kind: PROMOTION, description: 'Cupón 2x1 que paga el negocio', amount: -84 },
  { kind: FEE, description: 'Publicidad en la app', amount: -38 },
];
const UBER_OPEN = settlement({
  id: UBER_OPEN_ID,
  channel: UBER_EATS,
  status: OPEN,
  periodFrom: '2026-09-21',
  periodTo: '2026-09-27',
  depositedOn: '2026-09-30',
  depositedAmount: 1050.22,
  reference: 'Pago semanal 0930',
  orders: UBER_ORDERS,
  adjustments: UBER_ADJUSTMENTS,
  createdAt: at(10, 5, -4),
});

// Rappi, the same week, closed: its deposit is its orders minus a claim and a promotion.
const RAPPI_ORDERS = [
  order(RAPPI, '152101', 203, 13, 610),
  order(RAPPI, '152118', 209, 12, 420),
  order(RAPPI, '152133', 219, 11, 1000),
  order(RAPPI, '152140', 230, 10, 350),
  order(RAPPI, '152152', 236, 9, 610),
  order(RAPPI, '152167', 244, 8, 285),
];
const RAPPI_ADJUSTMENTS = [
  { kind: CLAIM, description: 'Faltó un refresco en el pedido 152133', amount: -45 },
  { kind: PROMOTION, description: 'Envío gratis que cubre el negocio', amount: -60 },
];
const RAPPI_DEPOSIT = r2(sum(RAPPI_ORDERS.map((each) => each.expectedNet)) - 105);
const RAPPI_CLOSED = settlement({
  id: RAPPI_CLOSED_ID,
  channel: RAPPI,
  status: CLOSED,
  periodFrom: '2026-09-21',
  periodTo: '2026-09-27',
  depositedOn: '2026-10-01',
  depositedAmount: RAPPI_DEPOSIT,
  reference: 'SPEI 4471',
  orders: RAPPI_ORDERS,
  adjustments: RAPPI_ADJUSTMENTS,
  createdAt: at(9, 40, -3),
  closedAt: at(10, 12, -3),
});

// DiDi Food: four orders pending since the 18th, one of them before the week (its statement brought it late) and one the
// courier paid the business.
const DIDI_PENDING = [
  order(DIDI_FOOD, '88213', 181, 16, 610),
  order(DIDI_FOOD, '88240', 212, 12, 420),
  order(DIDI_FOOD, '88257', 229, 10, 350, BY_THE_BUSINESS),
  order(DIDI_FOOD, '88262', 241, 8, 1000),
];

/** The week before: the settlements of the 14th to the 20th, one of them voided and registered again. */
function previousWeek() {
  const rappiOrders = [180, 182, 184, 186, 189].map((number, index) => order(RAPPI, `1520${number - 100}`, number, 19 - index, 350 + index * 65));
  const rappiTotal = sum(rappiOrders.map((each) => each.expectedNet));
  const didiOrders = [170, 173, 176, 178].map((number, index) => order(DIDI_FOOD, `880${number}`, number, 18 - index, 420 + index * 95));
  const didiTotal = sum(didiOrders.map((each) => each.expectedNet));
  return [
    settlement({
      id: uuid('settlement-didi-sep-14'),
      channel: DIDI_FOOD,
      status: CLOSED,
      periodFrom: '2026-09-14',
      periodTo: '2026-09-20',
      depositedOn: '2026-09-23',
      depositedAmount: didiTotal,
      orders: didiOrders,
      adjustments: [],
      createdAt: at(11, 0, -11),
      closedAt: at(11, 20, -11),
    }),
    settlement({
      id: uuid('settlement-rappi-sep-14-again'),
      channel: RAPPI,
      status: CLOSED,
      periodFrom: '2026-09-14',
      periodTo: '2026-09-20',
      depositedOn: '2026-09-24',
      depositedAmount: r2(rappiTotal - 30),
      reference: 'SPEI 4398',
      orders: rappiOrders,
      adjustments: [{ kind: CLAIM, description: 'Pedido 152084 incompleto', amount: -30 }],
      createdAt: at(12, 30, -10),
      closedAt: at(12, 45, -10),
    }),
    settlement({
      id: uuid('settlement-rappi-sep-14'),
      channel: RAPPI,
      status: VOIDED,
      periodFrom: '2026-09-14',
      periodTo: '2026-09-20',
      depositedOn: '2026-09-24',
      depositedAmount: 1500,
      orders: rappiOrders,
      adjustments: [],
      createdAt: at(12, 0, -10),
      voidedAt: at(12, 25, -10),
      voidReason: 'Capturé el depósito de otra semana',
    }),
  ];
}

const SETTLEMENTS = [RAPPI_CLOSED, UBER_OPEN, ...previousWeek()];

const RECEIVABLES = [
  { channel: RAPPI, isActive: true, pendingOrders: 4, pendingAmount: 1190.32, oldestPendingOn: '2026-09-28', openSettlementId: null, settledThrough: '2026-09-27' },
  { channel: UBER_EATS, isActive: true, pendingOrders: 1, pendingAmount: UBER_EARLIER.expectedNet, oldestPendingOn: UBER_EARLIER.registeredOn, openSettlementId: UBER_OPEN_ID, settledThrough: null },
  {
    channel: DIDI_FOOD,
    isActive: true,
    pendingOrders: DIDI_PENDING.length,
    pendingAmount: sum(DIDI_PENDING.map((each) => each.expectedNet)),
    oldestPendingOn: DIDI_PENDING[0].registeredOn,
    openSettlementId: null,
    settledThrough: '2026-09-20',
  },
];

/** Reportes › Liquidaciones: what each platform owes, the settlements, two of them in detail and DiDi's pending orders. */
const settlementRoutes: ExtraRoutes = (_data, helpers) => [
  ['GET', /^\/platform-settlements\/receivable$/, () => helpers.ok(RECEIVABLES)],
  ['GET', /^\/platform-settlements\/pending-orders$/, (_match, query) => helpers.ok({
    orders: query.get('channel') === String(DIDI_FOOD) ? DIDI_PENDING : [],
    truncated: false,
    openSettlementId: query.get('channel') === String(UBER_EATS) ? UBER_OPEN_ID : null,
  })],
  ['GET', /^\/platform-settlements\/([0-9a-f-]{36})$/, (match) => {
    if (match[1] === UBER_OPEN_ID) {
      return helpers.ok({ settlement: UBER_OPEN, orders: UBER_ORDERS, adjustments: UBER_ADJUSTMENTS, candidates: [UBER_EARLIER], candidatesTruncated: false });
    }
    if (match[1] === RAPPI_CLOSED_ID) {
      return helpers.ok({ settlement: RAPPI_CLOSED, orders: RAPPI_ORDERS, adjustments: RAPPI_ADJUSTMENTS, candidates: [], candidatesTruncated: false });
    }
    return { status: 404, json: { code: 'not_found', detail: 'No existe.' } };
  }],
  ['GET', /^\/platform-settlements$/, (_match, query) => helpers.ok(helpers.paginate(SETTLEMENTS, query, 20))],
];

export const LIQUIDACIONES_ROUTES = asOption(settlementRoutes);
