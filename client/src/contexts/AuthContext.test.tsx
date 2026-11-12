import { act, render, screen, waitFor } from '@testing-library/react';
import { useContext } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { authApi } from '../api/authApi';
import { profileApi } from '../api/profileApi';
import { apiRequest } from '../api/typedClient';
import { readPostalCode, savePostalCode } from '../utils/postalCodePreference';
import { latestCart, saveCart } from '../utils/savedCart';
import { AuthProvider } from './AuthContext';
import { AuthContext } from './AuthContextDefinition';

vi.mock('../api/authApi', () => ({ authApi: { login: vi.fn(), logout: vi.fn() } }));
vi.mock('../api/profileApi', () => ({ profileApi: { getProfile: vi.fn() } }));
vi.mock('../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('./useSessionSync', () => ({
  broadcastSessionChange: vi.fn(),
  reloadForTenantChange: vi.fn(),
  useSessionSync: vi.fn(),
}));

const profile = (role: 'Customer' | 'TenantAdmin') => ({
  id: 'u1', email: 'mariana@demo.com', firstName: 'Mariana', lastName: 'Ortiz', fullName: 'Mariana Ortiz',
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

  async function signInAndOut(role: 'Customer' | 'TenantAdmin') {
    vi.mocked(profileApi.getProfile).mockResolvedValue({ data: profile(role) } as never);
    render(<AuthProvider><Session /></AuthProvider>);
    await act(async () => { screen.getByRole('button', { name: 'Entrar' }).click(); });
    expect(await screen.findByText('Con sesión: Mariana')).toBeInTheDocument();
    saveCart('cafe-luna', { businessName: 'Café Luna', lines: [{ productId: 'latte', optionIds: [], quantity: 2 }] });
    savePostalCode('72000');

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
});
