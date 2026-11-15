import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { accountApi } from '../api/accountApi';
import { AuthContext, type AuthContextType } from '../contexts/AuthContextDefinition';
import { readCart } from '../utils/savedCart';
import { ORDER_AGAIN_ACCOUNT_WAIT_MS, useOrderAgain } from './useOrderAgain';

vi.mock('../api/accountApi', () => ({ accountApi: { saveCart: vi.fn() } }));

const account = vi.mocked(accountApi);

/** A customer account, signed in. */
const customer = {
  isAuthenticated: true,
  isLoading: false,
  user: {
    id: 'u1', email: 'mariana@demo.com', firstName: 'Mariana', lastName: 'Ortiz', fullName: 'Mariana Ortiz',
    phoneNumber: null, phoneNumberConfirmed: false, emailConfirmed: true, role: 'Customer', permissions: [],
  },
} as unknown as AuthContextType;

/** No one signed in. */
const guest = { isAuthenticated: false, isLoading: false, user: null } as unknown as AuthContextType;

/** Two lattes and a quarter kilo of cheese, which the cart does not take. */
const order = {
  businessSlug: 'cafe-luna',
  businessName: 'Café Luna',
  items: [
    { productId: 'latte', quantity: 2, optionIds: ['grande'] },
    { productId: 'queso', quantity: 0.25 },
  ],
};

function Orders() {
  const orderAgain = useOrderAgain();
  return <button type="button" onClick={() => void orderAgain(order)}>Pedir otra vez</button>;
}

function Store() {
  return <output data-testid="store">{useLocation().pathname}</output>;
}

function renderOrders(auth: AuthContextType) {
  return render(
    <AuthContext.Provider value={auth}>
      <MemoryRouter initialEntries={['/mis-pedidos']}>
        <Routes>
          <Route path="/mis-pedidos" element={<Orders />} />
          <Route path="/m/:slug" element={<Store />} />
        </Routes>
      </MemoryRouter>
    </AuthContext.Provider>,
  );
}

describe('useOrderAgain', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    window.localStorage.clear();
  });
  afterEach(() => vi.useRealTimers());

  it('keeps the cart in the account first, with what did not come back, and opens the store once it is kept (Fase 31P)', async () => {
    let kept: (value: unknown) => void = () => undefined;
    account.saveCart.mockReturnValue(new Promise((resolve) => { kept = resolve; }) as never);
    renderOrders(customer);

    await userEvent.click(screen.getByRole('button', { name: 'Pedir otra vez' }));

    await waitFor(() => expect(account.saveCart).toHaveBeenCalledWith(
      'cafe-luna', { lines: [{ productId: 'latte', optionIds: ['grande'], quantity: 2 }], reorderOf: 2, skipped: 1 },
    ));
    // The store waits for the account: it would read the account's older cart.
    expect(screen.queryByTestId('store')).not.toBeInTheDocument();
    const savedAt = '2026-11-15T05:00:00.000Z';
    await act(async () => kept({ slug: 'cafe-luna', businessName: 'Café Luna', savedAt, lines: [], reorderOf: 2, skipped: 1 }));

    expect(await screen.findByTestId('store')).toHaveTextContent('/m/cafe-luna');
    // This device has it with the account's time.
    expect(readCart('cafe-luna')).toEqual(expect.objectContaining({ reorderOf: 2, skipped: 1, savedAt }));
  });

  it('opens the store after a moment when the account cannot keep it, and this device keeps the cart', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    account.saveCart.mockRejectedValue(new Error('offline'));
    renderOrders(customer);

    await userEvent.click(screen.getByRole('button', { name: 'Pedir otra vez' }));
    await waitFor(() => expect(account.saveCart).toHaveBeenCalled());
    expect(screen.queryByTestId('store')).not.toBeInTheDocument();
    await act(async () => { await vi.advanceTimersByTimeAsync(ORDER_AGAIN_ACCOUNT_WAIT_MS); });

    expect(screen.getByTestId('store')).toHaveTextContent('/m/cafe-luna');
    expect(readCart('cafe-luna')).toEqual(expect.objectContaining({ reorderOf: 2, skipped: 1 }));
  });

  it('opens the store at once without an account', async () => {
    renderOrders(guest);

    await userEvent.click(screen.getByRole('button', { name: 'Pedir otra vez' }));

    expect(screen.getByTestId('store')).toHaveTextContent('/m/cafe-luna');
    expect(account.saveCart).not.toHaveBeenCalled();
    expect(readCart('cafe-luna')?.lines).toEqual([{ productId: 'latte', optionIds: ['grande'], quantity: 2 }]);
  });
});
