import type { ReactNode } from 'react';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import type { TenantProfileDto, UserProfileDto } from '../../types';
import HomeRedirect from './HomeRedirect';

vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));

const businessesPage = <p>Página para negocios</p>;

function start(
  role: string | null,
  tenant: Partial<TenantProfileDto> | null = null,
  { visitor, isLoading = false }: { visitor?: ReactNode; isLoading?: boolean } = {},
) {
  vi.mocked(useAuth).mockReturnValue({
    user: role ? ({ role, permissions: [] } as unknown as UserProfileDto) : null,
    isAuthenticated: role !== null,
    isLoading,
  } as unknown as ReturnType<typeof useAuth>);
  vi.mocked(useTenant).mockReturnValue({
    tenant: tenant as TenantProfileDto | null,
    capabilities: null,
    isLoading: false,
    refreshTenant: vi.fn(),
  });
  render(
    <MemoryRouter initialEntries={['/']}>
      <Routes>
        <Route path="/" element={<HomeRedirect visitor={visitor} />} />
        <Route path="*" element={<p data-testid="landed">{window.location.pathname}</p>} />
        <Route path="/explorar" element={<p>Explorar</p>} />
        <Route path="/setup" element={<p>Arranque</p>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('HomeRedirect', () => {
  it('starts visitors and customers in Explorar', () => {
    start(null);
    expect(screen.getByText('Explorar')).toBeInTheDocument();
  });

  it('starts a customer account in Explorar', () => {
    start('Customer');
    expect(screen.getByText('Explorar')).toBeInTheDocument();
  });

  it('starts a new business in its guided setup', () => {
    start('TenantAdmin', { id: 't1', onboardingCompletedAt: null });
    expect(screen.getByText('Arranque')).toBeInTheDocument();
  });

  it('takes the team to its job with the title of the app, not the one the server wrote for the page', () => {
    document.title = 'TienditApp para negocios: punto de venta, menú en línea y facturación';
    start('TenantAdmin', { id: 't1', onboardingCompletedAt: null }, { visitor: businessesPage });
    expect(screen.getByText('Arranque')).toBeInTheDocument();
    expect(document.title).toBe('TienditApp');
  });

  describe('at /, with the page for businesses', () => {
    it('shows it to a visitor, with the title it gives itself', () => {
      document.title = 'TienditApp para negocios: punto de venta, menú en línea y facturación';
      start(null, null, { visitor: businessesPage });
      expect(screen.getByText('Página para negocios')).toBeInTheDocument();
      expect(screen.queryByText('Explorar')).not.toBeInTheDocument();
      expect(document.title).toBe('TienditApp para negocios: punto de venta, menú en línea y facturación');
    });

    it('shows it while the session is checked, instead of a spinner', () => {
      start(null, null, { visitor: businessesPage, isLoading: true });
      expect(screen.getByText('Página para negocios')).toBeInTheDocument();
      expect(screen.queryByRole('status')).not.toBeInTheDocument();
    });

    it('still starts a customer account in Explorar', () => {
      start('Customer', null, { visitor: businessesPage });
      expect(screen.getByText('Explorar')).toBeInTheDocument();
      expect(screen.queryByText('Página para negocios')).not.toBeInTheDocument();
    });
  });
});
