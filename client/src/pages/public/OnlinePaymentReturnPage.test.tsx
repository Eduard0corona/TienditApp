import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { OnlineCheckoutStatus, OnlinePaymentRefundStatus } from '../../api/generated/enums';
import { publicMenuApi, type OnlineCheckoutStatus as CheckoutStatus } from '../../api/publicMenuApi';
import { rememberOnlineCheckout } from '../../utils/onlineCheckouts';
import { readCart, saveCart } from '../../utils/savedCart';
import OnlinePaymentReturnPage, { CONFIRM_REFRESH_MS } from './OnlinePaymentReturnPage';

vi.mock('../../api/publicMenuApi', () => ({ publicMenuApi: { confirmOnlineCheckout: vi.fn() } }));

const confirmCheckout = vi.mocked(publicMenuApi.confirmOnlineCheckout);
const CHECKOUT = '4f9c8a52-7d1e-4c3b-9a6f-2e5d8b1c0a37';
const LINK = 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE';

function status(overrides: Partial<CheckoutStatus> = {}): CheckoutStatus {
  return {
    status: OnlineCheckoutStatus.AwaitingPayment,
    amount: 145,
    expiresAt: new Date(Date.now() + 20 * 60_000).toISOString(),
    businessName: 'Café Luna',
    contactPhone: '2221234567',
    checkoutUrl: 'https://www.mercadopago.com.mx/checkout/v1/redirect?pref_id=123',
    orderNumber: null,
    confirmed: false,
    linkedToAccount: false,
    trackingToken: null,
    failureMessage: null,
    ...overrides,
  };
}

