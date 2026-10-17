import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from '../../api/typedClient';
import { OrderStatus, OrderType } from '../../types';
import { readCart } from '../../utils/savedCart';
import MyOrders from './MyOrders';

// The title of the customer's frame only: its section tabs read the business, which a customer has none of.
vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

const request = vi.mocked(apiRequest);

function Store() {
  return <output data-testid="store">{useLocation().pathname}</output>;
}

describe('MyOrders', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    window.localStorage.clear();
  });

  it('fills the cart of the store with the order on «Pedir otra vez», and opens the store', async () => {
    request.mockResolvedValue({
      items: [{
        id: 'o1',
        orderNumber: 'T-1042',
        businessName: 'Café Luna',
        businessSlug: 'cafe-luna',
        canOrderAgain: true,
        status: OrderStatus.Delivered,
        orderType: OrderType.Retail,
        total: 118,
        paidAmount: 118,
        createdAt: '2026-10-12T19:00:00Z',
        items: [
          { productId: 'latte', productName: 'Latte', quantity: 2, optionIds: ['grande'] },
          { productId: 'queso', productName: 'Queso', quantity: 0.25, optionIds: [] },
        ],
      }],
      pageNumber: 1,
      totalPages: 1,
      totalCount: 1,
      hasPreviousPage: false,
      hasNextPage: false,
    } as never);
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/mis-pedidos']}>
          <Routes>
            <Route path="/mis-pedidos" element={<MyOrders />} />
            <Route path="/m/:slug" element={<Store />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    await userEvent.click(await screen.findByRole('button', { name: 'Pedir otra vez T-1042 de Café Luna' }));

    expect(screen.getByTestId('store')).toHaveTextContent('/m/cafe-luna');
    // The menu takes whole units: the cheese by weight does not come back, and the store says so.
    expect(readCart('cafe-luna')).toEqual(expect.objectContaining({
      businessName: 'Café Luna',
      lines: [{ productId: 'latte', optionIds: ['grande'], quantity: 2 }],
      reorderOf: 2,
    }));
  });
});
