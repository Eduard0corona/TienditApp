import { asOption, type DemoData, type SceneOptions } from '../fixtures';
import { NOW_ISO, uuid } from '../mocks/data.mjs';

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;
type PrepareData = NonNullable<SceneOptions['prepareData']>;

// Invoicing (Fase 28A, ADR 0045): Configuración › Facturación (`data.invoicing`, the InvoicingSettingsDto), the invoice of
// an order in Pedidos (`data.orderInvoicing`, the OrderInvoicingDto of each order by its id) and the fiscal data of a
// customer in Clientes. The order is ORD-000343, the office of Grupo Altavista: delivered and paid by transfer, or (Fase
// 28C, ADR 0047) invoiced in installments, with the payment complements of its payments.

// OrderInvoiceStatus and CfdiCancellationMotive as the contract numbers them.
const STAMPED = 1;
const REFUSED = 2;
const CANCELLING = 3;
const CANCELLED = 4;
const ERRORS_WITHOUT_REPLACEMENT = 2;
const NOT_CARRIED_OUT = 3;

export const ORDER_NUMBER = 'ORD-000343';
export const CUSTOMER_NAME = 'Oficinas Grupo Altavista';

/** As its constancia says it: the SAT checks it when the invoice is stamped. */
const ALTAVISTA = { rfc: 'GAL1806043P5', name: 'GRUPO ALTAVISTA', postalCode: '11560', fiscalRegime: '601', cfdiUse: 'G03' };

const minutesFromNow = (minutes: number) => new Date(Date.parse(NOW_ISO) + minutes * 60_000).toISOString();

/** The CSD of Café Luna, valid for four years since February. */
const CERTIFICATE = {
  number: '30001000000500003416', rfc: 'CLU201005AB1', validFrom: '2026-02-11T18:20:00Z', validTo: '2030-02-11T18:20:00Z',
  uploadedAt: '2026-10-01T16:12:00Z', usable: true, expiresSoon: false, expired: false, otherRfc: false,
};

/** The business's fiscal data in Mi negocio, its CSD and its serie: ready to invoice. */
const READY = {
  available: true, sandbox: false, serie: 'F', paymentSerie: 'P', rfc: 'CLU201005AB1', legalName: 'CAFE LUNA', fiscalRegime: '626',
  postalCode: '06700', missingData: [] as string[], certificate: CERTIFICATE, ready: true,
};

const settings = (value: Record<string, unknown>): PrepareData => (data) => {
  data.invoicing = value;
};

export const FACTURACION_LISTA = asOption(settings(READY));
/** Without the CSD yet and with the regime missing in Mi negocio. */
export const FACTURACION_SIN_CSD = asOption(settings({
  ...READY, fiscalRegime: null, missingData: ['El régimen fiscal del negocio'], certificate: undefined, ready: false,
}));
/** In the PAC's test environment, with a CSD that expires in three weeks. */
export const FACTURACION_PRUEBAS_POR_VENCER = asOption(settings({
  ...READY, sandbox: true, certificate: { ...CERTIFICATE, validTo: minutesFromNow(21 * 24 * 60), expiresSoon: true },
}));
/** The platform has not set its PAC up. */
export const FACTURACION_NO_DISPONIBLE = asOption(settings({ ...READY, available: false, certificate: undefined, ready: false }));

const r2 = (amount: number) => Math.round(amount * 100) / 100;

/** An invoice of the order, for Grupo Altavista and for its total. */
function invoice(data: DemoData, folio: number, fields: Record<string, unknown>) {
  const order = data.orders.find((o: { orderNumber: string }) => o.orderNumber === ORDER_NUMBER);
  const subTotal = r2(order.total / 1.16);
  return {
    id: uuid(`invoice-${folio}`), orderId: order.id, number: `F-${folio}`, status: STAMPED, receiver: ALTAVISTA,
    paymentForm: '03', paymentMethod: 'PUE', subTotal, taxTotal: r2(order.total - subTotal), total: order.total,
    requestedAt: minutesFromNow(-50), uuid: null, stampedAt: null, hasXml: false, hasPdf: false, failureMessage: null,
    refusedAt: null, cancellationRequestedAt: null, cancelledAt: null,
    ...fields,
  };
}