function renderPage(search = '') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[`/m/cafe-luna/pago/${CHECKOUT}${search}`]}>
        <Routes>
          <Route path="/m/:slug/pago/:checkoutId" element={<OnlinePaymentReturnPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('OnlinePaymentReturnPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  afterEach(() => {
    vi.useRealTimers();
    window.localStorage.clear();
  });

  it('sends the payment of the return and the key this device kept, and shows the placed order with its link', async () => {
    rememberOnlineCheckout(CHECKOUT, 'llave-de-regreso');
    saveCart('cafe-luna', { businessName: 'Café Luna', lines: [{ productId: 'p1', optionIds: [], quantity: 2 }] });
    confirmCheckout.mockResolvedValue(status({
      status: OnlineCheckoutStatus.Placed, orderNumber: 'ORD-12', checkoutUrl: null, trackingToken: LINK,
    }));

    renderPage(`?collection_id=987654321&collection_status=approved&external_reference=${CHECKOUT}`);

    expect(await screen.findByRole('heading', { name: '¡Pago recibido!' })).toBeInTheDocument();
    expect(confirmCheckout).toHaveBeenCalledWith('cafe-luna', CHECKOUT, { paymentId: '987654321', returnKey: 'llave-de-regreso' }, expect.anything());
    expect(screen.getByText('ORD-12')).toBeInTheDocument();
    expect(screen.getByText('Pagado en línea').nextSibling).toHaveTextContent('$145.00');
    expect(screen.getByText('Tu pedido ya está con Café Luna; lo confirmará en unos minutos.')).toBeInTheDocument();
    // The link in the fragment, never in a URL a server keeps (ADR 0023).
    expect(screen.getByRole('link', { name: 'Seguir tu pedido' })).toHaveAttribute('href', `/seguimiento#${LINK}`);
    expect(screen.getByRole('button', { name: 'Copiar enlace de seguimiento' })).toBeInTheDocument();
    // The cart this device kept for the store was ordered.
    expect(readCart('cafe-luna')).toBeNull();
  });

  it('sends no payment that is not Mercado Pago\'s number, and no key from another device', async () => {
    confirmCheckout.mockResolvedValue(status({ status: OnlineCheckoutStatus.Placed, orderNumber: 'ORD-12', confirmed: true }));

    renderPage('?payment_id=null&status=approved');

    expect(await screen.findByText('Café Luna ya está preparando tu pedido.')).toBeInTheDocument();
    expect(confirmCheckout).toHaveBeenCalledWith('cafe-luna', CHECKOUT, { paymentId: null, returnKey: null }, expect.anything());
    // Without the key there is no link: WhatsApp tells how it goes.
    expect(screen.queryByRole('link', { name: 'Seguir tu pedido' })).not.toBeInTheDocument();
    expect(screen.getByText('Te avisamos por WhatsApp cómo va tu pedido.')).toBeInTheDocument();
  });

  it('takes a customer account to its orders', async () => {
    confirmCheckout.mockResolvedValue(status({
      status: OnlineCheckoutStatus.Placed, orderNumber: 'ORD-12', linkedToAccount: true, trackingToken: LINK,
    }));

    renderPage();

    expect(await screen.findByRole('link', { name: 'Ver mis pedidos' })).toHaveAttribute('href', '/mis-pedidos');
    expect(screen.getByText('Lo encuentras en tus pedidos.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Copiar enlace de seguimiento' })).not.toBeInTheDocument();
  });

  it('says why the order could not be placed and how the money goes back', async () => {
    confirmCheckout.mockResolvedValue(status({
      status: OnlineCheckoutStatus.NotPlaced,
      checkoutUrl: null,
      failureMessage: 'Café Luna dejó de recibir pedidos en línea antes de que se aprobara tu pago.',
      refundStatus: OnlinePaymentRefundStatus.Requested,
    }));

    renderPage('?collection_status=approved');

    expect(await screen.findByRole('heading', { name: 'No pudimos enviar tu pedido' })).toBeInTheDocument();
    expect(screen.getByText('Café Luna dejó de recibir pedidos en línea antes de que se aprobara tu pago.')).toBeInTheDocument();
    expect(screen.getByText('Te devolvemos tu pago completo a tu tarjeta o cuenta de Mercado Pago.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Escribir al negocio/ }).getAttribute('href')).toMatch(/^https:\/\/wa\.me\/522221234567\?text=/);
    expect(screen.getByRole('link', { name: 'Volver al menú' })).toHaveAttribute('href', '/m/cafe-luna');
  });

  it('says the money is back once Mercado Pago gave it back', async () => {
    confirmCheckout.mockResolvedValueOnce(status({ status: OnlineCheckoutStatus.NotPlaced, refundStatus: OnlinePaymentRefundStatus.Completed }));
    renderPage();
    expect(await screen.findByText(/Ya te devolvimos tu pago completo/)).toBeInTheDocument();
  });

  it('tells a refused refund to write to the business', async () => {
    confirmCheckout.mockResolvedValueOnce(status({ status: OnlineCheckoutStatus.NotPlaced, refundStatus: OnlinePaymentRefundStatus.Failed }));
    renderPage();
    expect(await screen.findByText('Café Luna te devolverá tu pago. Si tienes dudas, escríbele.')).toBeInTheDocument();
  });

  it('says the time to pay ended with nothing charged', async () => {
    confirmCheckout.mockResolvedValue(status({ status: OnlineCheckoutStatus.Expired, checkoutUrl: null }));

    renderPage();

    expect(await screen.findByRole('heading', { name: 'Se acabó el tiempo para pagar' })).toBeInTheDocument();
    expect(screen.getByText(/no se te cobró nada/)).toBeInTheDocument();
  });

  it('keeps asking while Mercado Pago confirms an approved payment, until the order is placed', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    confirmCheckout.mockResolvedValueOnce(status())
      .mockResolvedValue(status({ status: OnlineCheckoutStatus.Placed, orderNumber: 'ORD-12', checkoutUrl: null }));

    renderPage('?payment_id=987654321&status=approved');

    expect(await screen.findByText('Mercado Pago ya lo aprobó; en un momento enviamos tu pedido a Café Luna.')).toBeInTheDocument();
    // Approved: no second payment is offered while it is confirmed.
    expect(screen.queryByRole('link', { name: 'Pagar en Mercado Pago' })).not.toBeInTheDocument();
    await vi.advanceTimersByTimeAsync(CONFIRM_REFRESH_MS);

    expect(await screen.findByRole('heading', { name: '¡Pago recibido!' })).toBeInTheDocument();
    await vi.advanceTimersByTimeAsync(CONFIRM_REFRESH_MS * 3);
    expect(confirmCheckout).toHaveBeenCalledTimes(2);
  });

  it('offers to pay again after a rejected payment, while there is time', async () => {
    confirmCheckout.mockResolvedValue(status());

    renderPage('?collection_status=rejected');

    expect(await screen.findByRole('heading', { name: 'Tu pago no se aprobó' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Intentar de nuevo' }))
      .toHaveAttribute('href', 'https://www.mercadopago.com.mx/checkout/v1/redirect?pref_id=123');
    expect(screen.getByText('Total').nextSibling).toHaveTextContent('$145.00');
  });

  it('offers to pay when the customer came back without paying, but not once the time is over', async () => {
    confirmCheckout.mockResolvedValue(status({ expiresAt: new Date(Date.now() - 60_000).toISOString() }));

    renderPage();

    expect(await screen.findByRole('heading', { name: 'Aún no recibimos tu pago' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Pagar en Mercado Pago' })).not.toBeInTheDocument();
  });

  it('lets the customer try again when the server could not be reached', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const offline = new Error('Network Error');
    confirmCheckout.mockRejectedValueOnce(offline).mockRejectedValueOnce(offline).mockRejectedValueOnce(offline)
      .mockResolvedValue(status());

    renderPage();
    // It asks twice more on its own before saying so.
    await vi.advanceTimersByTimeAsync(3_000);

    expect(await screen.findByRole('heading', { name: 'No pudimos revisar tu pago' })).toBeInTheDocument();
    expect(confirmCheckout).toHaveBeenCalledTimes(3);
    await userEvent.click(screen.getByRole('button', { name: 'Intentar de nuevo' }));
    expect(await screen.findByRole('link', { name: 'Pagar en Mercado Pago' })).toBeInTheDocument();
  });

  it('says it does not know a payment the server does not find', async () => {
    confirmCheckout.mockRejectedValue({ isAxiosError: true, response: { status: 404, data: { detail: 'No encontrado.' } } });

    renderPage();

    expect(await screen.findByRole('heading', { name: 'No encontramos ese pago' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Intentar de nuevo' })).not.toBeInTheDocument();
  });
});
