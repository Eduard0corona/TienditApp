import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { AuthContext, type AuthContextType } from '../../contexts/AuthContextDefinition';
import TwoFactorLoginStep from './TwoFactorLoginStep';

// As the API answers a code it does not take: 401 with ProblemDetails (TwoFactorController).
const refused = (detail: string) => ({
  isAxiosError: true,
  response: { status: 401, data: { code: 'unauthorized', detail, error: detail } },
});

function deferred() {
  let resolve: () => void = () => undefined;
  const promise = new Promise<void>((res) => {
    resolve = res;
  });
  return { promise, resolve };
}

function renderStep(completeTwoFactorLogin = vi.fn().mockResolvedValue(undefined)) {
  const onSuccess = vi.fn();
  const onCancel = vi.fn();
  render(
    <AuthContext.Provider value={{ completeTwoFactorLogin } as unknown as AuthContextType}>
      <TwoFactorLoginStep challengeToken="reto-123" onSuccess={onSuccess} onCancel={onCancel} />
    </AuthContext.Provider>,
  );
  return { completeTwoFactorLogin, onSuccess, onCancel };
}

const codeField = () => screen.getByRole('textbox');
const verify = () => userEvent.click(screen.getByRole('button', { name: 'Verificar' }));

describe('TwoFactorLoginStep (Fase 31E)', () => {
  it('asks for the 6 digits of the app, with the field ready to type a one-time code', () => {
    renderStep();

    expect(screen.getByRole('heading', { name: 'Verificación en dos pasos' })).toBeInTheDocument();
    expect(screen.getByText('Ingresa el código de 6 dígitos de tu app de autenticación.')).toBeInTheDocument();
    const field = screen.getByLabelText('Código de verificación');
    expect(field).toHaveFocus();
    expect(field).toHaveAttribute('aria-invalid', 'false');
    expect(field).toHaveAttribute('autocomplete', 'one-time-code');
    expect(field).toHaveAttribute('inputmode', 'numeric');
    expect(field).toHaveAttribute('maxlength', '7');
  });

  it('sends the code of the app with the challenge of the sign-in, and goes on when the server takes it', async () => {
    const { completeTwoFactorLogin, onSuccess } = renderStep();

    await userEvent.type(codeField(), '123456');
    await verify();

    expect(completeTwoFactorLogin).toHaveBeenCalledOnce();
    expect(completeTwoFactorLogin).toHaveBeenCalledWith({ twoFactorChallengeToken: 'reto-123', code: '123456', useRecoveryCode: false });
    expect(onSuccess).toHaveBeenCalledOnce();
  });

  it.each(['123 456', '123-456'])('takes the 6 digits written as %s, as an app shows them, and sends them as typed', async (code) => {
    const { completeTwoFactorLogin, onSuccess } = renderStep();

    await userEvent.type(codeField(), code);
    await verify();

    expect(completeTwoFactorLogin).toHaveBeenCalledWith(expect.objectContaining({ code }));
    expect(onSuccess).toHaveBeenCalledOnce();
  });

  it('does not send an empty code or one that is not 6 digits, and says why next to the field', async () => {
    const { completeTwoFactorLogin } = renderStep();

    await verify();
    expect(await screen.findByText('El código es requerido')).toBeInTheDocument();
    expect(codeField()).toHaveAttribute('aria-invalid', 'true');
    expect(codeField()).toHaveAccessibleDescription('El código es requerido');

    await userEvent.type(codeField(), '12345');
    await verify();
    expect(await screen.findByText('El código debe tener 6 dígitos')).toBeInTheDocument();

    await userEvent.clear(codeField());
    await userEvent.type(codeField(), '12345a');
    await verify();
    expect(await screen.findByText('El código debe tener 6 dígitos')).toBeInTheDocument();

    expect(completeTwoFactorLogin).not.toHaveBeenCalled();
  });

  it('says what the server answered when it refuses the code, keeps the person here and lets them try again', async () => {
    const completeTwoFactorLogin = vi.fn()
      .mockRejectedValueOnce(refused('Código de autenticación inválido.'))
      .mockResolvedValueOnce(undefined);
    const { onSuccess } = renderStep(completeTwoFactorLogin);

    await userEvent.type(codeField(), '000000');
    await verify();

    expect(await screen.findByRole('alert')).toHaveTextContent('Código de autenticación inválido.');
    expect(onSuccess).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Verificar' })).toBeEnabled();

    await userEvent.clear(codeField());
    await userEvent.type(codeField(), '654321');
    await verify();

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(completeTwoFactorLogin).toHaveBeenLastCalledWith(expect.objectContaining({ code: '654321' }));
    expect(onSuccess).toHaveBeenCalledOnce();
  });

  it('says it could not verify the code when the server gives no reason', async () => {
    renderStep(vi.fn().mockRejectedValueOnce({ isAxiosError: true, response: { status: 500, data: {} } }));

    await userEvent.type(codeField(), '123456');
    await verify();

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudo verificar el código');
  });

  it('with a recovery code, takes any code that was written, sends it as one and goes on', async () => {
    const { completeTwoFactorLogin, onSuccess } = renderStep();

    await userEvent.click(screen.getByRole('button', { name: 'Usar un código de recuperación' }));

    expect(screen.getByText('Ingresa uno de tus códigos de recuperación. Cada código solo puede usarse una vez.')).toBeInTheDocument();
    const field = screen.getByLabelText('Código de recuperación');
    expect(field).toHaveFocus();
    expect(field).toHaveAttribute('autocomplete', 'off');
    expect(field).toHaveAttribute('inputmode', 'text');
    expect(field).toHaveAttribute('maxlength', '32');

    await verify();
    expect(await screen.findByText('El código de recuperación es requerido')).toBeInTheDocument();
    expect(completeTwoFactorLogin).not.toHaveBeenCalled();

    await userEvent.type(field, 'ABCDE-12345');
    await verify();

    expect(completeTwoFactorLogin).toHaveBeenCalledWith({ twoFactorChallengeToken: 'reto-123', code: 'ABCDE-12345', useRecoveryCode: true });
    expect(onSuccess).toHaveBeenCalledOnce();
  });

  it('switching to a recovery code takes away the 6-digit warning of the field', async () => {
    renderStep();

    await userEvent.type(codeField(), '12345');
    await verify();
    expect(await screen.findByText('El código debe tener 6 dígitos')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Usar un código de recuperación' }));

    expect(screen.queryByText('El código debe tener 6 dígitos')).not.toBeInTheDocument();
    expect(codeField()).toHaveAttribute('aria-invalid', 'false');
    expect(codeField()).not.toHaveAccessibleDescription();
  });

  it('switching between the app and a recovery code empties the field and takes away the error', async () => {
    const { completeTwoFactorLogin } = renderStep(vi.fn().mockRejectedValueOnce(refused('Código de autenticación inválido.')));

    await userEvent.type(codeField(), '111111');
    await verify();
    expect(await screen.findByRole('alert')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Usar un código de recuperación' }));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Código de recuperación')).toHaveValue('');

    await userEvent.type(codeField(), 'ABCDE-12345');
    await userEvent.click(screen.getByRole('button', { name: 'Usar el código de la app de autenticación' }));
    expect(screen.getByLabelText('Código de verificación')).toHaveValue('');
    expect(screen.getByLabelText('Código de verificación')).toHaveFocus();
    expect(screen.getByText('Ingresa el código de 6 dígitos de tu app de autenticación.')).toBeInTheDocument();

    // Back with the app, the 6 digits are checked again.
    await userEvent.type(codeField(), 'ABCDEF');
    await verify();
    expect(await screen.findByText('El código debe tener 6 dígitos')).toBeInTheDocument();
    expect(completeTwoFactorLogin).toHaveBeenCalledOnce();
  });

  it('while it verifies, nothing else can be pressed: not again, not the other kind of code, not back', async () => {
    const pending = deferred();
    const { completeTwoFactorLogin, onSuccess, onCancel } = renderStep(vi.fn().mockReturnValueOnce(pending.promise));

    await userEvent.type(codeField(), '123456');
    await verify();

    expect(screen.getByRole('button', { name: 'Verificando...' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Usar un código de recuperación' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Volver al inicio de sesión' })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'Verificando...' }));
    await userEvent.click(screen.getByRole('button', { name: 'Usar un código de recuperación' }));
    await userEvent.click(screen.getByRole('button', { name: 'Volver al inicio de sesión' }));
    await userEvent.type(codeField(), '{Enter}');
    expect(completeTwoFactorLogin).toHaveBeenCalledOnce();
    expect(onCancel).not.toHaveBeenCalled();
    expect(screen.getByLabelText('Código de verificación')).toBeInTheDocument();

    pending.resolve();
    expect(await screen.findByRole('button', { name: 'Verificar' })).toBeEnabled();
    expect(onSuccess).toHaveBeenCalledOnce();
  });

  it('«Volver al inicio de sesión» goes back without sending anything', async () => {
    const { completeTwoFactorLogin, onCancel } = renderStep();

    await userEvent.type(codeField(), '123456');
    await userEvent.click(screen.getByRole('button', { name: 'Volver al inicio de sesión' }));

    expect(onCancel).toHaveBeenCalledOnce();
    expect(completeTwoFactorLogin).not.toHaveBeenCalled();
  });
});
