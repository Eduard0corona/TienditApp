import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import type { TenantProfileDto, UserProfileDto } from '../../types';
import HomeRedirect from './HomeRedirect';

vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));

function start(role: string | null, tenant: Partial<TenantProfileDto> | null = null) {
  vi.mocked(useAuth).mockReturnValue({
    user: role ? ({ role, permissions: [] } as unknown as UserProfileDto) : null,
    isAuthenticated: role !== null,
    isLoading: false,
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
        <Route path="/" element={<HomeRedirect />} />
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

  it('takes the team to its job with the title of the app, not the one the server wrote for Explorar', () => {
    document.title = 'Explorar · TienditApp';
    start('TenantAdmin', { id: 't1', onboardingCompletedAt: null });
    expect(screen.getByText('Arranque')).toBeInTheDocument();
    expect(document.title).toBe('TienditApp');
  });
});
