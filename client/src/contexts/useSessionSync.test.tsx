import { render, renderHook, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import App from '../App';
import { useAuth } from '../hooks/useAuth';
import { useTenant } from '../hooks/useTenant';
import { CUSTOMER_LEFT_EVENT } from '../utils/customerDevice';
import { broadcastSessionChange, reloadForTenantChange, useSessionSync } from './useSessionSync';

vi.mock('../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../hooks/useTenant', () => ({ useTenant: vi.fn() }));

describe('reloadForTenantChange', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  });

  it('starts over with a spinner while the session is checked, not with the page for businesses of /', () => {
    const assign = vi.fn();
    vi.stubGlobal('location', { assign });
    reloadForTenantChange();
    vi.unstubAllGlobals();
    expect(assign).toHaveBeenCalledTimes(1);
    const [path] = assign.mock.calls[0] as [string];
    expect(path).not.toBe('/');

    // The page it reloads into, before the session of the new business is back.
    vi.mocked(useAuth).mockReturnValue({
      user: null,
      isAuthenticated: false,
      isLoading: true,
    } as unknown as ReturnType<typeof useAuth>);
    vi.mocked(useTenant).mockReturnValue({ tenant: null, capabilities: null, isLoading: false, refreshTenant: vi.fn() });
    window.history.replaceState(null, '', path);
    render(<App />);

    expect(screen.getByRole('status')).toHaveTextContent('Cargando...');
    // The page for businesses is lazy: at / its fallback would show instead.
    expect(screen.queryByText('Cargando…')).not.toBeInTheDocument();
    expect(window.location.pathname).toBe(path);
  });
});

describe('useSessionSync', () => {
  it('ends the session when another tab signs out, and stops the cart saves of this one', async () => {
    const clearSession = vi.fn();
    const customerLeft = vi.fn();
    window.addEventListener(CUSTOMER_LEFT_EVENT, customerLeft);
    const { unmount } = renderHook(() => useSessionSync(clearSession));

    // Another tab of this browser signed out (Fase 31J: it forgot the carts of the device).
    broadcastSessionChange({ type: 'logout' });

    await waitFor(() => expect(clearSession).toHaveBeenCalledTimes(1));
    expect(customerLeft).toHaveBeenCalledTimes(1);
    unmount();
    window.removeEventListener(CUSTOMER_LEFT_EVENT, customerLeft);
  });
});
