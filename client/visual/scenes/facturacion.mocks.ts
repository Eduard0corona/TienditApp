import { asOption, type DemoData, type SceneOptions } from '../fixtures';
import { NOW_ISO, uuid } from '../mocks/data.mjs';

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;
type PrepareData = NonNullable<SceneOptions['prepareData']>;

// Invoicing (Fase 28A, ADR 0045): Configuración › Facturación (`data.invoicing`, the InvoicingSettingsDto), the invoice of
// an order in Pedidos (`data.orderInvoicing`, the OrderInvoicingDto of each order by its id) and the fiscal data of a
// customer in Clientes. The order is ORD-000343, the office of Grupo Altavista: delivered and paid by transfer, or (Fase
// 28C, ADR 0047) invoiced in installments, with the payment complements of its payments, or (Fase 28D, ADR 0048) with
// what was given back and the credit notes of each refund. Since the Fase 28E (ADR 0049), each CFDI says how its latest
// email to the customer went; since the Fase 28F (ADR 0050), whether the customer asked for it online; since the Fase
// 28M (ADR 0058), how its latest WhatsApp went, with the business's WhatsApp connected; since the Fase 28N (ADR 0059),
// the notice of a cancellation as its latest email.

// OrderInvoiceStatus and CfdiCancellationMotive as the contract numbers them.
const STAMPING = 0;
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
  // The CFDI by WhatsApp (Fase 28M): without the business's WhatsApp connected, the card points to connecting it.
  whatsAppConnected: false, whatsAppTemplateId: null, whatsAppTemplates: [] as unknown[],
  whatsAppDefaultText: 'Hola {nombre}: {negocio} te envía {comprobante} por {monto}, del {fecha}. Descarga el XML y el PDF aquí: {enlace}',
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
/** With the logo of Configuración › Negocio, which goes on the PDF of each CFDI (Fase 28L, ADR 0057). */
export const FACTURACION_CON_LOGO = asOption((data: DemoData) => {
  data.invoicing = READY;
  const logo = '/visual-img/logo.svg';
  data.tenant.logoUrl = logo;
  data.tenant.logoImages = { original: logo, large: logo, medium: logo, small: logo, thumbnail: logo };
});
/** WhatsApp connected (Fase 28M): the CFDI go with the template approved in Meta with a document header. */
export const FACTURACION_WHATSAPP = asOption(settings({
  ...READY, whatsAppConnected: true, whatsAppTemplateId: uuid('template:factura'),
  whatsAppTemplates: [
    {
      id: uuid('template:factura'), name: 'Factura con PDF', providerTemplateName: 'factura_pdf',
      body: 'Hola {nombre}: {negocio} te envía {comprobante} por {monto}, del {fecha}. Descárgala aquí: {enlace}',
    },
    { id: uuid('template:factura-24h'), name: 'Factura sin aprobar', providerTemplateName: null, body: 'Hola {nombre}, tu factura: {enlace}' },
  ],
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
    refusedAt: null, cancellationRequestedAt: null, cancelledAt: null, receiverEmail: null, emails: [], whatsApps: [], requestedByCustomer: false,
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
      whatsAppConnected: false,
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

/**
 * F-3 stayed «Timbrando» past the PAC's window (Fase 28K, ADR 0056): asked for four days ago, while the PAC did not answer.
 * It is no longer asked again: it is checked in the SAT's portal.
 */
export const PEDIDO_TIMBRANDO_SIN_TIMBRE = asOption(orderInvoicing((data) => ({
  canInvoice: false,
  problem: 'Este pedido ya tiene la factura F-3: cancélala para facturarlo otra vez.',
  invoices: [
    invoice(data, 3, {
      status: STAMPING, requestedAt: minutesFromNow(-4 * 24 * 60), stampWindowEndsAt: minutesFromNow(-4 * 24 * 60 + 71 * 60),
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
    failureMessage: null, refusedAt: null, cancellationRequestedAt: null, cancelledAt: null, emails: [], whatsApps: [],
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
const WRITE_OFF = 1;
/** OrderWriteOffReason.Discount (Fase 28J): a discount or rebate after the sale. */
const DISCOUNT = 4;

/**
 * A credit note of F-3 (CFDI tipo E) for what a refund gave back or a write-off forgave, with its IVA; stamped by TienditApp
 * unless the fields say otherwise (also which invoice it credits).
 */
function creditNote(folio: number, amount: number, fields: Record<string, unknown>) {
  const subTotal = r2(amount / 1.16);
  return {
    id: uuid(`credit-note-${folio}`), orderInvoiceId: uuid('invoice-3'), number: `NC-${folio}`, status: STAMPED, paymentForm: '03',
    subTotal, taxTotal: r2(amount - subTotal), total: amount, requestedAt: minutesFromNow(-34), issuedAutomatically: true,
    uuid: null, stampedAt: null, hasXml: false, hasPdf: false, failureMessage: null, refusedAt: null,
    cancellationRequestedAt: null, cancelledAt: null, emails: [], whatsApps: [],
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

/** A write-off of the order (Fase 26B), as the invoicing of the order describes it. */
function writeOff(name: string, at: string, reason: number, amount: number, notes: string, fields: Record<string, unknown>) {
  return {
    kind: WRITE_OFF, adjustmentId: uuid(`write-off-${name}`), at, amount, method: null, writeOffReason: reason, notes,
    reversed: false, canIssue: false, problem: null, awaitsAutomaticIssue: false, creditNotes: [],
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
 * A sale to the general public, without a customer, that the global invoice F-58 covers (Fase 28I, ADR 0054): it is not
 * invoiced on its own (its credit notes in force go first if the customer asks for theirs), and its refunds take credit notes
 * of F-58, to the general public, never by email, and so do its write-offs (Fase 28J, ADR 0055):
 * - a discount for the delay, taken off what was still owed before the global invoice: NC-3, by «Condonación» (15),
 *   «Descuento o bonificación», issued by TienditApp before the refund's, which came after it;
 * - two conchas that were missing, given back by transfer: NC-4, issued by TienditApp;
 * - a cold americano given back in cash a moment ago, still without one: TienditApp issues it soon, or «Emitir ahora».
 */
export const PEDIDO_EN_FACTURA_GLOBAL = asOption(((data) => {
  const order = data.orders.find((o: { orderNumber: string }) => o.orderNumber === ORDER_NUMBER);
  const share = (part: number) => r2(order.total * part);
  const [discount, transfer, cash] = [share(0.05), share(0.12), share(0.06)];
  const ofF58 = { orderInvoiceId: null, globalInvoiceId: uuid('global-58'), globalInvoiceNumber: 'F-58' };
  orderInvoicing(() => ({
    canInvoice: false,
    problem: 'Esta venta está en la factura global F-58. Si el cliente pide su factura, cancela con el motivo 02 las notas '
      + 'de crédito vigentes de esa global (NC-3, NC-4) en el pedido de cada venta, cancela la global con el motivo 04 en Reportes › '
      + 'Factura global, factura esta venta y emite otra vez la global del periodo: ya no la incluirá.',
    proposedReceiver: null,
    proposedEmail: null,
    customerId: null,
    customerName: null,
    globalInvoiceNumber: 'F-58',
    adjustments: [
      writeOff('retraso', minutesFromNow(-90), DISCOUNT, discount, 'Se entregó una hora tarde', {
        creditNotes: [creditNote(3, discount, {
          ...ofF58, paymentForm: '15', requestedAt: minutesFromNow(-36),
          uuid: '9B2D4F6A-1C3E-4A5B-8C7D-6E5F4A3B2C1D', stampedAt: minutesFromNow(-36), hasXml: true, hasPdf: true,
        })],
      }),
      refund('conchas', minutesFromNow(-40), TRANSFER, transfer, 'Faltaron dos conchas', {
        creditNotes: [creditNote(4, transfer, {
          ...ofF58, uuid: '3C8E1B2D-5F4A-4B6C-9D7E-8A0F1B2C3D4E', stampedAt: minutesFromNow(-34), hasXml: true, hasPdf: true,
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

// CfdiEmailDocument, CfdiEmailKind and CfdiEmailFailure as the contract numbers them.
const INVOICE_DOCUMENT = 0;
const DOCUMENTS = 0;
const CANCELLATION = 1;
const REJECTED = 0;

/** An email of F-3 to Grupo Altavista, sent by TienditApp when it was stamped unless the fields say otherwise. */
function email(name: string, fields: Record<string, unknown>) {
  return {
    id: uuid(`email-${name}`), document: INVOICE_DOCUMENT, kind: DOCUMENTS, cfdiId: uuid('invoice-3'), to: INVOICE_EMAIL, requestedAt: minutesFromNow(-12),
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
 * F-3 cancelled because it had errors (Fase 28N): TienditApp told Grupo Altavista by email, after the email of its files,
 * and the order can be invoiced again.
 */
export const PEDIDO_CANCELACION_AVISADA = asOption(orderInvoicing((data) => ({
  invoices: [
    invoice(data, 3, {
      status: CANCELLED, requestedAt: minutesFromNow(-12), uuid: '5FB2822E-396D-4725-8521-CDC4BDD20CCF', stampedAt: minutesFromNow(-12),
      hasXml: true, hasPdf: true, receiverEmail: INVOICE_EMAIL,
      cancellationMotive: ERRORS_WITHOUT_REPLACEMENT, cancellationRequestedAt: minutesFromNow(-5), cancelledAt: minutesFromNow(-5),
      emails: [
        email('aviso', { kind: CANCELLATION, requestedAt: minutesFromNow(-5), sentAt: minutesFromNow(-4) }),
        email('timbre', { sentAt: minutesFromNow(-11) }),
      ],
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

// OutgoingMessageStatus as the contract numbers it.
const MESSAGE_SENT = 1;
const MESSAGE_FAILED = 2;
/** The WhatsApp of Grupo Altavista's accountant (Fase 28M). */
const INVOICE_WHATSAPP = '+525512345678';

/** A WhatsApp message of F-3 to Grupo Altavista, sent by TienditApp when it was stamped unless the fields say otherwise. */
function whatsApp(name: string, fields: Record<string, unknown>) {
  return {
    id: uuid(`whatsapp-${name}`), document: INVOICE_DOCUMENT, cfdiId: uuid('invoice-3'), phone: INVOICE_WHATSAPP, requestedAt: minutesFromNow(-12),
    byTienditApp: true, status: MESSAGE_SENT, sentAt: minutesFromNow(-11), deliveredAt: null, readAt: null, attempts: 1, error: null,
    ...fields,
  };
}

/** Never invoiced, with the business's WhatsApp connected (Fase 28M): the order's phone is proposed for its PDF. */
export const PEDIDO_POR_FACTURAR_CON_WHATSAPP = asOption(orderInvoicing(() => ({
  whatsAppConnected: true, proposedWhatsApp: INVOICE_WHATSAPP,
})));

/** F-3 went by email and by WhatsApp when it was stamped: the accountant already read the WhatsApp. */
export const PEDIDO_FACTURA_POR_WHATSAPP = asOption(orderInvoicing((data) => ({
  canInvoice: false,
  problem: 'Este pedido ya tiene la factura F-3: cancélala para facturarlo otra vez.',
  whatsAppConnected: true,
  proposedWhatsApp: INVOICE_WHATSAPP,
  invoices: [
    invoice(data, 3, {
      requestedAt: minutesFromNow(-12), uuid: '5FB2822E-396D-4725-8521-CDC4BDD20CCF', stampedAt: minutesFromNow(-12),
      hasXml: true, hasPdf: true, receiverEmail: INVOICE_EMAIL, receiverWhatsApp: INVOICE_WHATSAPP,
      emails: [email('timbre', { sentAt: minutesFromNow(-11) })],
      whatsApps: [whatsApp('timbre', { deliveredAt: minutesFromNow(-11), readAt: minutesFromNow(-6) })],
    }),
  ],
})));

/** Sent without a template approved in Meta, out of the 24 hours: WhatsApp did not deliver it. */
export const PEDIDO_WHATSAPP_NO_ENTREGADO = asOption(orderInvoicing((data) => ({
  canInvoice: false,
  problem: 'Este pedido ya tiene la factura F-3: cancélala para facturarlo otra vez.',
  whatsAppConnected: true,
  invoices: [
    invoice(data, 3, {
      requestedAt: minutesFromNow(-12), uuid: '5FB2822E-396D-4725-8521-CDC4BDD20CCF', stampedAt: minutesFromNow(-12),
      hasXml: true, hasPdf: true, receiverWhatsApp: INVOICE_WHATSAPP,
      whatsApps: [whatsApp('timbre', {
        status: MESSAGE_FAILED,
        error: 'WhatsApp no lo entregó: el cliente no escribió en las últimas 24 horas. Usa una plantilla aprobada en Meta.',
      })],
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
