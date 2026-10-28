import { asOption, type DemoData, type SceneOptions } from '../fixtures';
import { NOW_ISO, uuid } from '../mocks/data.mjs';

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;
type PrepareData = NonNullable<SceneOptions['prepareData']>;

// Invoicing (Fase 28A, ADR 0045): Configuración › Facturación (`data.invoicing`, the InvoicingSettingsDto), the invoice of
// an order in Pedidos (`data.orderInvoicing`, the OrderInvoicingDto of each order by its id) and the fiscal data of a
// customer in Clientes. The order is ORD-000343, the office of Grupo Altavista: delivered and paid by transfer, or (Fase
// 28C, ADR 0047) invoiced in installments, with the payment complements of its payments, or (Fase 28D, ADR 0048) with
// what was given back and the credit notes of each refund. Since the Fase 28E (ADR 0049), each CFDI says how its latest
// email to the customer went; since the Fase 28F (ADR 0050), whether the customer asked for it online.

// OrderInvoiceStatus and CfdiCancellationMotive as the contract numbers them.
const STAMPED = 1;
const REFUSED = 2;
const CANCELLING = 3;
const CANCELLED = 4;
const ERRORS_WITH_REPLACEMENT = 1;
const ERRORS_WITHOUT_REPLACEMENT = 2;
const NOT_CARRIED_OUT = 3;

export const ORDER_NUMBER = 'ORD-000343';
export const CUSTOMER_NAME = 'Oficinas Grupo Altavista';

/** As its constancia says it: the SAT checks it when the invoice is stamped. */
const ALTAVISTA = { rfc: 'GAL1806043P5', name: 'GRUPO ALTAVISTA', postalCode: '11560', fiscalRegime: '601', cfdiUse: 'G03' };
/** Where Grupo Altavista gets its invoices (Fase 28E). */
const INVOICE_EMAIL = 'facturas@altavista.mx';

const minutesFromNow = (minutes: number) => new Date(Date.parse(NOW_ISO) + minutes * 60_000).toISOString();

/** The CSD of Café Luna, valid for four years since February. */
const CERTIFICATE = {
  number: '30001000000500003416', rfc: 'CLU201005AB1', validFrom: '2026-02-11T18:20:00Z', validTo: '2030-02-11T18:20:00Z',
  uploadedAt: '2026-10-01T16:12:00Z', usable: true, expiresSoon: false, expired: false, otherRfc: false,
};

/** The business's fiscal data in Mi negocio, its CSD and its serie: ready to invoice, with self-invoicing off (Fase 28F). */
const READY = {
  available: true, sandbox: false, serie: 'F', paymentSerie: 'P', creditNoteSerie: 'NC', rfc: 'CLU201005AB1', legalName: 'CAFE LUNA', fiscalRegime: '626',
  postalCode: '06700', missingData: [] as string[], certificate: CERTIFICATE, ready: true, selfInvoicing: false,
  selfInvoicePath: '/m/cafe-la-esquina/facturar',
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
/** Its customers invoice their tickets online (Fase 28F): the page's address shows under the switch. */
export const FACTURACION_AUTOFACTURA = asOption(settings({ ...READY, selfInvoicing: true }));
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
    refusedAt: null, cancellationRequestedAt: null, cancelledAt: null, receiverEmail: null, emails: [], requestedByCustomer: false,
    canSubstitute: false,
    awaitsReplacedCancellation: false,
    ...fields,
  };
}

