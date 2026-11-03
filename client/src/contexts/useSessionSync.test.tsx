import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import App from '../App';
import { useAuth } from '../hooks/useAuth';
import { useTenant } from '../hooks/useTenant';
import { reloadForTenantChange } from './useSessionSync';

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
