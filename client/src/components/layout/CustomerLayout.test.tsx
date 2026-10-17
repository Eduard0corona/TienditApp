import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useAuth } from '../../hooks/useAuth';
import type { UserProfileDto } from '../../types';
import CustomerLayout from './CustomerLayout';

vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('./ThemeToggle', () => ({ default: () => null }));

const logout = vi.fn();

function signedIn(role: string | null) {
  vi.mocked(useAuth).mockReturnValue({
    user: role ? ({ role } as UserProfileDto) : null,
    isAuthenticated: role !== null,
    isLoading: false,
    logout,
  } as unknown as ReturnType<typeof useAuth>);
}

function renderLayout() {
  return render(
    <MemoryRouter initialEntries={['/explorar']}>
      <Routes>
        <Route element={<CustomerLayout />}>
          <Route path="/explorar" element={<p>Negocios</p>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('CustomerLayout', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('lets a visitor enter or create a customer account', () => {
    signedIn(null);
    renderLayout();

    expect(screen.getByText('Negocios')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Entrar' })).toHaveAttribute('href', '/login');
    expect(screen.getByRole('link', { name: 'Crear cuenta' })).toHaveAttribute('href', '/register?cuenta=cliente');
    expect(screen.getByRole('link', { name: 'Explorar' })).toHaveAttribute('aria-current', 'page');
    expect(screen.queryByRole('link', { name: /Mis pedidos/ })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Recibe pedidos en línea con TienditApp' })).toHaveAttribute('href', '/register?cuenta=negocio');
  });

  it('gives a customer its orders, its profile and a way out', async () => {
    signedIn('Customer');
    renderLayout();

    expect(screen.getByRole('link', { name: /Mis pedidos/ })).toHaveAttribute('href', '/mis-pedidos');
    expect(screen.getByRole('link', { name: 'Mi cuenta' })).toHaveAttribute('href', '/cuenta');
    expect(screen.queryByRole('link', { name: 'Entrar' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Salir' }));
    expect(logout).toHaveBeenCalled();
  });

  it('takes the team of a business back to it', () => {
    signedIn('TenantAdmin');
    renderLayout();

    expect(screen.getByRole('link', { name: /Mi negocio/ })).toHaveAttribute('href', '/');
    expect(screen.queryByRole('link', { name: /Mis pedidos/ })).not.toBeInTheDocument();
  });
});
