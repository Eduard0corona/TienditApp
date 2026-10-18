import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { AuthContext, type AuthContextType } from '../../contexts/AuthContextDefinition';
import Login from './Login';

function Address() {
  const location = useLocation();
  return <p data-testid="address">{`${location.pathname}${location.search}${location.hash}`}</p>;
}

describe('Login', () => {
  it('goes back to the page it came from as it was, with its search (Fase 24F)', async () => {
    const login = vi.fn().mockResolvedValue({ requiresTwoFactor: false });
    const from = { pathname: '/explorar', search: '?q=tacos', hash: '' };

    render(
      <AuthContext.Provider value={{ login } as unknown as AuthContextType}>
        <MemoryRouter initialEntries={[{ pathname: '/login', state: { from } }]}>
          <Routes>
            <Route path="/login" element={<Login />} />
            <Route path="/explorar" element={<Address />} />
          </Routes>
        </MemoryRouter>
      </AuthContext.Provider>,
    );
    await userEvent.type(screen.getByLabelText('Correo'), 'mariana@demo.com');
    await userEvent.type(screen.getByLabelText('Contraseña'), 'Secreta123!');
    await userEvent.click(screen.getByRole('button', { name: 'Iniciar sesión' }));

    expect(await screen.findByTestId('address')).toHaveTextContent(/^\/explorar\?q=tacos$/);
  });
});
