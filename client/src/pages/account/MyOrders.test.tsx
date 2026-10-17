import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from '../../api/typedClient';
import { MyOrdersScope, OrderStatus, OrderTrackingStep, OrderType } from '../../types';
import { readCart, saveCart } from '../../utils/savedCart';
import MyOrders from './MyOrders';

// The title of the customer's frame only: its section tabs read the business, which a customer has none of.
vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

const request = vi.mocked(apiRequest);

function Store() {
  return <output data-testid="store">{useLocation().pathname}</output>;
}

type Line = { productId: string; productName: string; quantity: number; optionIds: string[] };

/** An order of Café Luna with these lines, delivered unless said. */
function order(items: Line[], overrides: Record<string, unknown> = {}) {
  return {
    id: 'o1',
    orderNumber: 'T-1042',
    businessName: 'Café Luna',
    businessSlug: 'cafe-luna',
    canOrderAgain: true,
    status: OrderStatus.Delivered,
    orderType: OrderType.Retail,
    step: OrderTrackingStep.Delivered,
    stepLabel: 'Entregado',
    total: 118,
    paidAmount: 118,
    createdAt: '2026-10-12T19:00:00Z',
    items,
    ...overrides,
  };
}

function pageOf(items: unknown[]) {
  return { items, pageNumber: 1, totalPages: 1, totalCount: items.length, hasPreviousPage: false, hasNextPage: false };
}

/** The account's orders: these in progress, and one before (Café Luna, with these lines). */
function answer(items: Line[], inProgress: unknown[] = []) {
  request.mockImplementation(((_method: string, _path: string, options: { query: { scope: number } }) =>
    Promise.resolve(options.query.scope === MyOrdersScope.InProgress ? pageOf(inProgress) : pageOf([order(items)]))) as never);
}

function renderOrders() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/mis-pedidos']}>
        <Routes>
          <Route path="/mis-pedidos" element={<MyOrders />} />
          <Route path="/m/:slug" element={<Store />} />
          <Route path="/mis-pedidos/:id" element={<Store />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('MyOrders', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    window.localStorage.clear();
  });

  it('fills the cart of the store with the order on «Pedir otra vez», and opens the store', async () => {
    answer([
      { productId: 'latte', productName: 'Latte', quantity: 2, optionIds: ['grande'] },
      { productId: 'queso', productName: 'Queso', quantity: 0.25, optionIds: [] },
    ]);

    renderOrders();
    await userEvent.click(await screen.findByRole('button', { name: 'Pedir otra vez T-1042 de Café Luna' }));

    expect(screen.getByTestId('store')).toHaveTextContent('/m/cafe-luna');
    // The menu takes whole units: the cheese by weight does not come back, and the store says so.
    expect(readCart('cafe-luna')).toEqual(expect.objectContaining({
      businessName: 'Café Luna',
      lines: [{ productId: 'latte', optionIds: ['grande'], quantity: 2 }],
      reorderOf: 2,
      skipped: 1,
    }));
  });

  it('opens the store as it is for an order all by weight, keeping the cart left there', async () => {
    saveCart('cafe-luna', { businessName: 'Café Luna', lines: [{ productId: 'concha', optionIds: [], quantity: 3 }] });
    answer([{ productId: 'queso', productName: 'Queso', quantity: 0.25, optionIds: [] }]);

    renderOrders();
    await userEvent.click(await screen.findByRole('button', { name: 'Pedir otra vez T-1042 de Café Luna' }));

    expect(screen.getByTestId('store')).toHaveTextContent('/m/cafe-luna');
    expect(readCart('cafe-luna')?.lines).toEqual([{ productId: 'concha', optionIds: [], quantity: 3 }]);
  });

  it('shows the orders in progress first, each with its step, and opens one on «Ver pedido»', async () => {
    answer([{ productId: 'latte', productName: 'Latte', quantity: 1, optionIds: [] }], [
      order([{ productId: 'tacos', productName: 'Tacos', quantity: 3, optionIds: [] }], {
        id: 'o2',
        orderNumber: 'T-1050',
        status: OrderStatus.InPreparation,
        step: OrderTrackingStep.Preparing,
        stepLabel: 'En preparación',
        paidAmount: 0,
      }),
    ]);

    renderOrders();

    const inProgress = await screen.findByRole('region', { name: 'En curso' });
    const past = screen.getByRole('region', { name: 'Anteriores' });
    expect(inProgress).toHaveTextContent('T-1050');
    expect(inProgress).toHaveTextContent('En preparación');
    expect(inProgress).toHaveTextContent('Por pagar');
    expect(past).toHaveTextContent('T-1042');
    expect(past).toHaveTextContent('Entregado');
    expect(request).toHaveBeenCalledWith('get', '/api/v1/me/orders', expect.objectContaining({
      query: { scope: MyOrdersScope.InProgress, pageNumber: 1, pageSize: 50 },
    }));

    await userEvent.click(screen.getByRole('link', { name: 'Ver pedido T-1050 de Café Luna' }));
    expect(screen.getByTestId('store')).toHaveTextContent('/mis-pedidos/o2');
  });

  it('says there are no orders when the account has none', async () => {
    request.mockResolvedValue(pageOf([]) as never);

    renderOrders();

    expect(await screen.findByText('Todavía no tienes pedidos')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'En curso' })).not.toBeInTheDocument();
  });
});
