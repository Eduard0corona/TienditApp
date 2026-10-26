import { AxiosError, AxiosHeaders } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import api from './axios';
import { getApiErrorMessage } from './errors';
import { downloadInvoiceFile, downloadPaymentComplementFile, isOrderInvoicingWaiting, type OrderInvoicing } from './invoicing';
import { CreditNoteAdjustment, OrderInvoiceStatus } from '../types';

vi.mock('./axios', () => ({ default: { get: vi.fn() } }));

/** A refusal as axios gives it for a download: its body is a blob too. */
function refusal(status: number, body: string, type = 'application/problem+json') {
  const config = { headers: new AxiosHeaders() };
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', config, null, {
    status, statusText: '', headers: {}, config, data: new Blob([body], { type }),
  });
}

describe('downloadInvoiceFile', () => {
  beforeEach(() => vi.resetAllMocks());

  it('names the file as the server does', async () => {
    vi.mocked(api.get).mockResolvedValue({ data: new Blob(['<cfdi/>']), headers: { 'content-disposition': 'attachment; filename="F-1.xml"' } });

    const file = await downloadInvoiceFile('inv-1', 'xml');

    expect(file.fileName).toBe('F-1.xml');
    expect(api.get).toHaveBeenCalledWith('/invoicing/invoices/inv-1/xml', { responseType: 'blob' });
  });

  it('reads back the server\'s reason, which comes as a blob, so it reaches the person', async () => {
    const reason = 'El PDF no está disponible por ahora: descarga el XML, que es la factura.';
    vi.mocked(api.get).mockRejectedValue(refusal(404, JSON.stringify({ status: 404, detail: reason })));

    const error = await downloadInvoiceFile('inv-1', 'pdf').catch((err: unknown) => err);

    expect(getApiErrorMessage(error, 'No se pudo descargar el PDF.')).toBe(reason);
  });

  it('keeps the generic message when the refusal is not JSON', async () => {
    vi.mocked(api.get).mockRejectedValue(refusal(404, '<html>Not found</html>', 'text/html'));

    const error = await downloadInvoiceFile('inv-1', 'pdf').catch((err: unknown) => err);

    expect(getApiErrorMessage(error, 'No se pudo descargar el PDF.')).toBe('No se pudo descargar el PDF.');
  });
});

describe('downloadPaymentComplementFile', () => {
  beforeEach(() => vi.resetAllMocks());

  it('asks for the complement\'s file and names it as a complement when the server gives no name', async () => {
    vi.mocked(api.get).mockResolvedValue({ data: new Blob(['%PDF']), headers: {} });

    const file = await downloadPaymentComplementFile('pc-1', 'pdf');

    expect(file.fileName).toBe('complemento.pdf');
    expect(api.get).toHaveBeenCalledWith('/invoicing/payment-complements/pc-1/pdf', { responseType: 'blob' });
  });
});

describe('isOrderInvoicingWaiting', () => {
  type Payment = OrderInvoicing['payments'][number];
  const payment = (fields: Partial<Payment>): Payment => ({
    orderPaymentId: 'pay-1', paidAt: '2026-10-04T17:00:00Z', method: 0, amount: 50, dueBy: '2026-11-05', canIssue: false,
    problem: null, awaitsAutomaticIssue: false, complements: [], ...fields,
  });
  const complement = (status: OrderInvoiceStatus) => ({ status, emails: [] }) as unknown as Payment['complements'][number];
  const invoice = (status: OrderInvoiceStatus) => ({ status, emails: [] }) as unknown as OrderInvoicing['invoices'][number];
  type Adjustment = OrderInvoicing['adjustments'][number];
  const adjustment = (fields: Partial<Adjustment>): Adjustment => ({
    kind: CreditNoteAdjustment.Refund, adjustmentId: 'ref-1', at: '2026-10-04T17:00:00Z', amount: 20, method: 1, writeOffReason: undefined,
    notes: null, reversed: false, canIssue: false, problem: null, awaitsAutomaticIssue: false, creditNotes: [], ...fields,
  });
  const note = (status: OrderInvoiceStatus) => ({ status, emails: [] }) as unknown as Adjustment['creditNotes'][number];

  it('reads again while an invoice or a complement waits for the PAC or the SAT', () => {
    expect(isOrderInvoicingWaiting({ invoices: [invoice(OrderInvoiceStatus.Stamping)], payments: [], adjustments: [] })).toBe(true);
    expect(isOrderInvoicingWaiting({ invoices: [invoice(OrderInvoiceStatus.Cancelling)], payments: [], adjustments: [] })).toBe(true);
    expect(isOrderInvoicingWaiting({
      invoices: [invoice(OrderInvoiceStatus.Stamped)],
      payments: [payment({ complements: [complement(OrderInvoiceStatus.Stamping)] })],
      adjustments: [],
    })).toBe(true);
  });

  it('reads again while a payment waits for the complement TienditApp issues on its own', () => {
    expect(isOrderInvoicingWaiting({
      invoices: [invoice(OrderInvoiceStatus.Stamped)],
      payments: [payment({ canIssue: true, awaitsAutomaticIssue: true })],
      adjustments: [],
    })).toBe(true);
  });

  it('stops once nothing changes on its own: what is left is issued again by hand, or cannot be issued', () => {
    expect(isOrderInvoicingWaiting({
      invoices: [invoice(OrderInvoiceStatus.Stamped)],
      payments: [
        payment({ complements: [complement(OrderInvoiceStatus.Stamped)] }),
        payment({ canIssue: true, complements: [complement(OrderInvoiceStatus.Refused)] }),
        payment({ problem: 'Este cobro se registró como «Otro».' }),
      ],
      adjustments: [
        adjustment({ creditNotes: [note(OrderInvoiceStatus.Stamped)] }),
        adjustment({ canIssue: true, creditNotes: [note(OrderInvoiceStatus.Refused)] }),
        adjustment({ problem: 'Este reembolso se registró como «Otro».' }),
      ],
    })).toBe(false);
  });

  it('reads again while a refund or a write-off waits for its credit note, or a credit note for the PAC or the SAT', () => {
    const stamped = [invoice(OrderInvoiceStatus.Stamped)];
    expect(isOrderInvoicingWaiting({ invoices: stamped, payments: [], adjustments: [adjustment({ canIssue: true, awaitsAutomaticIssue: true })] }))
      .toBe(true);
    expect(isOrderInvoicingWaiting({ invoices: stamped, payments: [], adjustments: [adjustment({ creditNotes: [note(OrderInvoiceStatus.Stamping)] })] }))
      .toBe(true);
    expect(isOrderInvoicingWaiting({ invoices: stamped, payments: [], adjustments: [adjustment({ creditNotes: [note(OrderInvoiceStatus.Cancelling)] })] }))
      .toBe(true);
  });
});
