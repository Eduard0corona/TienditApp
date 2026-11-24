import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Link, MemoryRouter, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { accountApi } from '../api/accountApi';
import { OrderAgainButton } from '../components/account/OrderAgainButton';
import { AuthContext, type AuthContextType } from '../contexts/AuthContextDefinition';
import { cancelAccountCartSaves } from '../utils/accountCartSync';
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
  id: 'o1',
  orderNumber: 'ORD-000390',
  businessSlug: 'cafe-luna',
  businessName: 'Café Luna',
  items: [
    { productId: 'latte', quantity: 2, optionIds: ['grande'] },
    { productId: 'queso', quantity: 0.25 },
  ],
};

function Orders() {
  const { orderAgain, opening } = useOrderAgain();
  return (
    <>
      <OrderAgainButton order={order} opening={opening} onClick={() => void orderAgain(order)} className="btn" />
      {/* Two taps before the page re-renders, as a double click can be. */}
      <button type="button" onClick={() => { void orderAgain(order); void orderAgain(order); }}>Dos veces</button>
      <Link to="/explorar">Explorar</Link>
    </>
  );
}

function Store() {
  const navigate = useNavigate();
  return (
    <>
      <output data-testid="store">{useLocation().pathname}</output>
      <button type="button" onClick={() => navigate(-1)}>Atrás</button>
    </>
  );
}

function renderOrders(auth: AuthContextType) {
  return render(
    <AuthContext.Provider value={auth}>
      <MemoryRouter initialEntries={['/mis-pedidos']}>
        <Routes>
          <Route path="/mis-pedidos" element={<Orders />} />
          <Route path="/explorar" element={<p>Explorar</p>} />
          <Route path="/m/:slug" element={<Store />} />
        </Routes>
      </MemoryRouter>
    </AuthContext.Provider>,
  );
}

/** What the account answers when it keeps the cart. */
// When the account kept it: a moment ago. A cart is good for 7 days from then, read with the real clock.
const keptAt = () => new Date(Date.now() - 60_000).toISOString();
const keptCart = (savedAt: string) => ({ slug: 'cafe-luna', businessName: 'Café Luna', savedAt, lines: [], reorderOf: 2, skipped: 1 });

