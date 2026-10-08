import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { authApi } from '../../api/authApi';
import ConfirmEmail from './ConfirmEmail';

vi.mock('../../api/authApi', () => ({
  authApi: {
    confirmEmail: vi.fn(),
    resendConfirmationEmail: vi.fn(),
  },
}));

const api = vi.mocked(authApi);

/** Stands in for the login: shows the router state it was opened with. */
function LoginProbe() {
  const location = useLocation();
  return <p>login {JSON.stringify(location.state)}</p>;
}

function renderLink(url: string) {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <Routes>
        <Route path="/confirm-email" element={<ConfirmEmail />} />
        <Route path="/login" element={<LoginProbe />} />
      </Routes>
    </MemoryRouter>
  );
}

describe('ConfirmEmail', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('activates the account with the link and opens the login', async () => {
    api.confirmEmail.mockResolvedValue({ data: { message: 'ok' } } as Awaited<ReturnType<typeof authApi.confirmEmail>>);

    // The email escapes the token: "+" and "/" arrive encoded.
    renderLink('/confirm-email?userId=u-1&token=ab%2Bc%2Fd%3D');

    expect(await screen.findByText('login {"accountActivated":true}')).toBeInTheDocument();
    expect(api.confirmEmail).toHaveBeenCalledTimes(1);
    expect(api.confirmEmail).toHaveBeenCalledWith('u-1', 'ab+c/d=');
  });

  it('stays to explain a failed link and offers a new one', async () => {
    api.confirmEmail.mockRejectedValue(new Error('network'));

    renderLink('/confirm-email?userId=u-1&token=vencido');

    expect(await screen.findByRole('alert')).toHaveTextContent('Es posible que el enlace haya expirado');
    expect(screen.getByLabelText('Pide un enlace nuevo')).toBeInTheDocument();
    expect(screen.queryByText(/^login/)).not.toBeInTheDocument();
  });

  it('does not call the API for an incomplete link', () => {
    renderLink('/confirm-email?userId=u-1');

    expect(screen.getByRole('alert')).toHaveTextContent('El enlace está incompleto');
    expect(api.confirmEmail).not.toHaveBeenCalled();
  });
});
