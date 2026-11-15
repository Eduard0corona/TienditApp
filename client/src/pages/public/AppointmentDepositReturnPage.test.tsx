import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { OnlineCheckoutStatus, OnlinePaymentRefundStatus } from '../../api/generated/enums';
import { publicMenuApi, type AppointmentDepositStatus } from '../../api/publicMenuApi';
import { rememberOnlineCheckout } from '../../utils/onlineCheckouts';
import AppointmentDepositReturnPage from './AppointmentDepositReturnPage';

vi.mock('../../api/publicMenuApi', () => ({ publicMenuApi: { confirmAppointmentDeposit: vi.fn() } }));

const confirmDeposit = vi.mocked(publicMenuApi.confirmAppointmentDeposit);
const CHECKOUT = '4f9c8a52-7d1e-4c3b-9a6f-2e5d8b1c0a37';
const LINK = 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE';

function status(overrides: Partial<AppointmentDepositStatus> = {}): AppointmentDepositStatus {
  return {
    status: OnlineCheckoutStatus.AwaitingPayment,
    amount: 45,
    expiresAt: new Date(Date.now() + 20 * 60_000).toISOString(),
    businessName: 'Barbería Luis',
    contactPhone: '2221234567',
    checkoutUrl: 'https://www.mercadopago.com.mx/checkout/v1/redirect?pref_id=123',
    failureMessage: null,
    refundStatus: undefined,
    receipt: undefined,
    ...overrides,
  };
}

function receipt() {
  return {
    serviceName: 'Corte de cabello',
    startAt: '2026-10-05T16:00:00+00:00',
    endAt: '2026-10-05T16:45:00+00:00',
    dateLabel: 'lunes 5 de octubre',
    timeLabel: '10:00 a. m.',
    businessName: 'Barbería Luis',
    contactPhone: null,
    address: null,
    visit: undefined,
    staffName: null,
    linkedToAccount: false,
    trackingToken: LINK,
  };
}

function renderPage(search = '') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[`/m/barberia-luis/cita/pago/${CHECKOUT}${search}`]}>
        <Routes>
          <Route path="/m/:slug/cita/pago/:checkoutId" element={<AppointmentDepositReturnPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('AppointmentDepositReturnPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  afterEach(() => {
    window.localStorage.clear();
  });

  it('sends the payment of the return and the key this device kept, and shows the request booked with its link', async () => {
    rememberOnlineCheckout(CHECKOUT, 'llave-de-regreso');
    confirmDeposit.mockResolvedValue(status({ status: OnlineCheckoutStatus.Placed, checkoutUrl: null, receipt: receipt() }));

    renderPage('?collection_id=987654321&collection_status=approved');

    expect(await screen.findByRole('heading', { name: '¡Anticipo pagado!' })).toBeInTheDocument();
    expect(confirmDeposit).toHaveBeenCalledWith(
      'barberia-luis', CHECKOUT, { paymentId: '987654321', returnKey: 'llave-de-regreso' }, expect.anything());
    expect(screen.getByText('Corte de cabello')).toBeInTheDocument();
    expect(screen.getByText('Lunes 5 de octubre · 10:00 a. m.')).toBeInTheDocument();
    expect(screen.getByText('Anticipo pagado').nextSibling).toHaveTextContent('$45.00');
    // The link in the fragment, never in a URL a server keeps.
    expect(screen.getByRole('link', { name: 'Ver tu cita' })).toHaveAttribute('href', `/cita#${LINK}`);
    expect(screen.getByRole('button', { name: 'Copiar enlace de tu cita' })).toBeInTheDocument();
  });

  it('says it was booked without the link on another device', async () => {
    confirmDeposit.mockResolvedValue(status({ status: OnlineCheckoutStatus.Placed, checkoutUrl: null }));

    renderPage();

    expect(await screen.findByRole('heading', { name: '¡Anticipo pagado!' })).toBeInTheDocument();
    expect(confirmDeposit).toHaveBeenCalledWith('barberia-luis', CHECKOUT, { paymentId: null, returnKey: null }, expect.anything());
    expect(screen.queryByRole('link', { name: 'Ver tu cita' })).not.toBeInTheDocument();
    expect(screen.getByText(/el enlace de tu cita llega en ese mensaje/)).toBeInTheDocument();
  });

  it('says why a paid deposit could not book the appointment, and that it goes back', async () => {
    confirmDeposit.mockResolvedValue(status({
      status: OnlineCheckoutStatus.NotPlaced, checkoutUrl: null, failureMessage: 'Esa hora ya no está libre. Elige otra.',
      refundStatus: OnlinePaymentRefundStatus.Completed,
    }));

    renderPage();

    expect(await screen.findByRole('heading', { name: 'No pudimos reservar tu cita' })).toBeInTheDocument();
    expect(screen.getByText('Esa hora ya no está libre. Elige otra.')).toBeInTheDocument();
    expect(screen.getByText(/Ya te devolvimos tu anticipo completo/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Elegir otra hora' })).toHaveAttribute('href', '/m/barberia-luis/agendar');
  });

  it('offers to pay again while there is time, and says nothing was charged once it ended', async () => {
    confirmDeposit.mockResolvedValueOnce(status());
    renderPage('?collection_status=rejected');

    expect(await screen.findByRole('heading', { name: 'Tu pago no se aprobó' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Intentar de nuevo' })).toHaveAttribute('href', status().checkoutUrl);
  });

  it('waits for the payment Mercado Pago approved without offering to pay again, and sends no id that is not one', async () => {
    confirmDeposit.mockResolvedValue(status());

    renderPage('?payment_id=no-es-un-pago&status=approved');

    // The same heading shows while it first asks: this line comes only with the server's answer.
    expect(await screen.findByText(/Mercado Pago ya lo aprobó/)).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Confirmando tu anticipo…' })).toBeInTheDocument();
    expect(confirmDeposit).toHaveBeenCalledWith('barberia-luis', CHECKOUT, { paymentId: null, returnKey: null }, expect.anything());
    expect(screen.queryByRole('link', { name: 'Pagar en Mercado Pago' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Intentar de nuevo' })).not.toBeInTheDocument();
  });

  it('says the time to pay ended, and nothing was charged', async () => {
    confirmDeposit.mockResolvedValue(status({ status: OnlineCheckoutStatus.Expired, checkoutUrl: null }));

    renderPage();

    expect(await screen.findByRole('heading', { name: 'Se acabó el tiempo para pagar' })).toBeInTheDocument();
    expect(screen.getByText(/no se te cobró nada/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Volver a agendar' })).toHaveAttribute('href', '/m/barberia-luis/agendar');
  });

  it('says a payment that is not of an appointment of this business was not found', async () => {
    confirmDeposit.mockRejectedValue({ isAxiosError: true, response: { status: 404, data: {} } });

    renderPage();

    expect(await screen.findByRole('heading', { name: 'No encontramos ese pago' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Intentar de nuevo' })).not.toBeInTheDocument();
  });
});