describe('useOrderAgain', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    window.localStorage.clear();
  });
  afterEach(() => {
    vi.useRealTimers();
    // The saves of a store go one at a time: one a test left on its way does not hold the next test's.
    cancelAccountCartSaves();
  });

  it('keeps the cart in the account first, with what did not come back, and opens the store once it is kept (Fase 31P)', async () => {
    let kept: (value: unknown) => void = () => undefined;
    account.saveCart.mockReturnValue(new Promise((resolve) => { kept = resolve; }) as never);
    renderOrders(customer);

    await userEvent.click(screen.getByRole('button', { name: 'Pedir otra vez ORD-000390 de Café Luna' }));

    await waitFor(() => expect(account.saveCart).toHaveBeenCalledWith(
      'cafe-luna', { lines: [{ productId: 'latte', optionIds: ['grande'], quantity: 2 }], reorderOf: 2, skipped: 1 },
    ));
    // The store waits for the account: it would read the account's older cart. The button says what happens meanwhile.
    expect(screen.queryByTestId('store')).not.toBeInTheDocument();
    const busy = screen.getByRole('button', { name: 'Abriendo la tienda de Café Luna…' });
    expect(busy).toBeDisabled();
    expect(busy).toHaveAttribute('aria-busy', 'true');
    const savedAt = keptAt();
    await act(async () => kept(keptCart(savedAt)));

    expect(await screen.findByTestId('store')).toHaveTextContent('/m/cafe-luna');
    // This device has it with the account's time.
    expect(readCart('cafe-luna')).toEqual(expect.objectContaining({ reorderOf: 2, skipped: 1, savedAt }));
  });

  it('opens the store after a moment when the account does not answer, and this device keeps the cart', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let answer: (value: unknown) => void = () => undefined;
    account.saveCart.mockReturnValue(new Promise((resolve) => { answer = resolve; }) as never);
    renderOrders(customer);

    await userEvent.click(screen.getByRole('button', { name: 'Pedir otra vez ORD-000390 de Café Luna' }));
    await waitFor(() => expect(account.saveCart).toHaveBeenCalled());
    await act(async () => { await vi.advanceTimersByTimeAsync(ORDER_AGAIN_ACCOUNT_WAIT_MS - 100); });
    expect(screen.queryByTestId('store')).not.toBeInTheDocument();
    await act(async () => { await vi.advanceTimersByTimeAsync(100); });

    expect(screen.getByTestId('store')).toHaveTextContent('/m/cafe-luna');
    expect(readCart('cafe-luna')).toEqual(expect.objectContaining({ reorderOf: 2, skipped: 1 }));
    // The saves of a store go one at a time: this one ends before the next test saves.
    await act(async () => answer(keptCart(keptAt())));
  });

  it('opens the store as soon as the account says it cannot keep it, and this device keeps the cart', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    account.saveCart.mockRejectedValue(new Error('offline'));
    renderOrders(customer);

    await userEvent.click(screen.getByRole('button', { name: 'Pedir otra vez ORD-000390 de Café Luna' }));

    // Without waiting the whole moment for an answer that already came.
    expect(await screen.findByTestId('store')).toHaveTextContent('/m/cafe-luna');
    expect(readCart('cafe-luna')).toEqual(expect.objectContaining({ reorderOf: 2, skipped: 1 }));
  });

  it('does not open the store after all when the customer left the page before the account answered', async () => {
    let kept: (value: unknown) => void = () => undefined;
    account.saveCart.mockReturnValue(new Promise((resolve) => { kept = resolve; }) as never);
    renderOrders(customer);

    await userEvent.click(screen.getByRole('button', { name: 'Pedir otra vez ORD-000390 de Café Luna' }));
    await waitFor(() => expect(account.saveCart).toHaveBeenCalled());
    await userEvent.click(screen.getByRole('link', { name: 'Explorar' }));
    await act(async () => kept(keptCart(keptAt())));

    expect(screen.getByText('Explorar')).toBeInTheDocument();
    expect(screen.queryByTestId('store')).not.toBeInTheDocument();
    // The cart is kept all the same: the store brings it back when the customer opens it.
    expect(readCart('cafe-luna')).toEqual(expect.objectContaining({ reorderOf: 2, skipped: 1 }));
  });

  it('goes to the store once for two taps in a row, with one save', async () => {
    let kept: (value: unknown) => void = () => undefined;
    account.saveCart.mockReturnValue(new Promise((resolve) => { kept = resolve; }) as never);
    renderOrders(customer);

    await userEvent.click(screen.getByRole('button', { name: 'Dos veces' }));
    await waitFor(() => expect(account.saveCart).toHaveBeenCalled());
    await act(async () => kept(keptCart(keptAt())));
    expect(await screen.findByTestId('store')).toHaveTextContent('/m/cafe-luna');

    expect(account.saveCart).toHaveBeenCalledTimes(1);
    // Back once is back at the orders: the store is not twice in the history.
    await userEvent.click(screen.getByRole('button', { name: 'Atrás' }));
    expect(screen.getByRole('button', { name: 'Pedir otra vez ORD-000390 de Café Luna' })).toBeEnabled();
  });

  it('opens the store at once without an account', async () => {
    renderOrders(guest);

    await userEvent.click(screen.getByRole('button', { name: 'Pedir otra vez ORD-000390 de Café Luna' }));

    expect(screen.getByTestId('store')).toHaveTextContent('/m/cafe-luna');
    expect(account.saveCart).not.toHaveBeenCalled();
    expect(readCart('cafe-luna')?.lines).toEqual([{ productId: 'latte', optionIds: ['grande'], quantity: 2 }]);
  });
});
