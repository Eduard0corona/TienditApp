import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest, type Schemas } from '../../api/typedClient';
import { CfdiCancellationMotive, CreditNoteAdjustment, OrderInvoiceStatus, OrderPaymentMethod, OrderWriteOffReason } from '../../types';
import CreditNoteList from './CreditNoteList';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
const toast = { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() };
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

type Adjustment = Schemas['OrderAdjustmentDto'];
type Note = Schemas['CreditNoteDto'];
type Invoice = Schemas['OrderInvoiceDto'];

const F3: Invoice = {
  id: 'inv-1', orderId: 'o-1', number: 'F-3', status: OrderInvoiceStatus.Stamped,
  receiver: { rfc: 'GAL1806043P5', name: 'GRUPO ALTAVISTA', postalCode: '11560', fiscalRegime: '601', cfdiUse: 'G03' },
  paymentForm: '03', paymentMethod: 'PUE', subTotal: 172.41, taxTotal: 27.59, total: 200, requestedAt: '2026-10-24T16:00:00Z',
  uuid: '5FB2822E-396D-4725-8521-CDC4BDD20CCF', stampedAt: '2026-10-24T16:00:05Z', hasXml: true, hasPdf: true,
  failureMessage: null, refusedAt: null, cancellationMotive: undefined, cancellationRequestedAt: null, cancelledAt: null,
};

const NC1: Note = {
  id: 'nc-1', orderInvoiceId: 'inv-1', number: 'NC-1', status: OrderInvoiceStatus.Stamped, paymentForm: '04', subTotal: 17.24,
  taxTotal: 2.76, total: 20, requestedAt: '2026-10-24T17:05:00Z', issuedAutomatically: true,
  uuid: '7A1C2E3F-4B5D-4C6E-8F9A-0B1C2D3E4F5A', stampedAt: '2026-10-24T17:05:03Z', hasXml: true, hasPdf: true,
  failureMessage: null, refusedAt: null, cancellationMotive: undefined, cancellationRequestedAt: null, cancelledAt: null,
};

const REFUND: Adjustment = {
  kind: CreditNoteAdjustment.Refund, adjustmentId: 'ref-1', at: '2026-10-24T17:00:00Z', amount: 20, method: OrderPaymentMethod.Card,
  writeOffReason: undefined, notes: 'Faltó un café', reversed: false, canIssue: false, problem: null, awaitsAutomaticIssue: false,
  creditNotes: [NC1],
};

const WRITE_OFF: Adjustment = {
  kind: CreditNoteAdjustment.WriteOff, adjustmentId: 'wo-1', at: '2026-10-24T18:00:00Z', amount: 50, method: undefined,
  writeOffReason: OrderWriteOffReason.WontBeCollected, notes: null, reversed: false, canIssue: true, problem: null,
  awaitsAutomaticIssue: true, creditNotes: [],
};

function renderList(adjustments: Adjustment[], canCancel = false, invoices: Invoice[] = [F3]) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <CreditNoteList orderId="o-1" adjustments={adjustments} invoices={invoices} canCancel={canCancel} />
    </QueryClientProvider>,
  );
}

function answer(write: (method: string, path: string, options?: { path?: Record<string, string>; body?: unknown }) => Promise<unknown>) {
  vi.mocked(apiRequest).mockImplementation(write as unknown as typeof apiRequest);
}

