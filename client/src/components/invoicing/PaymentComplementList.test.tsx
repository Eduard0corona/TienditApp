import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest, type Schemas } from '../../api/typedClient';
import { CfdiCancellationMotive, OrderInvoiceStatus, OrderPaymentMethod } from '../../types';
import PaymentComplementList from './PaymentComplementList';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
const toast = { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() };
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

type Payment = Schemas['OrderPaymentComplementsDto'];
type Complement = Schemas['PaymentComplementDto'];

const P1: Complement = {
  id: 'pc-1', orderInvoiceId: 'inv-1', orderPaymentId: 'pay-1', number: 'P-1', status: OrderInvoiceStatus.Stamped, paymentForm: '01',
  amount: 50, installment: 1, previousBalance: 200, remainingBalance: 150, requestedAt: '2026-10-04T17:05:00Z',
  issuedAutomatically: true, uuid: '9F3C1E2A-0B1D-4C5E-8F70-1A2B3C4D5E6F', stampedAt: '2026-10-04T17:05:03Z', hasXml: true, hasPdf: true,
  failureMessage: null, refusedAt: null, cancellationMotive: undefined, cancellationRequestedAt: null, cancelledAt: null,
};

const FIRST: Payment = {
  orderPaymentId: 'pay-1', paidAt: '2026-10-04T17:00:00Z', method: OrderPaymentMethod.Cash, amount: 50, dueBy: '2026-11-05',
  canIssue: false, problem: null, complements: [P1],
};

const SECOND: Payment = {
  orderPaymentId: 'pay-2', paidAt: '2026-10-20T18:00:00Z', method: OrderPaymentMethod.Transfer, amount: 150, dueBy: '2999-11-05',
  canIssue: true, problem: null, complements: [],
};

function renderList(payments: Payment[], canCancel = false) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <PaymentComplementList orderId="o-1" payments={payments} canCancel={canCancel} />
    </QueryClientProvider>,
  );
}

function answer(write: (method: string, path: string, options?: { path?: Record<string, string>; body?: unknown }) => Promise<unknown>) {
  vi.mocked(apiRequest).mockImplementation(write as unknown as typeof apiRequest);
}

