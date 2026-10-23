import { asOption, type DemoData, type SceneOptions } from '../fixtures';
import { NOW_ISO } from '../mocks/data.mjs';
import { publicRoutes } from './publico-cuenta-marco.mocks';
import { createSale } from './vender-pedidos.mocks';

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;
type PrepareData = NonNullable<SceneOptions['prepareData']>;

// Paying online with Mercado Pago (Fase 24I, ADR 0031): Configuración › Pagos en línea (`data.onlinePayments`, the
// OnlinePaymentSettingsDto), the store that offers it (`data.menuPaysOnline`), the page Mercado Pago sends the customer
// back to (`data.onlineCheckout`, the OnlineCheckoutStatusDto) and an order paid online with its refunds; since the Fase 24K
// (ADR 0044) also what happened in Mercado Pago: refunds made there, chargebacks and claims.

// OnlineCheckoutStatus, OnlinePaymentRefundStatus, OnlinePaymentRefundReason, OnlinePaymentDispute, OrderChannel and
// OrderPaymentMethod as the contract numbers them.
const AWAITING_PAYMENT = 0;
const PLACED = 1;
const EXPIRED = 2;
const NOT_PLACED = 3;
const REFUND_REQUESTED = 0;
const REFUND_COMPLETED = 1;
const REFUND_FAILED = 2;
const REFUND_DONE_IN_MERCADO_PAGO = 3;
const BY_BUSINESS = 3;
const IN_MERCADO_PAGO = 4;
const CHARGEBACK = 6;
const DISPUTE_OPEN = 1;
const DISPUTE_LOST = 2;
const ONLINE_CHANNEL = 2;
const ONLINE_PAYMENT = 6;
const CASH_PAYMENT = 0;

/** The checkout of the return page: its id travels in the URL Mercado Pago sends the customer back to. */
export const CHECKOUT_ID = '7c1e9a40-52b8-4f6d-9e3a-1b0d4c2f8a65';

/** The link of the order's receipt (Fase 24C): 43 characters, as the API gives it. */
const TRACKING_LINK = 'Mp4Lw8RtYc2NbVx6ZqKs0HfJd3Ga7Eu9Pi1Oo-_TnB';

const minutesFromNow = (minutes: number) => new Date(Date.parse(NOW_ISO) + minutes * 60_000).toISOString();

const NOT_CONNECTED = {
  available: true, sandbox: false, connected: false, accountId: null, connectedAt: null, keyExpiresAt: null,
  needsReconnect: false, acceptsPayments: false, menuTakesOrders: true, checkoutsInCourse: 0, refundsInCourse: 0, refundsRefused: 0,
  disputesOpen: 0, paymentsReviewedUntil: null,
};

/** Connected on September 28 and offered in the menu; its payments were reviewed in Mercado Pago 6 minutes ago. */
const CONNECTED = {
  ...NOT_CONNECTED, connected: true, accountId: 1928374650, connectedAt: '2026-09-28T17:40:00Z', keyExpiresAt: '2027-03-27T17:40:00Z',
  acceptsPayments: true, paymentsReviewedUntil: minutesFromNow(-6),
};

const settings = (value: Record<string, unknown>): PrepareData => (data) => {
  data.onlinePayments = value;
};

/** The platform has not set Mercado Pago up: nothing to connect. */
export const PAGOS_NO_DISPONIBLES = asOption(settings({ ...NOT_CONNECTED, available: false }));
export const PAGOS_SIN_CONECTAR = asOption(settings(NOT_CONNECTED));
export const PAGOS_CONECTADOS = asOption(settings(CONNECTED));
/**
 * The account stopped answering (its key could not be renewed) while a payment and a refund are in course, Mercado Pago
 * refused one refund and two payments are in dispute: reconnecting and disconnecting wait, the owner is told to give that
 * refund back and to answer the disputes in Mercado Pago.
 */
export const PAGOS_CON_AVISOS = asOption(settings({
  ...CONNECTED, needsReconnect: true, checkoutsInCourse: 1, refundsInCourse: 1, refundsRefused: 1, disputesOpen: 2,
}));

/** The store offers paying online; with `delivers` it also takes orders to Roma and Condesa. */
const storePaysOnline = (delivers: boolean): PrepareData => (data) => {
  data.menuPaysOnline = true;
  data.menuDelivery = delivers;
};

