import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { authApi } from '../../api/authApi';
import { apiRequest } from '../../api/typedClient';
import { useAuth } from '../../hooks/useAuth';
import AcceptInvitation from './AcceptInvitation';
import ConfirmPhone from './ConfirmPhone';
import ForgotPassword from './ForgotPassword';
import ResetPassword from './ResetPassword';

vi.mock('../../api/authApi', () => ({
  authApi: { confirmPhone: vi.fn(), resendPhoneConfirmation: vi.fn(), forgotPassword: vi.fn(), resetPassword: vi.fn() },
}));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));

const api = vi.mocked(authApi);
const request = vi.mocked(apiRequest);
const acceptInvitation = vi.fn();

const refused = (message: string, status = 400) => ({ isAxiosError: true, response: { status, data: { error: message } } });

function deferred() {
  let resolve: (value: unknown) => void = () => undefined;
  let reject: (reason: unknown) => void = () => undefined;
  const promise = new Promise((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

/** The account screens are reached by a link, with the login and the start behind them. */
function renderAt(url: string, state?: unknown) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[{ pathname: url.split('?')[0], search: url.includes('?') ? `?${url.split('?')[1]}` : '', state }]}>
        <Routes>
          <Route path="/accept-invitation" element={<AcceptInvitation />} />
          <Route path="/confirm-phone" element={<ConfirmPhone />} />
          <Route path="/forgot-password" element={<ForgotPassword />} />
          <Route path="/reset-password" element={<ResetPassword />} />
          <Route path="/login" element={<p>Pantalla de inicio de sesión</p>} />
          <Route path="/" element={<p>Inicio del negocio</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

const STRONG = 'Cafe2026x';

beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(useAuth).mockReturnValue({ acceptInvitation } as unknown as ReturnType<typeof useAuth>);
});

afterEach(() => vi.useRealTimers());

/** Lets the wait before asking for another code run out, a second at a time as the screen counts it. */
async function waitOut(seconds: number) {
  for (let i = 0; i < seconds; i += 1) await act(() => vi.advanceTimersByTimeAsync(1000));
}

describe('AcceptInvitation', () => {
  it('shows who invited the person and enters the business with the password chosen', async () => {
    const user = userEvent.setup();
    request.mockResolvedValue({ firstName: 'Ana', email: 'ana@cafeluna.mx', businesses: ['Café Luna', 'Café Sol'] } as never);
    acceptInvitation.mockResolvedValue(undefined);
    renderAt('/accept-invitation?userId=u-1&token=t%2B1');

    expect(screen.getByRole('status')).toHaveTextContent('Revisando tu invitación…');
    expect(await screen.findByRole('heading', { name: 'Hola, Ana' })).toBeInTheDocument();
    expect(screen.getByText('Café Luna, Café Sol')).toBeInTheDocument();
    expect(screen.getByLabelText('Entrarás con este correo')).toHaveValue('ana@cafeluna.mx');
    expect(request).toHaveBeenCalledWith('get', '/api/v1/auth/invitation', { query: { userId: 'u-1', token: 't+1' } });

    // The rules of the API are checked before sending.
    await user.type(screen.getByLabelText('Contraseña'), 'corta');
    await user.type(screen.getByLabelText('Repite la contraseña'), 'otra');
    await user.click(screen.getByRole('button', { name: 'Entrar al negocio' }));
    expect(await screen.findByText(/Usa al menos 8 caracteres/)).toBeInTheDocument();
    expect(screen.getByText('Las contraseñas no coinciden.')).toBeInTheDocument();
    expect(acceptInvitation).not.toHaveBeenCalled();

    await user.clear(screen.getByLabelText('Contraseña'));
    await user.type(screen.getByLabelText('Contraseña'), STRONG);
    await user.clear(screen.getByLabelText('Repite la contraseña'));
    await user.type(screen.getByLabelText('Repite la contraseña'), STRONG);
    await user.click(screen.getByRole('button', { name: 'Entrar al negocio' }));
    expect(await screen.findByText('Inicio del negocio')).toBeInTheDocument();
    expect(acceptInvitation).toHaveBeenCalledWith({ userId: 'u-1', token: 't+1', newPassword: STRONG });
  });

  it('says what the server refused and lets the person try again', async () => {
    const user = userEvent.setup();
    request.mockResolvedValue({ firstName: 'Ana', email: 'ana@cafeluna.mx', businesses: ['Café Luna'] } as never);
    acceptInvitation.mockRejectedValue(refused('La contraseña es demasiado común.'));
    renderAt('/accept-invitation?userId=u-1&token=t1');

    await user.type(await screen.findByLabelText('Contraseña'), STRONG);
    await user.type(screen.getByLabelText('Repite la contraseña'), STRONG);
    await user.click(screen.getByRole('button', { name: 'Entrar al negocio' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('La contraseña es demasiado común.');
    expect(screen.getByRole('button', { name: 'Entrar al negocio' })).toBeEnabled();
  });

  it('explains an incomplete link without asking for the invitation', async () => {
    renderAt('/accept-invitation?userId=u-1');
    expect(screen.getByRole('alert')).toHaveTextContent('El enlace está incompleto.');
    expect(request).not.toHaveBeenCalled();
  });

  it('says what the server answered for an invitation that no longer works', async () => {
    request.mockRejectedValue(refused('La invitación ya se usó.'));
    renderAt('/accept-invitation?userId=u-1&token=t1');
    expect(await screen.findByRole('alert')).toHaveTextContent('La invitación ya se usó.');
    expect(screen.getByRole('link', { name: '¿Ya tienes contraseña? Inicia sesión' })).toHaveAttribute('href', '/login');
  });
});

describe('ConfirmPhone', () => {
  it('confirms the code and opens the login after a moment', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    api.confirmPhone.mockResolvedValue({} as never);
    renderAt('/confirm-phone?userId=u-1');

    await user.type(screen.getByLabelText('Código'), '12345');
    await user.click(screen.getByRole('button', { name: 'Verificar' }));
    expect(await screen.findByText('Número confirmado. Ahora puedes iniciar sesión.')).toBeInTheDocument();
    expect(api.confirmPhone).toHaveBeenCalledWith('u-1', '12345');
    expect(screen.getByRole('button', { name: 'Verificar' })).toBeDisabled();
    expect(screen.getByRole('button', { name: '¿Número equivocado? Cambiarlo' })).toBeDisabled();

    await act(() => vi.advanceTimersByTimeAsync(2000));
    expect(screen.getByText('Pantalla de inicio de sesión')).toBeInTheDocument();
  });

  it('does not open the login later when the person already left', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    api.confirmPhone.mockResolvedValue({} as never);
    const screenOnPage = renderAt('/confirm-phone?userId=u-1');

    await user.type(screen.getByLabelText('Código'), '123456');
    await user.click(screen.getByRole('button', { name: 'Verificar' }));
    await screen.findByText('Número confirmado. Ahora puedes iniciar sesión.');
    screenOnPage.unmount();
    expect(vi.getTimerCount()).toBe(0);
  });

  it('checks the code, says when it is wrong and sends another one after waiting', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    api.confirmPhone.mockRejectedValue(refused('El código no es válido.'));
    const resending = deferred();
    api.resendPhoneConfirmation.mockReturnValue(resending.promise as never);
    // Coming from the registration, the first code was just sent.
    renderAt('/confirm-phone', { userId: 'u-2' });

    expect(screen.getByRole('button', { name: 'Reenviar código en 60 s' })).toBeDisabled();
    await user.type(screen.getByLabelText('Código'), '12a45');
    await user.click(screen.getByRole('button', { name: 'Verificar' }));
    expect(await screen.findByText('Solo se permiten dígitos (5 o 6).')).toBeInTheDocument();
    expect(api.confirmPhone).not.toHaveBeenCalled();

    await user.clear(screen.getByLabelText('Código'));
    await user.type(screen.getByLabelText('Código'), '11111');
    await user.click(screen.getByRole('button', { name: 'Verificar' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('El código no es válido.');

    await waitOut(60);
    await user.click(screen.getByRole('button', { name: '¿No te llegó? Reenviar código' }));
    expect(screen.getByRole('button', { name: 'Enviando…' })).toBeDisabled();
    // The number cannot be changed while a code is on its way.
    expect(screen.getByRole('button', { name: '¿Número equivocado? Cambiarlo' })).toBeDisabled();
    expect(api.resendPhoneConfirmation).toHaveBeenCalledTimes(1);
    expect(api.resendPhoneConfirmation).toHaveBeenCalledWith('u-2');
    await act(async () => resending.resolve({}));
    expect(screen.getByRole('status')).toHaveTextContent('Te enviamos un código nuevo.');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reenviar código en 60 s' })).toBeDisabled();
  });

  it('fixes a number typed wrong with the password of the account, without leaving while it sends', async () => {
    const user = userEvent.setup();
    const changing = deferred();
    request.mockReturnValue(changing.promise as never);
    renderAt('/confirm-phone?userId=u-3');

    await user.click(screen.getByRole('button', { name: '¿Número equivocado? Cambiarlo' }));
    await user.type(screen.getByLabelText('Número correcto'), '9991234567');
    await user.type(screen.getByLabelText('Tu contraseña'), STRONG);
    await user.click(screen.getByRole('button', { name: 'Enviar código' }));
    expect(screen.getByRole('button', { name: 'Cancelar' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Enviando…' })).toBeDisabled();
    expect(request).toHaveBeenCalledWith('post', '/api/v1/auth/change-unconfirmed-phone', {
      body: { userId: 'u-3', password: STRONG, phoneNumber: '9991234567' },
    });

    await act(async () => changing.resolve({}));
    expect(screen.getByRole('status')).toHaveTextContent('Te enviamos un código al número nuevo.');
    // Back to the code, which is empty; another one can be asked for after waiting.
    expect(screen.getByLabelText('Código')).toHaveValue('');
    expect(screen.getByRole('button', { name: 'Reenviar código en 60 s' })).toBeDisabled();
  });

  it('says what the server refused when changing the number', async () => {
    const user = userEvent.setup();
    request.mockRejectedValue(refused('La contraseña no es correcta.'));
    renderAt('/confirm-phone?userId=u-3');

    await user.click(screen.getByRole('button', { name: '¿Número equivocado? Cambiarlo' }));
    await user.type(screen.getByLabelText('Número correcto'), '9991234567');
    await user.type(screen.getByLabelText('Tu contraseña'), 'mala');
    await user.click(screen.getByRole('button', { name: 'Enviar código' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('La contraseña no es correcta.');
    expect(screen.getByLabelText('Número correcto')).toHaveValue('9991234567');
  });

  it('explains when there is no account to confirm', () => {
    renderAt('/confirm-phone');
    expect(screen.getByRole('alert')).toHaveTextContent('No encontramos la cuenta por confirmar.');
    expect(screen.queryByLabelText('Código')).not.toBeInTheDocument();
  });
});

describe('ForgotPassword', () => {
  it('sends a link by email and says it arrives only if the email is registered', async () => {
    const user = userEvent.setup();
    api.forgotPassword.mockResolvedValue({} as never);
    renderAt('/forgot-password');

    await user.click(screen.getByRole('button', { name: 'Enviar enlace' }));
    expect(await screen.findByText('Escribe tu correo.')).toBeInTheDocument();
    await user.type(screen.getByLabelText('Correo'), ' ana@cafeluna.mx ');
    await user.click(screen.getByRole('button', { name: 'Enviar enlace' }));
    expect(await screen.findByRole('status')).toHaveTextContent('Si el correo está registrado, te llegará un enlace');
    expect(api.forgotPassword).toHaveBeenCalledWith('ana@cafeluna.mx');
  });

  it('sends a code by SMS, asks for another one only once at a time and saves the new password', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    const again = deferred();
    request.mockImplementation(((_method: string, path: string) => {
      if (path === '/api/v1/auth/forgot-password/sms') return request.mock.calls.length > 1 ? again.promise : Promise.resolve({});
      return Promise.resolve({});
    }) as never);
    renderAt('/forgot-password');

    await user.click(screen.getByRole('button', { name: /Por SMS/ }));
    expect(screen.getByRole('button', { name: /Por SMS/ })).toHaveAttribute('aria-pressed', 'true');
    await user.type(screen.getByLabelText('Correo'), 'ana@cafeluna.mx');
    await user.click(screen.getByRole('button', { name: 'Enviar código' }));
    expect(await screen.findByRole('status')).toHaveTextContent('Si tu cuenta tiene un teléfono confirmado');
    expect(screen.getByRole('button', { name: 'Pedir otro código en 60 s' })).toBeDisabled();

    await waitOut(60);
    await user.click(screen.getByRole('button', { name: '¿No te llegó? Pedir otro código' }));
    expect(screen.getByRole('button', { name: 'Enviando…' })).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Enviando…' }));
    expect(request.mock.calls.filter(([, path]) => path === '/api/v1/auth/forgot-password/sms')).toHaveLength(2);
    await act(async () => again.resolve({}));
    expect(screen.getByRole('button', { name: 'Pedir otro código en 60 s' })).toBeDisabled();

    await user.type(screen.getByLabelText('Código'), '654321');
    await user.type(screen.getByLabelText('Contraseña nueva'), STRONG);
    await user.click(screen.getByRole('button', { name: 'Guardar contraseña' }));
    expect(await screen.findByText(/Tu contraseña se actualizó/)).toBeInTheDocument();
    expect(request).toHaveBeenCalledWith('post', '/api/v1/auth/reset-password/sms', {
      body: { email: 'ana@cafeluna.mx', code: '654321', newPassword: STRONG },
    });
  });

  it('keeps the channel while something is on its way', async () => {
    const user = userEvent.setup();
    const sending = deferred();
    api.forgotPassword.mockReturnValue(sending.promise as never);
    renderAt('/forgot-password');

    await user.type(screen.getByLabelText('Correo'), 'ana@cafeluna.mx');
    await user.click(screen.getByRole('button', { name: 'Enviar enlace' }));
    expect(screen.getByRole('button', { name: /Por SMS/ })).toBeDisabled();
    expect(screen.getByRole('button', { name: /Por correo/ })).toBeDisabled();
    await act(async () => sending.resolve({}));
    expect(screen.getByRole('button', { name: /Por SMS/ })).toBeEnabled();
    expect(screen.getByRole('status')).toHaveTextContent('Si el correo está registrado');
  });

  it('takes away what it said before when asking for another code fails', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    request.mockResolvedValueOnce({} as never).mockRejectedValueOnce(refused('Demasiados intentos. Espera un momento.'));
    renderAt('/forgot-password');

    await user.click(screen.getByRole('button', { name: /Por SMS/ }));
    await user.type(screen.getByLabelText('Correo'), 'ana@cafeluna.mx');
    await user.click(screen.getByRole('button', { name: 'Enviar código' }));
    expect(await screen.findByRole('status')).toHaveTextContent('Si tu cuenta tiene un teléfono confirmado');

    await waitOut(60);
    await user.click(screen.getByRole('button', { name: '¿No te llegó? Pedir otro código' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Demasiados intentos. Espera un momento.');
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it('says when the code is not valid and what failed when sending', async () => {
    const user = userEvent.setup();
    request.mockImplementation(((_method: string, path: string) => (
      path === '/api/v1/auth/reset-password/sms' ? Promise.reject(refused('El código ya expiró.')) : Promise.resolve({})
    )) as never);
    api.forgotPassword.mockRejectedValue(refused('Demasiados intentos.', 400));
    renderAt('/forgot-password');

    await user.type(screen.getByLabelText('Correo'), 'ana@cafeluna.mx');
    await user.click(screen.getByRole('button', { name: 'Enviar enlace' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Demasiados intentos.');

    // Changing how to recover it starts over.
    await user.click(screen.getByRole('button', { name: /Por SMS/ }));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    await user.type(screen.getByLabelText('Correo'), 'ana@cafeluna.mx');
    await user.click(screen.getByRole('button', { name: 'Enviar código' }));
    await user.type(await screen.findByLabelText('Código'), '111111');
    await user.type(screen.getByLabelText('Contraseña nueva'), STRONG);
    await user.click(screen.getByRole('button', { name: 'Guardar contraseña' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('El código ya expiró.');
  });
});

describe('ResetPassword', () => {
  it('saves the new password of the link and offers to sign in', async () => {
    const user = userEvent.setup();
    const saving = deferred();
    api.resetPassword.mockReturnValue(saving.promise as never);
    renderAt('/reset-password?userId=u-1&token=ab%2Bc');

    await user.type(screen.getByLabelText('Nueva contraseña'), STRONG);
    await user.type(screen.getByLabelText('Confirmar contraseña'), 'Cafe2026y');
    await user.click(screen.getByRole('button', { name: 'Guardar contraseña' }));
    expect(await screen.findByText('Las contraseñas no coinciden')).toBeInTheDocument();
    expect(api.resetPassword).not.toHaveBeenCalled();

    await user.clear(screen.getByLabelText('Confirmar contraseña'));
    await user.type(screen.getByLabelText('Confirmar contraseña'), STRONG);
    await user.click(screen.getByRole('button', { name: 'Guardar contraseña' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Guardando…' })).toBeDisabled());
    expect(api.resetPassword).toHaveBeenCalledWith('u-1', 'ab+c', STRONG);

    await act(async () => saving.resolve({}));
    expect(screen.getByRole('status')).toHaveTextContent('Tu contraseña se actualizó.');
    expect(screen.getByRole('link', { name: 'iniciar sesión' })).toHaveAttribute('href', '/login');
  });

  it('says what the server refused and keeps the form', async () => {
    const user = userEvent.setup();
    api.resetPassword.mockRejectedValue(refused('El enlace ya expiró.'));
    renderAt('/reset-password?userId=u-1&token=t1');

    await user.type(screen.getByLabelText('Nueva contraseña'), STRONG);
    await user.type(screen.getByLabelText('Confirmar contraseña'), STRONG);
    await user.click(screen.getByRole('button', { name: 'Guardar contraseña' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('El enlace ya expiró.');
    expect(screen.getByRole('button', { name: 'Guardar contraseña' })).toBeEnabled();
  });

  it('sends to ask for a new link when the one opened is incomplete', () => {
    renderAt('/reset-password?token=t1');
    expect(screen.getByRole('alert')).toHaveTextContent('El enlace no es válido.');
    expect(screen.getByRole('link', { name: 'Recuperar contraseña' })).toHaveAttribute('href', '/forgot-password');
    expect(screen.queryByLabelText('Nueva contraseña')).not.toBeInTheDocument();
  });
});
