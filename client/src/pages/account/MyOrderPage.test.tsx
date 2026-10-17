import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from '../../api/typedClient';
import { OrderStatus, OrderTrackingStep, OrderTrackingStepState, OrderType } from '../../types';
import { readCart } from '../../utils/savedCart';
import MyOrderPage from './MyOrderPage';

vi.mock('../../components/common/PageTitle', () => ({
  default: ({ title, parent }: { title: string; parent?: { label: string } }) => <h1>{parent?.label} › {title}</h1>,
}));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

const request = vi.mocked(apiRequest);

function Store() {
  return <output data-testid="store">{useLocation().pathname}</output>;
}

function detail(overrides: Record<string, unknown> = {}) {
  return {
    id: 'o1',
    orderNumber: 'T-1042',
    businessName: 'Café Luna',
    businessSlug: 'cafe-luna',
    businessContactPhone: '2221234567',
    canOrderAgain: true,
    status: OrderStatus.Ready,
    orderType: OrderType.Retail,
    createdAt: '2026-10-12T19:00:00Z',
    step: OrderTrackingStep.OnTheWay,
    stepLabel: 'En camino',
    steps: [
      { step: OrderTrackingStep.Received, label: 'Recibido', state: OrderTrackingStepState.Done, at: '2026-10-12T19:00:00Z' },
      { step: OrderTrackingStep.OnTheWay, label: 'En camino', state: OrderTrackingStepState.Current, at: '2026-10-12T19:30:00Z' },
      { step: OrderTrackingStep.Delivered, label: 'Entregado', state: OrderTrackingStepState.Upcoming, at: null },
    ],
    inProgress: true,
    courierTrackingUrl: 'https://paquetenvia.example/rastreo/123',
    items: [
      { productId: 'latte', productName: 'Latte', quantity: 2, amount: 110, notes: 'Sin azúcar', options: ['Grande'], optionIds: ['grande'] },
    ],
    deliveryFee: 35,
    pricesIncludeTax: true,
    tax: 20.01,
    total: 145,
    paidAmount: 0,
    cancellationReason: null,
    tableNumber: null,
    delivery: { postalCode: '72000', addressLine: 'Av. Juárez 120', references: 'Portón verde' },
    ...overrides,
  };
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/mis-pedidos/o1']}>
        <Routes>
          <Route path="/mis-pedidos/:id" element={<MyOrderPage />} />
          <Route path="/m/:slug" element={<Store />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('MyOrderPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    window.localStorage.clear();
  });

  it('shows where the order is, what it carries and where it goes', async () => {
    request.mockResolvedValue(detail() as never);

    renderPage();

    expect(await screen.findByRole('heading', { name: 'Mis pedidos › Pedido T-1042' })).toBeInTheDocument();
    expect(request).toHaveBeenCalledWith('get', '/api/v1/me/orders/{id}', expect.objectContaining({ path: { id: 'o1' } }));
    expect(screen.getAllByText('En camino')).not.toHaveLength(0);
    expect(screen.getByRole('list', { name: 'Seguimiento del pedido' })).toBeInTheDocument();
    expect(screen.getByText('Grande')).toBeInTheDocument();
    expect(screen.getByText('Sin azúcar')).toBeInTheDocument();
    expect(screen.getByText('Por pagar')).toBeInTheDocument();
    expect(screen.getByText(/Av\. Juárez 120, CP 72000/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Seguir el envío con la paquetería/ }))
      .toHaveAttribute('href', 'https://paquetenvia.example/rastreo/123');
    expect(screen.getByRole('link', { name: /Escribir al negocio/ }).getAttribute('href')).toMatch(/^https:\/\/wa\.me\/522221234567\?text=/);
    // Prices with IVA included: no line of IVA.
    expect(screen.queryByText('IVA')).not.toBeInTheDocument();
  });

  it('fills the store\'s cart on «Pedir otra vez»', async () => {
    request.mockResolvedValue(detail({ inProgress: false }) as never);

    renderPage();
    await userEvent.click(await screen.findByRole('button', { name: 'Pedir otra vez T-1042 de Café Luna' }));

    expect(screen.getByTestId('store')).toHaveTextContent('/m/cafe-luna');
    expect(readCart('cafe-luna')?.lines).toEqual([{ productId: 'latte', optionIds: ['grande'], quantity: 2 }]);
  });

  it('says why a cancelled order was cancelled, and asks for nothing', async () => {
    request.mockResolvedValue(detail({
      step: OrderTrackingStep.Cancelled,
      stepLabel: 'Cancelado',
      inProgress: false,
      cancellationReason: 'Se nos acabó el pan',
      courierTrackingUrl: null,
      steps: [
        { step: OrderTrackingStep.Received, label: 'Recibido', state: OrderTrackingStepState.Done, at: '2026-10-12T19:00:00Z' },
        { step: OrderTrackingStep.Cancelled, label: 'Cancelado', state: OrderTrackingStepState.Current, at: '2026-10-12T19:10:00Z' },
      ],
    }) as never);

    renderPage();

    expect(await screen.findByText('Se nos acabó el pan')).toBeInTheDocument();
    expect(screen.queryByText('Por pagar')).not.toBeInTheDocument();
  });

  it('says so when the order is not the account\'s', async () => {
    request.mockRejectedValue({ isAxiosError: true, response: { status: 404, data: { detail: 'No encontramos ese pedido en tu cuenta.' } } });

    renderPage();

    expect(await screen.findByText(/No encontramos ese pedido en tu cuenta\./)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver mis pedidos' })).toHaveAttribute('href', '/mis-pedidos');
  });
});
