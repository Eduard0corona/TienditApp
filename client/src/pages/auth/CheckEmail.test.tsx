import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { authApi } from '../../api/authApi';
import CheckEmail from './CheckEmail';

vi.mock('../../api/authApi', () => ({
  authApi: {
    resendConfirmationEmail: vi.fn(),
  },
}));

const api = vi.mocked(authApi);

function renderPage(state?: { email: string }) {
  return render(
    <MemoryRouter initialEntries={[{ pathname: '/check-email', state }]}>
      <CheckEmail />
    </MemoryRouter>
  );
}

describe('CheckEmail', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('tells to open the emailed link without naming the address', () => {
    renderPage({ email: 'ana@demo.com' });

    expect(screen.getByRole('heading', { name: 'Revisa tu correo' })).toBeInTheDocument();
    expect(screen.getByText('Te enviamos un enlace para activar tu cuenta.')).toBeInTheDocument();
    // The same page whether the email already had an account: that email says how to enter.
    expect(screen.getByText('¿Ya tenías una cuenta con ese correo? Te escribimos cómo entrar.')).toBeInTheDocument();
    expect(screen.queryByText(/ana@demo\.com/)).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ir a iniciar sesión' })).toHaveAttribute('href', '/login');
  });

  it('says nothing about an existing account when nobody just signed up', () => {
    // Opened from the login's "¿No te llegó el correo…?" link.
    renderPage();

    expect(screen.getByRole('heading', { name: 'Revisa tu correo' })).toBeInTheDocument();
    expect(screen.queryByText(/Ya tenías una cuenta/)).not.toBeInTheDocument();
  });

  it('waits a minute after the sign-up before offering another link', () => {
    renderPage({ email: 'ana@demo.com' });

    expect(screen.getByLabelText('¿No te llegó? Pide un enlace nuevo')).toHaveValue('ana@demo.com');
    expect(screen.getByRole('button', { name: /Reenviar en \d+ s/ })).toBeDisabled();
  });

  it('asks for a new link with the email and answers the same whether the account exists or not', async () => {
    api.resendConfirmationEmail.mockResolvedValue(
      { data: { message: 'ok' } } as Awaited<ReturnType<typeof authApi.resendConfirmationEmail>>
    );
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText('¿No te llegó? Pide un enlace nuevo'), 'ana@demo.com');
    await user.click(screen.getByRole('button', { name: 'Reenviar' }));

    expect(api.resendConfirmationEmail).toHaveBeenCalledWith('ana@demo.com');
    expect(await screen.findByRole('status')).toHaveTextContent(
      'Si la cuenta existe y aún no está activa, te enviamos un enlace nuevo.'
    );
    expect(screen.getByRole('button', { name: /Reenviar en \d+ s/ })).toBeDisabled();
  });

  it('shows the error when the link cannot be sent', async () => {
    api.resendConfirmationEmail.mockRejectedValue(new Error('network'));
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText('¿No te llegó? Pide un enlace nuevo'), 'ana@demo.com');
    await user.click(screen.getByRole('button', { name: 'Reenviar' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo enviar el enlace');
    expect(screen.getByRole('button', { name: 'Reenviar' })).toBeEnabled();
  });
});