/** What the order's invoicing says; the draft comes from the CFDI preview's route. */
const orderInvoicing = (build: (data: DemoData) => Record<string, unknown>): PrepareData => (data) => {
  const order = data.orders.find((o: { orderNumber: string }) => o.orderNumber === ORDER_NUMBER);
  data.orderInvoicing = {
    [order.id]: {
      available: true, sandbox: false, canInvoice: true, problem: null, proposedReceiver: ALTAVISTA,
      customerId: uuid('cust:altavista'), customerName: CUSTOMER_NAME, invoices: [], payments: [],
      ...build(data),
    },
  };
};

/** Never invoiced: the fiscal data of Grupo Altavista is proposed and the invoice can be stamped. */
export const PEDIDO_POR_FACTURAR = asOption(orderInvoicing(() => ({})));
/**
 * Invoiced three times: F-1 refused by the PAC for the postal code, F-2 cancelled because the use was wrong, and F-3
 * stamped with its folio fiscal and its files. With a live invoice nothing else is stamped.
 */
export const PEDIDO_FACTURADO = asOption(orderInvoicing((data) => ({
  canInvoice: false,
  problem: 'Este pedido ya tiene la factura F-3: cancélala para facturarlo otra vez.',
  invoices: [
    invoice(data, 3, {
      requestedAt: minutesFromNow(-12), uuid: '5FB2822E-396D-4725-8521-CDC4BDD20CCF', stampedAt: minutesFromNow(-12),
      hasXml: true, hasPdf: true,
    }),
    invoice(data, 2, {
      status: CANCELLED, requestedAt: minutesFromNow(-40), uuid: '0C9F1E7A-2B44-4E1D-9A63-7D5E28B1F0A4',
      stampedAt: minutesFromNow(-40), hasXml: true, hasPdf: true, receiver: { ...ALTAVISTA, cfdiUse: 'G01' },
      cancellationMotive: ERRORS_WITHOUT_REPLACEMENT, cancellationRequestedAt: minutesFromNow(-20), cancelledAt: minutesFromNow(-20),
    }),
    invoice(data, 1, {
      status: REFUSED, refusedAt: minutesFromNow(-49),
      failureMessage: 'El PAC rechazó la factura: CFDI40147 - El código postal del receptor no corresponde al RFC en la lista del SAT.',
    }),
  ],
})));
/** F-2 waits for the customer to accept its cancellation. */
export const PEDIDO_CANCELANDOSE = asOption(orderInvoicing((data) => ({
  canInvoice: false,
  problem: 'Este pedido ya tiene la factura F-2: cancélala para facturarlo otra vez.',
  invoices: [
    invoice(data, 2, {
      status: CANCELLING, uuid: '0C9F1E7A-2B44-4E1D-9A63-7D5E28B1F0A4', stampedAt: minutesFromNow(-2 * 24 * 60), hasXml: true, hasPdf: true,
      cancellationMotive: NOT_CARRIED_OUT, cancellationRequestedAt: minutesFromNow(-30),
    }),
  ],
})));

// OrderPaymentMethod as the contract numbers it.
const CASH = 0;
const CARD = 1;
const TRANSFER = 2;
const OTHER = 4;

/** A payment complement of F-3 (CFDI tipo P), stamped by TienditApp unless the fields say otherwise. */
function complement(folio: number, paymentId: string, fields: Record<string, unknown>) {
  return {
    id: uuid(`complement-${folio}`), orderInvoiceId: uuid('invoice-3'), orderPaymentId: paymentId, number: `P-${folio}`,
    status: STAMPED, paymentForm: '01', amount: 0, installment: 1, previousBalance: 0, remainingBalance: 0,
    requestedAt: '2026-09-12T17:06:00.000Z', issuedAutomatically: true, uuid: null, stampedAt: null, hasXml: false, hasPdf: false,
    failureMessage: null, refusedAt: null, cancellationRequestedAt: null, cancelledAt: null,
    ...fields,
  };
}

/**
 * Sold on credit to Grupo Altavista and invoiced in installments (F-3, PPD, form 99) the day of the down payment. Its
 * payments, each with what happened to its complement:
 * - the down payment in cash: P-1, issued by TienditApp;
 * - a transfer: P-2 refused by the PAC, then P-3 issued again by hand;
 * - one recorded as «Otro», which has no form of the SAT for a complement;
 * - today's card payment, still without one: TienditApp issues it soon, or «Emitir ahora».
 * A quarter of the sale is still owed.
 */