export const TIENDA_PAGA_EN_LINEA = asOption(storePaysOnline(false));
export const TIENDA_PAGA_EN_LINEA_A_DOMICILIO = asOption(storePaysOnline(true));

/** What the server says of the checkout after asking Mercado Pago, as the return page reads it. */
const checkout = (fields: Record<string, unknown>): PrepareData => (data) => {
  data.onlineCheckout = {
    status: AWAITING_PAYMENT, amount: 236, expiresAt: minutesFromNow(22), businessName: data.tenant.name,
    contactPhone: data.tenant.contactPhone, checkoutUrl: 'https://www.mercadopago.com.mx/checkout/v1/redirect?pref_id=visual',
    orderNumber: null, confirmed: false, linkedToAccount: false, trackingToken: null, failureMessage: null, refundStatus: null,
    ...fields,
  };
};

/** Approved and placed; this device kept the return key, so the tracking link comes. */
export const REGRESO_PEDIDO_RECIBIDO = asOption(checkout({
  status: PLACED, checkoutUrl: null, orderNumber: 'ORD-000361', trackingToken: TRACKING_LINK,
}));
/** Approved after the business stopped taking orders: not placed, and the money goes back. */
export const REGRESO_NO_SE_ENVIO = asOption(checkout({
  status: NOT_PLACED, checkoutUrl: null, refundStatus: REFUND_REQUESTED,
  failureMessage: 'Café La Esquina dejó de recibir pedidos en línea antes de que se aprobara tu pago.',
}));
/** The 30 minutes to pay ended without a payment. */
export const REGRESO_VENCIDO = asOption(checkout({ status: EXPIRED, checkoutUrl: null, expiresAt: minutesFromNow(-8) }));
/** Mercado Pago rejected the card (or the customer came back without paying): there is still time. */
export const REGRESO_SIN_PAGO = asOption(checkout({}));

/**
 * An order of the online menu Renata paid online ($236), with three refunds: one Mercado Pago is giving back, one it gave
 * back and one it refused (the business gives that one back from its account); and $20 handed back in cash.
 */
function withOrderPaidOnline(data: DemoData) {
  const product = (name: string) => data.products.find((candidate: { name: string }) => candidate.name === name).id;
  const order = createSale(data, {
    channel: ONLINE_CHANNEL, customerName: 'Renata Ibarra', customerPhone: '5531907742',
    items: [{ productId: product('Chilaquiles verdes'), quantity: 2 }, { productId: product('Café americano'), quantity: 2 }],
  });
  const at = (hour: number, minute: number) => `2026-10-04T${String(hour + 6).padStart(2, '0')}:${String(minute).padStart(2, '0')}:00.000Z`;
  const refund = (id: string, amount: number, onlineStatus: number, minute: number, onlineFailure: string | null = null) => ({
    id: `${order.id}-devolucion-${id}`, amount, method: ONLINE_PAYMENT, reason: 'Faltó un café americano', refundedAt: at(13, minute),
    refundedBy: 'mariana@cafelaesquina.mx', reference: null, restockItems: false, lines: [], onlineStatus, onlineFailure,
  });
  const refunds = [
    refund('1', 20, REFUND_COMPLETED, 2),
    refund('2', 20, REFUND_FAILED, 8, 'Mercado Pago no permite devolver este pago desde la aplicación.'),
    refund('3', 20, REFUND_REQUESTED, 15),
    {
      id: `${order.id}-devolucion-4`, amount: 20, method: CASH_PAYMENT, reason: 'Propina devuelta', refundedAt: at(13, 18),
      refundedBy: 'mariana@cafelaesquina.mx', reference: null, restockItems: false, lines: [],
    },
  ];
  const refunded = refunds.reduce((sum, item) => sum + item.amount, 0);
  Object.assign(order, {
    createdByBusiness: false, createdAt: at(12, 40), status: 6, allowedTransitions: [], canCancel: false,
    payments: [{
      id: `${order.id}-pago-1`, amount: order.total, method: ONLINE_PAYMENT, paidAt: at(12, 41), reference: '128734659012',
      notes: null, tip: 0, cashReceived: null, change: 0,
    }],
    paidAmount: order.total, balance: 0, refunds, refundedAmount: refunded, netPaidAmount: order.total - refunded,
  });
}

