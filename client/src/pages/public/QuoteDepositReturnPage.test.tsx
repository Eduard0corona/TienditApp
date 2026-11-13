import { render, screen } from '@testing-library/react';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { OnlineCheckoutStatus, OnlinePaymentRefundStatus } from '../../api/generated/enums';
import { publicQuoteApi, type QuoteDepositStatus } from '../../api/quotesApi';
import { rememberQuoteDeposit } from '../../utils/quoteDeposits';
import { CONFIRM_GIVE_UP_MS, CONFIRM_SLOW_REFRESH_MS } from './OnlinePaymentReturnPage';
import QuoteDepositReturnPage from './QuoteDepositReturnPage';

vi.mock('../../api/quotesApi', () => ({ publicQuoteApi: { confirmDeposit: vi.fn() } }));

const confirmDeposit = vi.mocked(publicQuoteApi.confirmDeposit);
const CHECKOUT = '4f9c8a52-7d1e-4c3b-9a6f-2e5d8b1c0a37';
const LINK = 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE';

function status(overrides: Partial<QuoteDepositStatus> = {}): QuoteDepositStatus {
  return {
    status: OnlineCheckoutStatus.AwaitingPayment,
    amount: 348,
    expiresAt: new Date(Date.now() + 20 * 60_000).toISOString(),
    checkoutUrl: 'https://www.mercadopago.com.mx/checkout/v1/redirect?pref_id=123',
    failureMessage: null,
    orderNumber: null,
    ...overrides,
  };
}

function renderPage(search = '') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[`/cotizacion/pago/${CHECKOUT}${search}`]}>
        <Routes>
          <Route path="/cotizacion/pago/:checkoutId" element={<QuoteDepositReturnPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('QuoteDepositReturnPage', () => {
  beforeEach(() => vi.resetAllMocks());
  afterEach(() => {
    window.localStorage.clear();
    vi.useRealTimers();
  });

  it('sends the link this device kept and the payment of the return, and shows the quote accepted with its order (Fase 31N)', async () => {
    rememberQuoteDeposit(CHECKOUT, LINK);
    confirmDeposit.mockResolvedValue(status({ status: OnlineCheckoutStatus.Placed, checkoutUrl: null, orderNumber: 'ORD-000042' }));

    renderPage('?payment_id=1234567890&status=approved');

    expect(await screen.findByRole('heading', { name: '¡Anticipo pagado!' })).toBeInTheDocument();
    expect(confirmDeposit).toHaveBeenCalledWith(LINK, CHECKOUT, '1234567890', expect.anything());
    expect(screen.getByText('ORD-000042')).toBeInTheDocument();
    expect(screen.getByText('$348.00')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver tu cotización' })).toHaveAttribute('href', `/cotizacion#${LINK}`);
  });

  it('asks nothing without the link: the quote says how it went', () => {
    renderPage('?payment_id=1234567890');

    expect(screen.getByRole('heading', { name: 'Abre otra vez el enlace de tu cotización' })).toBeInTheDocument();
    expect(confirmDeposit).not.toHaveBeenCalled();
  });

  it('says why a paid deposit did not accept the quote and that the money goes back', async () => {
    rememberQuoteDeposit(CHECKOUT, LINK);
    confirmDeposit.mockResolvedValue(status({
      status: OnlineCheckoutStatus.NotPlaced,
      checkoutUrl: null,
      failureMessage: 'Esta cotización ya tiene respuesta.',
      refundStatus: OnlinePaymentRefundStatus.Completed,
    }));

    renderPage();

    expect(await screen.findByRole('heading', { name: 'Tu pago no aceptó la cotización' })).toBeInTheDocument();
    expect(screen.getByText('Esta cotización ya tiene respuesta.')).toBeInTheDocument();
    expect(screen.getByText(/Ya te devolvimos tu pago completo/)).toBeInTheDocument();
  });

  it('offers to pay while there is time, and ignores a payment id that is not one', async () => {
    rememberQuoteDeposit(CHECKOUT, LINK);
    confirmDeposit.mockResolvedValue(status());

    renderPage('?payment_id=<script>');

    expect(await screen.findByRole('heading', { name: 'Aún no recibimos tu pago' })).toBeInTheDocument();
    expect(confirmDeposit).toHaveBeenCalledWith(LINK, CHECKOUT, null, expect.anything());
    expect(screen.getByRole('link', { name: 'Pagar en Mercado Pago' })).toHaveAttribute('href', status().checkoutUrl);
  });

  it('confirms an approved return without offering to pay again, and stops asking when the wait ends', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    rememberQuoteDeposit(CHECKOUT, LINK);
    confirmDeposit.mockResolvedValue(status());

    renderPage('?payment_id=1234567890&collection_status=approved');

    expect(await screen.findByText('Mercado Pago ya lo aprobó; en un momento tu cotización queda aceptada.')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Pagar en Mercado Pago' })).not.toBeInTheDocument();
    await vi.advanceTimersByTimeAsync(CONFIRM_GIVE_UP_MS + CONFIRM_SLOW_REFRESH_MS);
    const asked = confirmDeposit.mock.calls.length;
    await vi.advanceTimersByTimeAsync(CONFIRM_SLOW_REFRESH_MS * 5);
    expect(confirmDeposit).toHaveBeenCalledTimes(asked);
  });

  it('says it did not find a payment that is not of the version of the link', async () => {
    rememberQuoteDeposit(CHECKOUT, LINK);
    confirmDeposit.mockRejectedValue(new AxiosError('Not found', 'ERR_BAD_REQUEST', undefined, undefined,
      { status: 404, data: {}, statusText: 'Not Found', headers: {}, config: { headers: new AxiosHeaders() } } as AxiosResponse));

    renderPage();

    expect(await screen.findByRole('heading', { name: 'No encontramos ese pago' })).toBeInTheDocument();
  });
});