/** What the order's invoicing says; the draft comes from the CFDI preview's route. */
const orderInvoicing = (build: (data: DemoData) => Record<string, unknown>): PrepareData => (data) => {
  const order = data.orders.find((o: { orderNumber: string }) => o.orderNumber === ORDER_NUMBER);
  data.orderInvoicing = {
    [order.id]: {
      available: true, sandbox: false, canInvoice: true, problem: null, proposedReceiver: ALTAVISTA, proposedEmail: INVOICE_EMAIL,
      customerId: uuid('cust:altavista'), customerName: CUSTOMER_NAME, invoices: [], payments: [], adjustments: [],
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
    failureMessage: null, refusedAt: null, cancellationRequestedAt: null, cancelledAt: null, emails: [],
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

// CreditNoteAdjustment as the contract numbers it.
const REFUND = 0;

/** A credit note of F-3 (CFDI tipo E) for what a refund gave back, with its IVA; stamped by TienditApp unless the fields say otherwise. */
function creditNote(folio: number, amount: number, fields: Record<string, unknown>) {
  const subTotal = r2(amount / 1.16);
  return {
    id: uuid(`credit-note-${folio}`), orderInvoiceId: uuid('invoice-3'), number: `NC-${folio}`, status: STAMPED, paymentForm: '03',
    subTotal, taxTotal: r2(amount - subTotal), total: amount, requestedAt: minutesFromNow(-34), issuedAutomatically: true,
    uuid: null, stampedAt: null, hasXml: false, hasPdf: false, failureMessage: null, refusedAt: null,
    cancellationRequestedAt: null, cancelledAt: null, emails: [],
    ...fields,
  };
}

/** A refund of the order, as the invoicing of the order describes it. */
function refund(name: string, at: string, method: number, amount: number, notes: string, fields: Record<string, unknown>) {
  return {
    kind: REFUND, adjustmentId: uuid(`refund-${name}`), at, amount, method, writeOffReason: null, notes, reversed: false,
    canIssue: false, problem: null, awaitsAutomaticIssue: false, creditNotes: [],
    ...fields,
  };
}

/**
 * Invoiced (F-3, PUE, by transfer) and then four refunds, each with what happened to its credit note:
 * - two conchas that were missing, given back by transfer: NC-1, issued by TienditApp;
 * - one recorded as «Otro», which has no form of the SAT for a credit note;
 * - a double charge given back to the card: NC-2 refused by the PAC, to issue again;
 * - a cold americano given back in cash a moment ago, still without one: TienditApp issues it soon, or «Emitir ahora».
 * With NC-1 in force, F-3 says it is cancelled after it.
 */
export const PEDIDO_CON_NOTAS_DE_CREDITO = asOption(((data) => {
  const order = data.orders.find((o: { orderNumber: string }) => o.orderNumber === ORDER_NUMBER);
  const share = (part: number) => r2(order.total * part);
  const [transfer, other, card, cash] = [share(0.12), share(0.05), share(0.04), share(0.06)];
  orderInvoicing((d) => ({
    canInvoice: false,
    problem: 'Este pedido ya tiene la factura F-3: cancélala para facturarlo otra vez.',
    invoices: [
      invoice(d, 3, { uuid: '5FB2822E-396D-4725-8521-CDC4BDD20CCF', stampedAt: minutesFromNow(-50), hasXml: true, hasPdf: true }),
    ],
    adjustments: [
      refund('conchas', minutesFromNow(-40), TRANSFER, transfer, 'Faltaron dos conchas', {
        creditNotes: [creditNote(1, transfer, {
          uuid: '7A1C2E3F-4B5D-4C6E-8F9A-0B1C2D3E4F5A', stampedAt: minutesFromNow(-34), hasXml: true, hasPdf: true,
        })],
      }),
      refund('vale', minutesFromNow(-30), OTHER, other, 'Se compensó con un vale', {
        problem: 'Este reembolso se registró como «Otro», que no es una forma de pago del SAT: su nota de crédito no se emite desde TienditApp. Consúltalo con tu contador.',
      }),
      refund('tarjeta', minutesFromNow(-20), CARD, card, 'Se cobró dos veces con tarjeta', {
        canIssue: true,
        creditNotes: [creditNote(2, card, {
          status: REFUSED, paymentForm: '04', requestedAt: minutesFromNow(-14), refusedAt: minutesFromNow(-14),
          failureMessage: 'El PAC rechazó la nota de crédito: CFDI40102 - El certificado del emisor no estaba vigente en la fecha del comprobante.',
        })],
      }),
      refund('americano', minutesFromNow(-4), CASH, cash, 'Un americano llegó frío', { canIssue: true, awaitsAutomaticIssue: true }),
    ],
  }))(data);
}) as PrepareData);

/**
 * A sale to the general public that the global invoice F-58 covers (Fase 28I, ADR 0054): it is not invoiced on its own, and
 * its refunds take credit notes of F-58, to the general public, never by email:
 * - two conchas that were missing, given back by transfer: NC-4, issued by TienditApp;
 * - a cold americano given back in cash a moment ago, still without one: TienditApp issues it soon, or «Emitir ahora».
 */
export const PEDIDO_EN_FACTURA_GLOBAL = asOption(((data) => {
  const order = data.orders.find((o: { orderNumber: string }) => o.orderNumber === ORDER_NUMBER);
  const share = (part: number) => r2(order.total * part);
  const [transfer, cash] = [share(0.12), share(0.06)];
  orderInvoicing(() => ({
    canInvoice: false,
    problem: 'Esta venta está en la factura global F-58. Si el cliente pide su factura, cancela esa global con el motivo 04 en '
      + 'Reportes › Factura global, factura esta venta y emite otra vez la global del periodo: ya no la incluirá.',
    proposedReceiver: null,
    proposedEmail: null,
    globalInvoiceNumber: 'F-58',
    adjustments: [
      refund('conchas', minutesFromNow(-40), TRANSFER, transfer, 'Faltaron dos conchas', {
        creditNotes: [creditNote(4, transfer, {
          orderInvoiceId: null, globalInvoiceId: uuid('global-58'), globalInvoiceNumber: 'F-58',
          uuid: '3C8E1B2D-5F4A-4B6C-9D7E-8A0F1B2C3D4E', stampedAt: minutesFromNow(-34), hasXml: true, hasPdf: true,
        })],
      }),
      refund('americano', minutesFromNow(-4), CASH, cash, 'Un americano llegó frío', { canIssue: true, awaitsAutomaticIssue: true }),
    ],
  }))(data);
}) as PrepareData);

/** Grupo Altavista keeps its fiscal data and the email for its invoices; the list shows its RFC. */
export const CLIENTE_CON_DATOS_FISCALES = asOption(((data) => {
  const customer = data.customers.find((c: { name: string }) => c.name === CUSTOMER_NAME);
  customer.fiscalData = ALTAVISTA;
  customer.invoiceEmail = INVOICE_EMAIL;
}) as PrepareData);

// CfdiEmailDocument and CfdiEmailFailure as the contract numbers them.
const INVOICE_DOCUMENT = 0;
const REJECTED = 0;

/** An email of F-3 to Grupo Altavista, sent by TienditApp when it was stamped unless the fields say otherwise. */
function email(name: string, fields: Record<string, unknown>) {
  return {
    id: uuid(`email-${name}`), document: INVOICE_DOCUMENT, cfdiId: uuid('invoice-3'), to: INVOICE_EMAIL, requestedAt: minutesFromNow(-12),
    byTienditApp: true, byCustomer: false, sentAt: null, sentWithoutPdf: false, failedAt: null, failure: null, failedAttempts: 0, lastFailedAttemptAt: null,
    ...fields,
  };
}

/** F-3, stamped for Grupo Altavista with the email for its invoices: TienditApp sent its XML and PDF a minute later. */
export const PEDIDO_FACTURA_POR_CORREO = asOption(orderInvoicing((data) => ({
  canInvoice: false,
  problem: 'Este pedido ya tiene la factura F-3: cancélala para facturarlo otra vez.',
  invoices: [
    invoice(data, 3, {
      requestedAt: minutesFromNow(-12), uuid: '5FB2822E-396D-4725-8521-CDC4BDD20CCF', stampedAt: minutesFromNow(-12),
      hasXml: true, hasPdf: true, receiverEmail: INVOICE_EMAIL,
      emails: [email('timbre', { sentAt: minutesFromNow(-11) })],
    }),
  ],
})));

/** The email was typed wrong when invoicing: the customer's mail server refused it. */
export const PEDIDO_CORREO_RECHAZADO = asOption(orderInvoicing((data) => ({
  canInvoice: false,
  problem: 'Este pedido ya tiene la factura F-3: cancélala para facturarlo otra vez.',
  invoices: [
    invoice(data, 3, {
      requestedAt: minutesFromNow(-12), uuid: '5FB2822E-396D-4725-8521-CDC4BDD20CCF', stampedAt: minutesFromNow(-12),
      hasXml: true, hasPdf: true, receiverEmail: 'facturas@altavsita.mx',
      emails: [email('timbre', { to: 'facturas@altavsita.mx', failedAt: minutesFromNow(-11), failure: REJECTED })],
    }),
  ],
})));

/**
 * Grupo Altavista invoiced its ticket on the page of the business (Fase 28F): F-3 says the customer asked for it online, and
 * its latest email is the one they asked for again from there, after the one TienditApp sent when it was stamped.
 */
export const PEDIDO_AUTOFACTURADO = asOption(orderInvoicing((data) => ({
  canInvoice: false,
  problem: 'Este pedido ya tiene la factura F-3: cancélala para facturarlo otra vez.',
  invoices: [
    invoice(data, 3, {
      requestedAt: minutesFromNow(-12), uuid: '5FB2822E-396D-4725-8521-CDC4BDD20CCF', stampedAt: minutesFromNow(-12),
      hasXml: true, hasPdf: true, receiverEmail: INVOICE_EMAIL, requestedByCustomer: true,
      emails: [
        email('reenvio', { requestedAt: minutesFromNow(-3), byTienditApp: false, byCustomer: true, sentAt: minutesFromNow(-2) }),
        email('timbre', { sentAt: minutesFromNow(-11) }),
      ],
    }),
  ],
})));

/**
 * Replacing an invoice with errors (Fase 28G, ADR 0051): F-3 went out with the wrong postal code of Grupo Altavista and can
 * be replaced by an owner.
 */
export const PEDIDO_SUSTITUIBLE = asOption(orderInvoicing((data) => ({
  canInvoice: false,
  problem: 'Este pedido ya tiene la factura F-3: cancélala para facturarlo otra vez.',
  invoices: [
    invoice(data, 3, {
      requestedAt: minutesFromNow(-12), uuid: '5FB2822E-396D-4725-8521-CDC4BDD20CCF', stampedAt: minutesFromNow(-12),
      hasXml: true, hasPdf: true, receiver: { ...ALTAVISTA, postalCode: '11650' }, receiverEmail: INVOICE_EMAIL, canSubstitute: true,
    }),
  ],
})));
/**
 * F-4 replaced F-3 and is stamped; the SAT did not relate them yet, so F-3 is still stamped with the reason, and TienditApp
 * asks again (or the owner does, «Cancelar con relación»). F-4 is cancelled only after F-3.
 */
export const PEDIDO_SUSTITUIDO = asOption(orderInvoicing((data) => ({
  canInvoice: false,
  problem: 'Este pedido ya tiene la factura F-4: cancélala para facturarlo otra vez.',
  invoices: [
    invoice(data, 4, {
      requestedAt: minutesFromNow(-5), uuid: 'A3D9C1E2-7B5F-4C8A-9E21-6F0B4D7C2A15', stampedAt: minutesFromNow(-5),
      hasXml: true, hasPdf: true, receiverEmail: INVOICE_EMAIL, replacesInvoiceId: uuid('invoice-3'), replacesNumber: 'F-3',
      substitutionProblem: 'Esta factura sustituye a la F-3, que todavía no se cancela: sustitúyela cuando el SAT cancele la F-3.',
    }),
    invoice(data, 3, {
      requestedAt: minutesFromNow(-12), uuid: '5FB2822E-396D-4725-8521-CDC4BDD20CCF', stampedAt: minutesFromNow(-12),
      hasXml: true, hasPdf: true, receiver: { ...ALTAVISTA, postalCode: '11650' }, receiverEmail: INVOICE_EMAIL,
      replacedByInvoiceId: uuid('invoice-4'), replacedByNumber: 'F-4',
      failureMessage: 'El SAT todavía no relaciona esta factura con la que la sustituye: puede que aún no reciba la nueva (tarda hasta dos días).',
    }),
  ],
})));
/** F-3 was cancelled with relation (01) to F-4, which is the order's invoice now. */
export const PEDIDO_SUSTITUIDO_CANCELADO = asOption(orderInvoicing((data) => ({
  canInvoice: false,
  problem: 'Este pedido ya tiene la factura F-4: cancélala para facturarlo otra vez.',
  invoices: [
    invoice(data, 4, {
      requestedAt: minutesFromNow(-5), uuid: 'A3D9C1E2-7B5F-4C8A-9E21-6F0B4D7C2A15', stampedAt: minutesFromNow(-5),
      hasXml: true, hasPdf: true, receiverEmail: INVOICE_EMAIL, replacesInvoiceId: uuid('invoice-3'), replacesNumber: 'F-3',
      canSubstitute: true,
    }),
    invoice(data, 3, {
      status: CANCELLED, requestedAt: minutesFromNow(-12), uuid: '5FB2822E-396D-4725-8521-CDC4BDD20CCF', stampedAt: minutesFromNow(-12),
      hasXml: true, hasPdf: true, receiver: { ...ALTAVISTA, postalCode: '11650' }, receiverEmail: INVOICE_EMAIL,
      replacedByInvoiceId: uuid('invoice-4'), replacedByNumber: 'F-4', cancellationMotive: ERRORS_WITH_REPLACEMENT,
      cancellationRequestedAt: minutesFromNow(-4), cancelledAt: minutesFromNow(-4),
    }),
  ],
})));

const invoicingRoutes: ExtraRoutes = (data, helpers) => [
  ['GET', /^\/invoicing\/settings$/, () => helpers.ok(data.invoicing)],
  ['GET', /^\/invoicing\/orders\/([^/]+)$/, (match) => helpers.ok(data.orderInvoicing?.[match[1]])],
];

export const FACTURACION_ROUTES = asOption(invoicingRoutes);
