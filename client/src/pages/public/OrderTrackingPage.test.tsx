import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from '../../api/typedClient';
import { OrderTrackingStep, OrderTrackingStepState, OrderType } from '../../types';
import OrderTrackingPage from './OrderTrackingPage';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
const dialogs = vi.hoisted(() => ({ confirm: vi.fn() }));
const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => dialogs }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

const request = vi.mocked(apiRequest);
const LINK = 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE';

function tracking(overrides: Record<string, unknown> = {}) {
  return {
    orderNumber: 'ORD-9',
    businessName: 'Café Luna',
    businessSlug: 'cafe-luna',
    businessContactPhone: null,
    orderType: OrderType.Retail,
    createdAt: '2026-10-12T19:00:00Z',
    step: OrderTrackingStep.Confirmed,
    stepLabel: 'Confirmado',
    steps: [
      { step: OrderTrackingStep.Received, label: 'Recibido', state: OrderTrackingStepState.Done, at: '2026-10-12T19:00:00Z' },
      { step: OrderTrackingStep.Confirmed, label: 'Confirmado', state: OrderTrackingStepState.Current, at: '2026-10-12T19:02:00Z' },
      { step: OrderTrackingStep.Delivered, label: 'Entregado', state: OrderTrackingStepState.Upcoming, at: null },
    ],
    inProgress: true,
    toAddress: false,
    courierTrackingUrl: null,
    items: [{ productId: 'latte', productName: 'Latte', quantity: 1, amount: 55, notes: null, options: [], optionIds: [] }],
    deliveryFee: null,
    pricesIncludeTax: true,
    tax: 7.59,
    total: 55,
    paidAmount: 55,
    cancellationReason: null,
    canCancel: false,
    ...overrides,
  };
}

function renderPage(url: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[url]}>
        <Routes>
          <Route path="/seguimiento" element={<OrderTrackingPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('OrderTrackingPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('says the link expired while the page was open, keeps what it saw, and asks no more', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    request.mockResolvedValueOnce(tracking() as never).mockRejectedValue({
      isAxiosError: true,
      response: { status: 404, data: { detail: 'No encontramos ese pedido. El enlace pudo haber vencido.' } },
    });

    renderPage(`/seguimiento#${LINK}`);
    await screen.findByRole('heading', { name: 'Café Luna' });
    await vi.advanceTimersByTimeAsync(20_000);

    expect(await screen.findByText(/El enlace pudo haber vencido\. Lo de abajo es lo último que vimos\./)).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Café Luna' })).toBeInTheDocument();
    await vi.advanceTimersByTimeAsync(60_000);
    expect(request).toHaveBeenCalledTimes(2);
  });

  it('sends the link of the fragment in the body, and shows where the order is', async () => {
    request.mockResolvedValue(tracking() as never);

    renderPage(`/seguimiento#${LINK}`);

    expect(await screen.findByRole('heading', { name: 'Café Luna' })).toBeInTheDocument();
    expect(request).toHaveBeenCalledWith('post', '/api/v1/public/orders/tracking', expect.objectContaining({ body: { token: LINK } }));
    expect(screen.getByText(/Folio ORD-9/)).toBeInTheDocument();
    expect(screen.getByRole('listitem', { current: 'step' })).toHaveTextContent('Confirmado');
    expect(screen.getByText('Pagado')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Ver el menú/ })).toHaveAttribute('href', '/m/cafe-luna');
    // Without a phone of the business there is nobody to write to.
    expect(screen.queryByRole('link', { name: /Escribir al negocio/ })).not.toBeInTheDocument();
  });

  it('cancels an order the business has not accepted, with the link in the body, once the customer confirms (Fase 29R)', async () => {
    const user = userEvent.setup();
    const waiting = tracking({ step: OrderTrackingStep.Received, stepLabel: 'Recibido', paidAmount: 0, canCancel: true });
    const cancelled = tracking({
      step: OrderTrackingStep.Cancelled, stepLabel: 'Cancelado', inProgress: false, canCancel: false,
      cancellationReason: 'Lo canceló el cliente.', paidAmount: 0,
    });
    request.mockImplementation(async (_method: string, path: string) => (path.endsWith('/cancel') ? cancelled : waiting) as never);
    dialogs.confirm.mockResolvedValueOnce(false).mockResolvedValueOnce(true);
    renderPage(`/seguimiento#${LINK}`);

    await user.click(await screen.findByRole('button', { name: 'Cancelar pedido' }));
    expect(request).not.toHaveBeenCalledWith('post', '/api/v1/public/orders/tracking/cancel', expect.anything());
    expect(dialogs.confirm).toHaveBeenCalledWith(expect.objectContaining({ title: 'Cancelar pedido', danger: true }));
    await user.click(screen.getByRole('button', { name: 'Cancelar pedido' }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Cancelaste tu pedido ORD-9.'));
    expect(request).toHaveBeenCalledWith('post', '/api/v1/public/orders/tracking/cancel', { body: { token: LINK } });
    expect(screen.getByText('Lo canceló el cliente.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancelar pedido' })).not.toBeInTheDocument();
  });

  it('offers no cancelling once the business accepted it (Fase 29R)', async () => {
    request.mockResolvedValueOnce(tracking() as never);
    renderPage(`/seguimiento#${LINK}`);
    await screen.findByRole('heading', { name: 'Café Luna' });
    expect(screen.queryByRole('button', { name: 'Cancelar pedido' })).not.toBeInTheDocument();
  });

  it('says why it was not cancelled when the business accepted it meanwhile, and reads the order again (Fase 29R)', async () => {
    const user = userEvent.setup();
    const refused = {
      isAxiosError: true,
      response: { status: 422, data: { detail: 'El negocio ya aceptó tu pedido: para cancelarlo, llámale.' } },
    };
    request.mockImplementation(async (_method: string, path: string) => {
      if (path.endsWith('/cancel')) throw refused;
      return tracking({ canCancel: request.mock.calls.length < 2 }) as never;
    });
    dialogs.confirm.mockResolvedValue(true);
    renderPage(`/seguimiento#${LINK}`);

    await user.click(await screen.findByRole('button', { name: 'Cancelar pedido' }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('El negocio ya aceptó tu pedido: para cancelarlo, llámale.'));
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Cancelar pedido' })).not.toBeInTheDocument());
    expect(request.mock.calls.filter(([, path]) => path === '/api/v1/public/orders/tracking')).toHaveLength(2);
  });

  it('asks for the link of the receipt when the address has none, without asking the server', async () => {
    renderPage('/seguimiento');

    expect(await screen.findByRole('heading', { name: 'Abre el enlace de tu recibo' })).toBeInTheDocument();
    expect(request).not.toHaveBeenCalled();
  });

  it('says so when the link is unknown or expired', async () => {
    request.mockRejectedValue({
      isAxiosError: true,
      response: { status: 404, data: { detail: 'No encontramos ese pedido. El enlace pudo haber vencido.' } },
    });

    renderPage(`/seguimiento#${LINK}`);

    expect(await screen.findByText('No encontramos ese pedido. El enlace pudo haber vencido.')).toBeInTheDocument();
  });
});
