import { act, render, screen, waitFor } from '@testing-library/react';
import { useContext } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { authApi } from '../api/authApi';
import { refreshAccessToken } from '../api/axios';
import { profileApi } from '../api/profileApi';
import { apiRequest } from '../api/typedClient';
import { readPostalCode, savePostalCode } from '../utils/postalCodePreference';
import { CUSTOMER_LEFT_EVENT, CUSTOMER_STORAGE_KEY } from '../utils/customerDevice';
import { latestCart, saveCart } from '../utils/savedCart';
import { AuthProvider } from './AuthContext';
import { AuthContext } from './AuthContextDefinition';
import { useSessionSync } from './useSessionSync';

vi.mock('../api/authApi', () => ({ authApi: { login: vi.fn(), logout: vi.fn() } }));
vi.mock('../api/axios', async (importOriginal) => ({ ...(await importOriginal<object>()), refreshAccessToken: vi.fn() }));
vi.mock('../api/profileApi', () => ({ profileApi: { getProfile: vi.fn() } }));
vi.mock('../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('./useSessionSync', () => ({
  broadcastSessionChange: vi.fn(),
  reloadForTenantChange: vi.fn(),
  useSessionSync: vi.fn(),
}));

const profile = (role: 'Customer' | 'TenantAdmin', id = 'u1') => ({
  id, email: 'mariana@demo.com', firstName: 'Mariana', lastName: 'Ortiz', fullName: 'Mariana Ortiz',
  phoneNumber: null, phoneNumberConfirmed: false, emailConfirmed: true, role, permissions: [],
});

/** Signs in and out with the buttons of the app's own context. */
function Session() {
  const auth = useContext(AuthContext)!;
  return (
    <>
      <p>{auth.user ? `Con sesión: ${auth.user.firstName}` : 'Sin sesión'}</p>
      <button type="button" onClick={() => void auth.login({ email: 'mariana@demo.com', password: 'x' })}>Entrar</button>
      <button type="button" onClick={() => void auth.logout()}>Salir</button>
    </>
  );
}

describe('AuthProvider leaving the device (Fase 31J)', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    window.localStorage.clear();
    vi.mocked(authApi.login).mockResolvedValue({ data: { accessToken: 'token', requiresTwoFactor: false } } as never);
    vi.mocked(authApi.logout).mockResolvedValue({} as never);
    vi.mocked(apiRequest).mockRejectedValue(new Error('Sin negocio'));
  });

  afterEach(() => window.localStorage.clear());

  /** What the device keeps for whoever buys on it. */
  function keepOnDevice() {
    saveCart('cafe-luna', { businessName: 'Café Luna', lines: [{ productId: 'latte', optionIds: [], quantity: 2 }] });
    savePostalCode('72000');
  }

  async function signIn(role: 'Customer' | 'TenantAdmin', id = 'u1') {
    vi.mocked(profileApi.getProfile).mockResolvedValue({ data: profile(role, id) } as never);
    const rendered = render(<AuthProvider><Session /></AuthProvider>);
    await act(async () => { screen.getByRole('button', { name: 'Entrar' }).click(); });
    expect(await screen.findByText('Con sesión: Mariana')).toBeInTheDocument();
    return rendered;
  }

  async function signInAndOut(role: 'Customer' | 'TenantAdmin') {
    await signIn(role);
    keepOnDevice();

    await act(async () => { screen.getByRole('button', { name: 'Salir' }).click(); });
    await waitFor(() => expect(screen.getByText('Sin sesión')).toBeInTheDocument());
  }

  it('forgets the carts and the postal code of a customer account, which keeps them, so whoever comes next does not take them', async () => {
    await signInAndOut('Customer');

    expect(latestCart()).toBeNull();
    expect(readPostalCode()).toBe('');
    expect(authApi.logout).toHaveBeenCalled();
  });

  it('keeps them for a session of a business\'s team: its carts were only ever on this device', async () => {
    await signInAndOut('TenantAdmin');

    expect(latestCart()?.slug).toBe('cafe-luna');
    expect(readPostalCode()).toBe('72000');
  });

  it('forgets them too when the session of a customer account expires or is revoked, and stops what was waiting', async () => {
    await signIn('Customer');
    keepOnDevice();
    const customerLeft = vi.fn();
    window.addEventListener(CUSTOMER_LEFT_EVENT, customerLeft);

    // A refresh the server rejects (or a sign-out in another tab) ends the session through the sync.
    const clearSession = vi.mocked(useSessionSync).mock.calls.at(-1)![0];
    act(() => clearSession());

    expect(await screen.findByText('Sin sesión')).toBeInTheDocument();
    expect(latestCart()).toBeNull();
    expect(readPostalCode()).toBe('');
    expect(customerLeft).toHaveBeenCalledTimes(1);
    window.removeEventListener(CUSTOMER_LEFT_EVENT, customerLeft);
  });

  it('forgets them when the session of the customer account that kept them cannot be recovered on opening', async () => {
    window.localStorage.setItem('fpd_has_session', '1');
    window.localStorage.setItem(CUSTOMER_STORAGE_KEY, 'u1');
    keepOnDevice();
    vi.mocked(refreshAccessToken).mockRejectedValue(new Error('Sesión vencida'));

    render(<AuthProvider><Session /></AuthProvider>);

    await waitFor(() => expect(latestCart()).toBeNull());
    expect(readPostalCode()).toBe('');
  });

  it('forgets those of another customer account when one signs in, and keeps its own', async () => {
    // Another account's, left while the app was closed.
    window.localStorage.setItem(CUSTOMER_STORAGE_KEY, 'u0');
    keepOnDevice();
    const { unmount } = await signIn('Customer', 'u1');
    expect(latestCart()).toBeNull();
    expect(readPostalCode()).toBe('');
    unmount();

    // Its own: they are what it chose here.
    keepOnDevice();
    await signIn('Customer', 'u1');
    expect(latestCart()?.slug).toBe('cafe-luna');
    expect(readPostalCode()).toBe('72000');
  });
});
