import { asOption, type SceneOptions } from '../fixtures';
import { uuid } from '../mocks/data.mjs';

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;

// OrderChannel as the contract numbers it.
const UBER_EATS = 4;

/** Cents, as the server rounds them. */
const r2 = (amount: number) => Math.round(amount * 100) / 100;

/** A month with what TienditApp calculated: 2.5 % of ISR and half the IVA of its orders, without IVA. */
function month(number: number, orders: number, salesWithoutTax: number, voucher?: { isr: number; vat: number; operation: number; spans?: boolean }) {
  const calculatedIsr = r2(salesWithoutTax * 0.025);
  const calculatedVat = r2(salesWithoutTax * 0.16 * 0.5);
  return {
    month: number,
    orderCount: orders,
    salesWithoutTax,
    calculatedIsr,
    calculatedVat,
    certificateCount: voucher ? 1 : 0,
    certificateOperationTotal: voucher?.operation ?? null,
    certificateIsr: voucher?.isr ?? null,
    certificateVat: voucher?.vat ?? null,
    isrDifference: voucher ? r2(voucher.isr - calculatedIsr) : null,
    vatDifference: voucher ? r2(voucher.vat - calculatedVat) : null,
    spansMonths: voucher?.spans ?? false,
  };
}

function certificate(id: string, monthFrom: number, monthTo: number, isr: number, vat: number, operation: number, removed?: string) {
  return {
    id: uuid(`withholding-${id}`),
    channel: UBER_EATS,
    uuid: uuid(`cfdi-${id}`),
    issuerRfc: 'UBV121024TN8',
    issuerName: 'UBER B.V.',
    year: 2026,
    monthFrom,
    monthTo,
    issuedOn: `2026-${String(monthTo + 1).padStart(2, '0')}-02T22:28:50`,
    operationTotal: operation,
    isrBase: operation,
    isrWithheld: isr,
    isrRatePercent: r2((isr / operation) * 100),
    vatBase: r2(operation * 0.16),
    vatWithheld: vat,
    vatRatePercent: 50,
    totalWithheld: r2(isr + vat),
    createdAt: `2026-${String(monthTo + 1).padStart(2, '0')}-03T18:00:00Z`,
    removedAt: removed ? '2026-10-03T18:00:00Z' : null,
    removalReason: removed ?? null,
  };
}

// Uber Eats in 2026: July and August add up; September's CFDI withheld $12.40 more ISR than the orders; October has orders
// and no CFDI yet. A CFDI of August uploaded under the wrong platform was removed.
const MONTHS = [
  month(1, 0, 0), month(2, 0, 0), month(3, 0, 0), month(4, 0, 0), month(5, 0, 0), month(6, 0, 0),
  month(7, 41, 14820.5, { isr: 370.51, vat: 1185.64, operation: 14820.5 }),
  month(8, 47, 16950.25, { isr: 423.76, vat: 1356.02, operation: 16950.25 }),
  month(9, 44, 16269.76, { isr: 419.14, vat: 1301.58, operation: 16765.76 }),
  month(10, 6, 2215.9),
];
const sum = (values: number[]) => r2(values.reduce((total, value) => total + value, 0));
const WITH_VOUCHER = MONTHS.filter((each) => each.certificateCount > 0);
const TOTAL = {
  month: null,
  orderCount: sum(MONTHS.map((each) => each.orderCount)),
  salesWithoutTax: sum(MONTHS.map((each) => each.salesWithoutTax)),
  calculatedIsr: sum(MONTHS.map((each) => each.calculatedIsr)),
  calculatedVat: sum(MONTHS.map((each) => each.calculatedVat)),
  certificateCount: WITH_VOUCHER.length,
  certificateOperationTotal: sum(WITH_VOUCHER.map((each) => each.certificateOperationTotal ?? 0)),
  certificateIsr: sum(WITH_VOUCHER.map((each) => each.certificateIsr ?? 0)),
  certificateVat: sum(WITH_VOUCHER.map((each) => each.certificateVat ?? 0)),
  isrDifference: 0,
  vatDifference: 0,
  spansMonths: false,
};
TOTAL.isrDifference = r2(TOTAL.certificateIsr - TOTAL.calculatedIsr);
TOTAL.vatDifference = r2(TOTAL.certificateVat - TOTAL.calculatedVat);

const CERTIFICATES = [
  certificate('jul', 7, 7, 370.51, 1185.64, 14820.5),
  certificate('aug-rappi', 8, 8, 288.1, 921.92, 11524),
  certificate('aug', 8, 8, 423.76, 1356.02, 16950.25),
  certificate('sep', 9, 9, 419.14, 1301.58, 16765.76),
];
CERTIFICATES[1] = { ...CERTIFICATES[1], removedAt: '2026-09-04T18:00:00Z', removalReason: 'Era de Rappi' };

/** Of other platforms or years: nothing yet. */
function empty(channel: number, year: number) {
  return {
    channel,
    year,
    isrWithholdingPercent: 2.5,
    vatWithholdingPercent: 50,
    months: Array.from({ length: 12 }, (_, index) => month(index + 1, 0, 0)),
    total: { ...month(0, 0, 0), month: null },
    certificates: [],
    certificatesTruncated: false,
  };
}

/** What a batch of uploads answers: the one of October went in; the one of September was already registered. */
const UPLOADED = certificate('oct-week', 10, 10, 55.4, 177.27, 2215.9);

const withholdingRoutes: ExtraRoutes = (_data, helpers) => [
  ['GET', /^\/platform-withholdings$/, (_match, query) => {
    const channel = Number(query.get('channel'));
    const year = Number(query.get('year'));
    if (channel !== UBER_EATS || year !== 2026) return helpers.ok(empty(channel, year));
    return helpers.ok({
      channel,
      year,
      isrWithholdingPercent: 2.5,
      vatWithholdingPercent: 50,
      months: [...MONTHS, month(11, 0, 0), month(12, 0, 0)],
      total: TOTAL,
      certificates: CERTIFICATES,
      certificatesTruncated: false,
    });
  }],
  ['POST', /^\/platform-withholdings$/, (_match, _query, body) => {
    const xml = String((body as { xml?: string } | undefined)?.xml ?? '');
    if (xml.includes('septiembre')) {
      return { status: 409, json: { code: 'conflict', detail: 'Este CFDI ya está registrado en Uber Eats, septiembre de 2026.' } };
    }
    return helpers.ok(UPLOADED);
  }],
];

export const RETENCIONES_ROUTES = asOption(withholdingRoutes);

/** Two XML files as the person picks them: the content only tells the mock which one is which. */
export const UPLOAD_FILES = [
  { name: 'uber-eats-octubre-semana-1.xml', mimeType: 'text/xml', buffer: Buffer.from('<?xml version="1.0"?><retenciones:Retenciones/>') },
  { name: 'uber-eats-septiembre.xml', mimeType: 'text/xml', buffer: Buffer.from('<?xml version="1.0"?><!-- septiembre --><retenciones:Retenciones/>') },
];