describe('PaymentComplementList', () => {
  beforeEach(() => vi.resetAllMocks());

  it('shows nothing for an order without an invoice in installments', () => {
    const { container } = renderList([]);
    expect(container).toBeEmptyDOMElement();
  });

  it('shows each payment with its complement: installment, balances, folio fiscal and files', () => {
    renderList([FIRST]);

    const payment = screen.getAllByRole('listitem')[0];
    expect(within(payment).getByText('P-1')).toBeInTheDocument();
    expect(within(payment).getByText('Timbrado')).toBeInTheDocument();
    expect(payment).toHaveTextContent('Parcialidad 1 · pagó $50.00 de $200.00 · queda $150.00');
    expect(payment).toHaveTextContent('Forma 01 (Efectivo) · lo emitió TienditApp');
    expect(payment).toHaveTextContent('9F3C1E2A-0B1D-4C5E-8F70-1A2B3C4D5E6F');
    expect(screen.getByRole('button', { name: 'Descargar XML del complemento P-1' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Descargar PDF del complemento P-1' })).toBeInTheDocument();
    // Whoever manages the orders downloads; only owners and admins cancel. A payment with its complement issues no other.
    expect(screen.queryByRole('button', { name: /^Cancelar complemento/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Emitir/ })).not.toBeInTheDocument();
  });

  it('issues the complement of a payment before TienditApp does it on its own', async () => {
    let sent: unknown;
    answer((method, path, options) => {
      sent = { method, path, params: options?.path };
      return Promise.resolve({ ...P1, id: 'pc-2', orderPaymentId: 'pay-2', number: 'P-2', installment: 2, previousBalance: 150 });
    });
    renderList([FIRST, SECOND]);

    expect(screen.getByText('Sin complemento todavía. Emítelo a más tardar el 5 de noviembre de 2999.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /^Emitir ahora el complemento del cobro del/ }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Complemento P-2 timbrado.'));
    expect(sent).toEqual({ method: 'post', path: '/api/v1/invoicing/payments/{paymentId}/complements', params: { paymentId: 'pay-2' } });
  });

  it('says when the PAC refused it, and issues it again', async () => {
    const refused: Complement = {
      ...P1, status: OrderInvoiceStatus.Refused, uuid: null, stampedAt: null, hasXml: false, hasPdf: false,
      failureMessage: 'CFDI40102 - El resultado de la digestión debe ser igual al resultado de la desencripción del sello.',
    };
    answer(() => Promise.resolve({ ...refused, status: OrderInvoiceStatus.Stamping, failureMessage: null, number: 'P-2' }));
    renderList([{ ...FIRST, canIssue: true, dueBy: '2999-11-05', complements: [refused] }]);

    expect(screen.getByText('Rechazado')).toBeInTheDocument();
    expect(screen.getByText(/CFDI40102/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Descargar/ })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /^Emitir otra vez el complemento del cobro del/ }));

    await waitFor(() => expect(toast.info).toHaveBeenCalledWith('El PAC no respondió: TienditApp sigue pidiendo el timbre de P-2.'));
  });

  it('says why a payment has no complement, without offering to issue it', () => {
    renderList([{
      ...SECOND, canIssue: false, method: OrderPaymentMethod.Other,
      problem: 'Este cobro se registró como «Otro»: no tiene una forma de pago del SAT para su complemento.',
    }]);

    expect(screen.getByText('Este cobro se registró como «Otro»: no tiene una forma de pago del SAT para su complemento.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Emitir/ })).not.toBeInTheDocument();
    expect(screen.queryByText(/Emítelo a más tardar/)).not.toBeInTheDocument();
  });

  it('warns when the day to issue it went by', () => {
    renderList([{ ...SECOND, dueBy: '2020-02-05' }]);

    expect(screen.getByText('Su complemento venció el 5 de febrero de 2020: emítelo cuanto antes.')).toBeInTheDocument();
  });

  it('asks the PAC again for one that waits', async () => {
    const stamping: Complement = { ...P1, status: OrderInvoiceStatus.Stamping, uuid: null, stampedAt: null, hasXml: false, hasPdf: false };
    let sent: unknown;
    answer((method, path, options) => {
      sent = { method, path, params: options?.path };
      return Promise.resolve(P1);
    });
    renderList([{ ...FIRST, complements: [stamping] }]);

    await userEvent.click(screen.getByRole('button', { name: 'Volver a pedir el timbre del complemento P-1' }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Complemento P-1 timbrado.'));
    expect(sent).toEqual({ method: 'post', path: '/api/v1/invoicing/payment-complements/{complementId}/retry', params: { complementId: 'pc-1' } });
  });

  it('lets owners and admins cancel a stamped complement with its reason', async () => {
    let sent: unknown;
    answer((method, path, options) => {
      sent = { method, path, params: options?.path, body: options?.body };
      return Promise.resolve({ ...P1, status: OrderInvoiceStatus.Cancelled });
    });
    renderList([FIRST], true);

    await userEvent.click(screen.getByRole('button', { name: 'Cancelar complemento P-1' }));
    const dialog = await screen.findByRole('dialog', { name: 'Cancelar el complemento P-1' });
    await userEvent.click(within(dialog).getByLabelText('03 · El cobro no se llevó a cabo'));
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancelar complemento' }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Complemento P-1 cancelado.'));
    expect(sent).toEqual({
      method: 'post',
      path: '/api/v1/invoicing/payment-complements/{complementId}/cancel',
      params: { complementId: 'pc-1' },
      body: { motive: CfdiCancellationMotive.NotCarriedOut },
    });
  });

  it('says why the API refused to issue it', async () => {
    answer(() => Promise.reject(Object.assign(new Error('400'), {
      isAxiosError: true,
      response: { status: 400, data: { detail: 'El complemento P-2 de este cobro se está timbrando.' } },
    })));
    renderList([SECOND]);

    await userEvent.click(screen.getByRole('button', { name: /^Emitir ahora/ }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('El complemento P-2 de este cobro se está timbrando.'));
  });
});
