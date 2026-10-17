import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from '../../api/typedClient';
import { OrderTrackingStep, OrderTrackingStepState, OrderType } from '../../types';
import OrderTrackingPage from './OrderTrackingPage';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

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
