import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from '../../api/typedClient';
import { MyOrdersScope, OrderStatus, OrderTrackingStep, OrderType } from '../../types';
import { readCart, saveCart } from '../../utils/savedCart';
import MyOrders from './MyOrders';

// The title of the customer's frame only: its section tabs read the business, which a customer has none of.
vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

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

const MY_REVIEWS = '/api/v1/me/reviews';

/** The account's orders: these in progress, and one before (Café Luna, with these lines); no reviews. */
function answer(items: Line[], inProgress: unknown[] = []) {
  request.mockImplementation(((_method: string, path: string, options: { query: { scope: number } }) => {
    if (path === MY_REVIEWS) return Promise.resolve([]);
    return Promise.resolve(options.query.scope === MyOrdersScope.InProgress ? pageOf(inProgress) : pageOf([order(items)]));
  }) as never);
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

  afterEach(() => {
    vi.useRealTimers();
  });

  it('asks again for the ones before when an order leaves «En curso», and stops asking with none in progress', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const tacos = order([{ productId: 'tacos', productName: 'Tacos', quantity: 3, optionIds: [] }], {
      id: 'o2', orderNumber: 'T-1050', status: OrderStatus.InPreparation, step: OrderTrackingStep.Preparing, stepLabel: 'En preparación',
    });
    const latte = order([{ productId: 'latte', productName: 'Latte', quantity: 1, optionIds: [] }]);
    const delivered = { ...tacos, status: OrderStatus.Delivered, step: OrderTrackingStep.Delivered, stepLabel: 'Entregado' };
    const calls = { inProgress: 0, past: 0 };
    request.mockImplementation(((_method: string, path: string, options: { query: { scope: number } }) => {
      if (path === MY_REVIEWS) return Promise.resolve([]);
      if (options.query.scope === MyOrdersScope.InProgress) {
        calls.inProgress += 1;
        return Promise.resolve(pageOf(calls.inProgress === 1 ? [tacos] : []));
      }
      calls.past += 1;
      return Promise.resolve(pageOf(calls.past === 1 ? [latte] : [delivered, latte]));
    }) as never);

    renderOrders();
    expect(await screen.findByRole('region', { name: 'En curso' })).toHaveTextContent('T-1050');
    // The business delivers the tacos: the next ask finds nothing in progress.
    await vi.advanceTimersByTimeAsync(20_000);

    await waitFor(() => expect(calls.past).toBe(2));
    expect(await screen.findByText('T-1050', { exact: false })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'En curso' })).not.toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Anteriores' })).toHaveTextContent('T-1050');
    await vi.advanceTimersByTimeAsync(60_000);
    expect(calls.inProgress).toBe(2);
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
    request.mockImplementation(((_method: string, path: string) => Promise.resolve(path === MY_REVIEWS ? [] : pageOf([]))) as never);

    renderOrders();

    expect(await screen.findByText('Todavía no tienes pedidos')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'En curso' })).not.toBeInTheDocument();
  });

  it('rates the business of an order received, and says «Tu reseña» where the account already rated it', async () => {
    const latte = [{ productId: 'latte', productName: 'Latte', quantity: 1, optionIds: [] }];
    request.mockImplementation(((method: string, path: string, options: { query: { scope: number } }) => {
      if (path === MY_REVIEWS) {
        return Promise.resolve([{ slug: 'taqueria-sol', businessName: 'Taquería Sol', rating: 5, writtenAt: '2026-10-01T18:00:00Z', hidden: false }]);
      }
      if (path === '/api/v1/me/reviews/{slug}') {
        return Promise.resolve(method === 'get' ? { canReview: true, review: null } : {});
      }
      return Promise.resolve(options.query.scope === MyOrdersScope.InProgress
        ? pageOf([order(latte, { id: 'o9', orderNumber: 'T-1060', status: OrderStatus.Ready, step: OrderTrackingStep.Ready, stepLabel: 'Listo' })])
        : pageOf([order(latte), order(latte, { id: 'o3', orderNumber: 'T-7', businessName: 'Taquería Sol', businessSlug: 'taqueria-sol' })]));
    }) as never);

    renderOrders();

    expect(await screen.findByRole('button', { name: 'Tu reseña de Taquería Sol' })).toBeInTheDocument();
    // An order not received yet does not rate its business.
    expect(within(screen.getByRole('region', { name: 'En curso' })).queryByRole('button', { name: /Calificar/ })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Calificar a Café Luna' }));
    const dialog = await screen.findByRole('dialog', { name: 'Califica a Café Luna' });
    await userEvent.click(within(dialog).getByRole('radio', { name: '4 estrellas' }));
    await userEvent.type(within(dialog).getByLabelText('Cuéntanos cómo te fue (opcional)'), '  Muy rico  ');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Publicar' }));

    await waitFor(() => expect(request).toHaveBeenCalledWith('put', '/api/v1/me/reviews/{slug}', {
      path: { slug: 'cafe-luna' },
      body: { rating: 4, comment: 'Muy rico' },
    }));
    expect(toast.success).toHaveBeenCalledWith('Publicamos tu reseña de Café Luna.');
  });
});