export const PEDIDO_EN_PARCIALIDADES = asOption(((data) => {
  const order = data.orders.find((o: { orderNumber: string }) => o.orderNumber === ORDER_NUMBER);
  const share = (part: number) => r2(order.total * part);
  const [down, transfer, other, card] = [share(0.3), share(0.25), share(0.1), share(0.1)];
  order.balance = r2(order.total - down - transfer - other - card);
  const ids = ['enganche', 'transferencia', 'otro', 'tarjeta'].map((name) => uuid(`payment-${name}`));
  orderInvoicing((d) => ({
    canInvoice: false,
    problem: 'Este pedido ya tiene la factura F-3: cancélala para facturarlo otra vez.',
    invoices: [
      invoice(d, 3, {
        paymentForm: '99', paymentMethod: 'PPD', requestedAt: '2026-09-12T17:02:00.000Z',
        uuid: '5FB2822E-396D-4725-8521-CDC4BDD20CCF', stampedAt: '2026-09-12T17:02:00.000Z', hasXml: true, hasPdf: true,
      }),
    ],
    payments: [
      {
        orderPaymentId: ids[0], paidAt: '2026-09-12T17:00:00.000Z', method: CASH, amount: down, dueBy: '2026-10-05',
        canIssue: false, problem: null, awaitsAutomaticIssue: false,
        complements: [complement(1, ids[0], {
          amount: down, previousBalance: order.total, remainingBalance: r2(order.total - down),
          uuid: '9F3C1E2A-0B1D-4C5E-8F70-1A2B3C4D5E6F', stampedAt: '2026-09-12T17:06:00.000Z', hasXml: true, hasPdf: true,
        })],
      },
      {
        orderPaymentId: ids[1], paidAt: '2026-09-26T16:30:00.000Z', method: TRANSFER, amount: transfer, dueBy: '2026-10-05',
        canIssue: false, problem: null, awaitsAutomaticIssue: false,
        complements: [
          complement(3, ids[1], {
            paymentForm: '03', amount: transfer, installment: 2, previousBalance: r2(order.total - down),
            remainingBalance: r2(order.total - down - transfer), requestedAt: '2026-09-28T15:10:00.000Z', issuedAutomatically: false,
            uuid: '2B7D4F10-6C3A-4E8B-9D21-F05A7C3E1B64', stampedAt: '2026-09-28T15:10:00.000Z', hasXml: true, hasPdf: true,
          }),
          complement(2, ids[1], {
            status: REFUSED, paymentForm: '03', amount: transfer, installment: 2, previousBalance: r2(order.total - down),
            remainingBalance: r2(order.total - down - transfer), requestedAt: '2026-09-26T16:34:00.000Z',
            refusedAt: '2026-09-26T16:34:00.000Z',
            failureMessage: 'El PAC rechazó el complemento: CFDI40102 - El certificado del emisor no estaba vigente en la fecha del comprobante.',
          }),
        ],
      },
      {
        orderPaymentId: ids[2], paidAt: '2026-10-01T18:00:00.000Z', method: OTHER, amount: other, dueBy: '2026-11-05',
        canIssue: false, awaitsAutomaticIssue: false, complements: [],
        problem: 'Este cobro se registró como «Otro», que no es una forma de pago del SAT: su complemento no se emite desde TienditApp. Consúltalo con tu contador.',
      },
      {
        orderPaymentId: ids[3], paidAt: minutesFromNow(-25), method: CARD, amount: card, dueBy: '2026-11-05',
        canIssue: true, problem: null, awaitsAutomaticIssue: true, complements: [],
      },
    ],
  }))(data);
}) as PrepareData);

/** Grupo Altavista keeps its fiscal data; the list shows its RFC. */
export const CLIENTE_CON_DATOS_FISCALES = asOption(((data) => {
  const customer = data.customers.find((c: { name: string }) => c.name === CUSTOMER_NAME);
  customer.fiscalData = ALTAVISTA;
}) as PrepareData);

const invoicingRoutes: ExtraRoutes = (data, helpers) => [
  ['GET', /^\/invoicing\/settings$/, () => helpers.ok(data.invoicing)],
  ['GET', /^\/invoicing\/orders\/([^/]+)$/, (match) => helpers.ok(data.orderInvoicing?.[match[1]])],
];

export const FACTURACION_ROUTES = asOption(invoicingRoutes);