export const PEDIDO_PAGADO_EN_LINEA = asOption(withOrderPaidOnline);

/**
 * What happened in Mercado Pago to an order Renata paid online ($236), as TienditApp reflects it (Fase 24K): one refund
 * Mercado Pago refused that the business then gave back from its account, one made there from the business's account
 * and, with `lost`, the customer's bank took back the rest (a lost chargeback); without it, the chargeback is open.
 */
const withOrderDisputed = (lost: boolean) => (data: DemoData) => {
  const product = (name: string) => data.products.find((candidate: { name: string }) => candidate.name === name).id;
  const order = createSale(data, {
    channel: ONLINE_CHANNEL, customerName: 'Renata Ibarra', customerPhone: '5531907742',
    items: [{ productId: product('Chilaquiles verdes'), quantity: 2 }, { productId: product('Café americano'), quantity: 2 }],
  });
  // Mexico City time, before the day the scenes happen (October 4).
  const at = (date: string, hour: number, minute: number) =>
    `2026-${date}T${String(hour + 6).padStart(2, '0')}:${String(minute).padStart(2, '0')}:00.000Z`;
  const refund = (id: string, amount: number, fields: Record<string, unknown>) => ({
    id: `${order.id}-devolucion-${id}`, amount, method: ONLINE_PAYMENT, reference: null, restockItems: false, lines: [],
    onlineFailure: null, ...fields,
  });
  const refunds = [
    refund('1', 38, {
      reason: 'Faltó un café americano', refundedAt: at('09-20', 13, 2), refundedBy: 'mariana@cafelaesquina.mx',
      onlineStatus: REFUND_DONE_IN_MERCADO_PAGO, onlineReason: BY_BUSINESS,
      onlineFailure: 'Mercado Pago no permite devolver este pago desde la aplicación.',
    }),
    refund('2', 20, {
      reason: 'Devolución hecha desde la cuenta de Mercado Pago.', refundedAt: at('09-21', 10, 30), refundedBy: 'Mercado Pago',
      onlineStatus: REFUND_COMPLETED, onlineReason: IN_MERCADO_PAGO,
    }),
  ];
  if (lost) {
    refunds.push(refund('3', order.total - 58, {
      reason: 'Contracargo: el banco del cliente le regresó el pago.', refundedAt: at('10-02', 9, 15),
      refundedBy: 'Mercado Pago', onlineStatus: REFUND_COMPLETED, onlineReason: CHARGEBACK,
    }));
  }
  const refunded = refunds.reduce((sum, item) => sum + item.amount, 0);
  Object.assign(order, {
    createdByBusiness: false, createdAt: at('09-20', 12, 40), status: 6, allowedTransitions: [], canCancel: false,
    payments: [{
      id: `${order.id}-pago-1`, amount: order.total, method: ONLINE_PAYMENT, paidAt: at('09-20', 12, 41), reference: '128734659012',
      notes: null, tip: 0, cashReceived: null, change: 0,
    }],
    paidAmount: order.total, balance: 0, refunds, refundedAmount: refunded, netPaidAmount: order.total - refunded,
    onlineDispute: lost ? DISPUTE_LOST : DISPUTE_OPEN, onlineDisputeUpdatedAt: lost ? at('10-02', 9, 15) : at('09-26', 16, 5),
  });
};

export const PEDIDO_EN_DISPUTA = asOption(withOrderDisputed(false));
export const PEDIDO_CONTRACARGO_PERDIDO = asOption(withOrderDisputed(true));

/** The settings, the store's checkouts and the return page's answers, on top of the public menu's. */
const onlinePaymentRoutes: ExtraRoutes = (data, helpers) => [
  ['GET', /^\/online-payments$/, () => helpers.ok(data.onlinePayments)],
  ['POST', /^\/public\/menu\/([^/]+)\/checkouts\/([^/]+)\/confirm$/, () => helpers.ok(data.onlineCheckout)],
  ...publicRoutes(data, helpers),
];

export const PAGO_EN_LINEA_ROUTES = asOption(onlinePaymentRoutes);