describe('CreditNoteList', () => {
  beforeEach(() => vi.resetAllMocks());

  it('shows nothing for an order with nothing given back or forgiven', () => {
    const { container } = renderList([]);
    expect(container).toBeEmptyDOMElement();
  });

  it('shows each refund with its credit note: amounts, form, folio fiscal and files', () => {
    renderList([REFUND]);

    expect(screen.getByRole('heading', { name: 'Notas de crédito' })).toBeInTheDocument();
    expect(screen.getByText(/^La factura F-3 ampara la venta completa/)).toBeInTheDocument();
    const refund = screen.getAllByRole('listitem')[0];
    expect(refund).toHaveTextContent(/Reembolso del .*Tarjeta · \$20\.00/);
    expect(refund).toHaveTextContent('Faltó un café');
    expect(within(refund).getByText('NC-1')).toBeInTheDocument();
    expect(within(refund).getByText('Timbrada')).toBeInTheDocument();
    expect(refund).toHaveTextContent('Por $20.00 (subtotal $17.24 e IVA $2.76)');
    expect(refund).toHaveTextContent('Forma 04 (Tarjeta) · la emitió TienditApp · folio fiscal');
    expect(refund).toHaveTextContent('7A1C2E3F-4B5D-4C6E-8F9A-0B1C2D3E4F5A');
    expect(screen.getByRole('button', { name: 'Descargar XML de la nota de crédito NC-1' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Descargar PDF de la nota de crédito NC-1' })).toBeInTheDocument();
    // Whoever manages the orders downloads; only owners and admins cancel. An adjustment with its note issues no other.
    expect(screen.queryByRole('button', { name: /^Cancelar nota de crédito/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Emitir/ })).not.toBeInTheDocument();
  });

  it('issues the credit note of a write-off before TienditApp does it on its own', async () => {
    let sent: unknown;
    answer((method, path, options) => {
      sent = { method, path, params: options?.path };
      return Promise.resolve({ ...NC1, id: 'nc-2', number: 'NC-2', paymentForm: '15', total: 50 });
    });
    renderList([REFUND, WRITE_OFF]);

    const writeOff = screen.getAllByRole('listitem').find((item) => item.textContent?.startsWith('Condonación del'))!;
    expect(writeOff).toHaveTextContent(/Condonación del .*No se va a cobrar · \$50\.00/);
    expect(within(writeOff).getByText('Sin nota de crédito todavía: TienditApp la emite en unos minutos.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /^Emitir ahora la nota de crédito de la condonación de \$50\.00 del/ }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Nota de crédito NC-2 timbrada.'));
    expect(sent).toEqual({ method: 'post', path: '/api/v1/invoicing/write-offs/{writeOffId}/credit-notes', params: { writeOffId: 'wo-1' } });
  });

  it('says when the PAC refused it, and issues it again for the refund', async () => {
    const refused: Note = {
      ...NC1, status: OrderInvoiceStatus.Refused, uuid: null, stampedAt: null, hasXml: false, hasPdf: false,
      failureMessage: 'CFDI40108 - El TipoDeComprobante es E y la FormaPago no es válida.',
    };
    let sent: unknown;
    answer((method, path, options) => {
      sent = { method, path, params: options?.path };
      return Promise.resolve({ ...refused, status: OrderInvoiceStatus.Stamping, failureMessage: null, number: 'NC-2' });
    });
    renderList([{ ...REFUND, canIssue: true, creditNotes: [refused] }]);

    expect(screen.getByText('Rechazada')).toBeInTheDocument();
    expect(screen.getByText(/CFDI40108/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Descargar/ })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /^Emitir otra vez la nota de crédito del reembolso de \$20\.00 del/ }));

    await waitFor(() => expect(toast.info).toHaveBeenCalledWith('El PAC no respondió: TienditApp sigue pidiendo el timbre de NC-2.'));
    expect(sent).toEqual({ method: 'post', path: '/api/v1/invoicing/refunds/{refundId}/credit-notes', params: { refundId: 'ref-1' } });
  });

  it('says why an adjustment has no credit note, without offering to issue it', () => {
    renderList([
      {
        ...REFUND, method: OrderPaymentMethod.Other, canIssue: false, creditNotes: [],
        problem: 'Este reembolso no tiene una forma de pago del SAT para su nota de crédito.',
      },
      { ...WRITE_OFF, reversed: true, canIssue: false, awaitsAutomaticIssue: false, problem: 'Esa condonación ya se deshizo: no lleva nota de crédito.' },
    ]);

    expect(screen.getByText('Este reembolso no tiene una forma de pago del SAT para su nota de crédito.')).toBeInTheDocument();
    expect(screen.getByText('Esa condonación ya se deshizo: no lleva nota de crédito.')).toBeInTheDocument();
    expect(screen.getByText('Deshecha')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Emitir/ })).not.toBeInTheDocument();
    expect(screen.queryByText(/TienditApp la emite/)).not.toBeInTheDocument();
  });

  it('asks the PAC again for one that waits', async () => {
    const stamping: Note = { ...NC1, status: OrderInvoiceStatus.Stamping, uuid: null, stampedAt: null, hasXml: false, hasPdf: false };
    let sent: unknown;
    answer((method, path, options) => {
      sent = { method, path, params: options?.path };
      return Promise.resolve(NC1);
    });
    renderList([{ ...REFUND, creditNotes: [stamping] }]);

    expect(screen.getByText('Timbrando')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Volver a pedir el timbre de la nota de crédito NC-1' }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Nota de crédito NC-1 timbrada.'));
    expect(sent).toEqual({ method: 'post', path: '/api/v1/invoicing/credit-notes/{creditNoteId}/retry', params: { creditNoteId: 'nc-1' } });
  });

  it('lets owners and admins cancel the credit note of a refund, only because it has errors', async () => {
    let sent: unknown;
    answer((method, path, options) => {
      sent = { method, path, params: options?.path, body: options?.body };
      return Promise.resolve({ ...NC1, status: OrderInvoiceStatus.Cancelling });
    });
    renderList([REFUND], true);

    await userEvent.click(screen.getByRole('button', { name: 'Cancelar nota de crédito NC-1' }));
    const dialog = await screen.findByRole('dialog', { name: 'Cancelar la nota de crédito NC-1' });
    expect(dialog).toHaveTextContent('sigue vigente mientras tanto. Un reembolso registrado no se deshace: su nota solo se cancela por errores (02)');
    expect(within(dialog).getByLabelText('02 · Tiene errores (después emites otra para el reembolso)')).toBeChecked();
    expect(within(dialog).getAllByRole('radio')).toHaveLength(1);
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancelar nota de crédito' }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Cancelación de NC-1 pedida: el SAT espera la respuesta del cliente.'));
    expect(sent).toEqual({
      method: 'post',
      path: '/api/v1/invoicing/credit-notes/{creditNoteId}/cancel',
      params: { creditNoteId: 'nc-1' },
      body: { motive: CfdiCancellationMotive.ErrorsWithoutReplacement },
    });
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('cancels the credit note of a write-off with the reasons of a write-off', async () => {
    const forgiven: Note = { ...NC1, id: 'nc-3', number: 'NC-3', paymentForm: '15', total: 50, subTotal: 43.1, taxTotal: 6.9 };
    let sent: unknown;
    answer((_method, _path, options) => {
      sent = { path: options?.path, body: options?.body };
      return Promise.resolve({ ...forgiven, status: OrderInvoiceStatus.Cancelled });
    });
    renderList([{ ...WRITE_OFF, canIssue: false, awaitsAutomaticIssue: false, creditNotes: [forgiven] }], true);

    expect(screen.getByText(/Forma 15 \(Condonación\)/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Cancelar nota de crédito NC-3' }));
    const dialog = await screen.findByRole('dialog', { name: 'Cancelar la nota de crédito NC-3' });
    expect(within(dialog).getByLabelText('02 · Tiene errores (después emites otra para la condonación)')).toBeChecked();
    await userEvent.click(within(dialog).getByLabelText('03 · La condonación no se llevó a cabo'));
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancelar nota de crédito' }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Nota de crédito NC-3 cancelada.'));
    expect(sent).toEqual({ path: { creditNoteId: 'nc-3' }, body: { motive: CfdiCancellationMotive.NotCarriedOut } });
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('says which invoice an old credit note belonged to, and that the next ones wait for a stamped invoice', () => {
    const cancelledF3: Invoice = { ...F3, status: OrderInvoiceStatus.Cancelled };
    const old: Note = { ...NC1, status: OrderInvoiceStatus.Cancelled, cancellationMotive: CfdiCancellationMotive.ErrorsWithoutReplacement };
    renderList([{ ...REFUND, creditNotes: [old] }], false, [cancelledF3]);

    expect(screen.getByText('Lo que se devolvió o se condonó de este pedido. Sus notas de crédito se emiten con una factura timbrada.'))
      .toBeInTheDocument();
    expect(screen.getAllByRole('listitem')[0]).toHaveTextContent('la emitió TienditApp · de la factura F-3');
  });

  it('says why the API refused to issue it', async () => {
    answer(() => Promise.reject(Object.assign(new Error('400'), {
      isAxiosError: true,
      response: { status: 400, data: { detail: 'La nota de crédito NC-2 de esta condonación se está timbrando.' } },
    })));
    renderList([WRITE_OFF]);

    await userEvent.click(screen.getByRole('button', { name: /^Emitir ahora/ }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('La nota de crédito NC-2 de esta condonación se está timbrando.'));
  });
});
