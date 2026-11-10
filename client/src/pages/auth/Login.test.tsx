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

  describe('with two-step verification (Fase 31E)', () => {
    function renderLogin() {
      const login = vi.fn().mockResolvedValue({ requiresTwoFactor: true, twoFactorChallengeToken: 'reto-123' });
      const completeTwoFactorLogin = vi.fn().mockResolvedValue(undefined);
      const from = { pathname: '/orders', search: '?status=1', hash: '' };
      render(
        <AuthContext.Provider value={{ login, completeTwoFactorLogin } as unknown as AuthContextType}>
          <MemoryRouter initialEntries={[{ pathname: '/login', state: { from } }]}>
            <Routes>
              <Route path="/login" element={<Login />} />
              <Route path="/orders" element={<Address />} />
            </Routes>
          </MemoryRouter>
        </AuthContext.Provider>,
      );
      return { login, completeTwoFactorLogin };
    }

    async function signIn() {
      await userEvent.type(screen.getByLabelText('Correo'), 'mariana@demo.com');
      await userEvent.type(screen.getByLabelText('Contraseña'), 'Secreta123!');
      await userEvent.click(screen.getByRole('button', { name: 'Iniciar sesión' }));
    }

    it('asks for the code instead of going in, and with it goes to the page it came from', async () => {
      const { completeTwoFactorLogin } = renderLogin();

      await signIn();

      expect(await screen.findByRole('heading', { name: 'Verificación en dos pasos' })).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Iniciar sesión' })).not.toBeInTheDocument();
      expect(screen.queryByTestId('address')).not.toBeInTheDocument();

      await userEvent.type(screen.getByLabelText('Código de verificación'), '123456');
      await userEvent.click(screen.getByRole('button', { name: 'Verificar' }));

      expect(completeTwoFactorLogin).toHaveBeenCalledWith({ twoFactorChallengeToken: 'reto-123', code: '123456', useRecoveryCode: false });
      expect(await screen.findByTestId('address')).toHaveTextContent(/^\/orders\?status=1$/);
    });

    it('«Volver al inicio de sesión» brings back the form as it was, and signing in again asks for a new code', async () => {
      const { login, completeTwoFactorLogin } = renderLogin();

      await signIn();
      await userEvent.click(await screen.findByRole('button', { name: 'Volver al inicio de sesión' }));

      expect(screen.getByRole('heading', { name: 'Iniciar sesión' })).toBeInTheDocument();
      expect(screen.getByLabelText('Correo')).toHaveValue('mariana@demo.com');
      expect(completeTwoFactorLogin).not.toHaveBeenCalled();

      await userEvent.click(screen.getByRole('button', { name: 'Iniciar sesión' }));
      expect(await screen.findByRole('heading', { name: 'Verificación en dos pasos' })).toBeInTheDocument();
      expect(login).toHaveBeenCalledTimes(2);
      expect(login).toHaveBeenLastCalledWith(expect.objectContaining({ email: 'mariana@demo.com', password: 'Secreta123!' }));
    });

    it('when the verification expired, going back and signing in again verifies with the new challenge', async () => {
      const { login, completeTwoFactorLogin } = renderLogin();
      login
        .mockResolvedValueOnce({ requiresTwoFactor: true, twoFactorChallengeToken: 'reto-1' })
        .mockResolvedValueOnce({ requiresTwoFactor: true, twoFactorChallengeToken: 'reto-2' });
      const expired = 'La verificación expiró o no es válida. Inicia sesión de nuevo.';
      completeTwoFactorLogin
        .mockRejectedValueOnce({ isAxiosError: true, response: { status: 401, data: { code: 'unauthorized', detail: expired, error: expired } } })
        .mockResolvedValueOnce(undefined);

      await signIn();
      await userEvent.type(await screen.findByLabelText('Código de verificación'), '123456');
      await userEvent.click(screen.getByRole('button', { name: 'Verificar' }));
      expect(await screen.findByRole('alert')).toHaveTextContent(expired);

      await userEvent.click(screen.getByRole('button', { name: 'Volver al inicio de sesión' }));
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
      await userEvent.click(screen.getByRole('button', { name: 'Iniciar sesión' }));

      // A new step: no warning of the one before, and the code goes with the new challenge.
      expect(await screen.findByLabelText('Código de verificación')).toHaveValue('');
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
      await userEvent.type(screen.getByLabelText('Código de verificación'), '654321');
      await userEvent.click(screen.getByRole('button', { name: 'Verificar' }));

      expect(completeTwoFactorLogin).toHaveBeenLastCalledWith({ twoFactorChallengeToken: 'reto-2', code: '654321', useRecoveryCode: false });
      expect(await screen.findByTestId('address')).toHaveTextContent(/^\/orders\?status=1$/);
    });
  });
});
