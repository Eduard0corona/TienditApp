import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { profileApi } from '../../api/profileApi';
import { apiRequest } from '../../api/typedClient';
import { AuthContext, type AuthContextType } from '../../contexts/AuthContextDefinition';
import { useTenant } from '../../hooks/useTenant';
import { TenantCapability, type UserProfileDto } from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import Profile from './Profile';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/profileApi', () => ({
  profileApi: { updateProfile: vi.fn(), uploadAvatar: vi.fn(), deleteAvatar: vi.fn(), changePassword: vi.fn(), disableTwoFactor: vi.fn() },
}));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
// What reaches the person by email has its own tests: here, only whom it is offered to.
vi.mock('../../components/profile/NoticeEmailsCard', () => ({
  default: ({ agenda, quotes, orders, ownsBusiness }: { agenda: boolean; quotes: boolean; orders: boolean; ownsBusiness: boolean }) => (
    <section aria-label="Avisos por correo">
      {[agenda && 'agenda', quotes && 'cotizaciones', orders && 'pedidos', ownsBusiness && 'dueño'].filter(Boolean).join(', ')}
    </section>
  ),
}));

const api = vi.mocked(profileApi);

const ANA: UserProfileDto = {
  id: 'u1', email: 'ana@cafelaesquina.mx', firstName: 'Ana', lastName: 'López', fullName: 'Ana López', phoneNumber: '+525512345678',
  phoneNumberConfirmed: true, emailConfirmed: true, role: 'TenantUser', tenantRole: 'Staff', permissions: [], twoFactorEnabled: false,
  createdAt: '2026-01-15T18:00:00Z', avatarUrl: null, avatarImages: null,
} as unknown as UserProfileDto;

function renderProfile(
  user: Partial<UserProfileDto> = {},
  { capabilities = TenantCapability.Retail, tenant = true }: { capabilities?: number; tenant?: boolean } = {},
) {
  const auth = {
    isAuthenticated: true,
    isLoading: false,
    user: { ...ANA, ...user },
    refreshProfile: vi.fn().mockResolvedValue(undefined),
    logout: vi.fn().mockResolvedValue(undefined),
  } as unknown as AuthContextType;
  vi.mocked(useTenant).mockReturnValue({
    tenant: tenant ? { name: 'Café La Esquina' } : null,
    capabilities,
  } as unknown as ReturnType<typeof useTenant>);
  const view = render(
    <AuthContext.Provider value={auth}>
      <Profile />
    </AuthContext.Provider>,
  );
  return { ...view, auth };
}

describe('Profile', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    setBusinessTimeZone('America/Mexico_City');
  });

  afterEach(() => {
    vi.useRealTimers();
    setBusinessTimeZone(null);
  });

  it('shows who the person is: name, email, phone, role, two-step verification and since when', () => {
    renderProfile();

    expect(screen.getByRole('heading', { name: 'Mi perfil' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Ana López' })).toBeInTheDocument();
    const personal = screen.getByRole('heading', { name: 'Información personal' }).parentElement as HTMLElement;
    expect(personal).toHaveTextContent('Nombre completo: Ana López');
    expect(personal).toHaveTextContent('Correo: ana@cafelaesquina.mxConfirmado');
    expect(personal).toHaveTextContent('Teléfono: +525512345678');
    expect(personal).toHaveTextContent('Rol: Empleado');
    expect(personal).toHaveTextContent('2FA: Desactivado');
    expect(personal).toHaveTextContent('Miembro desde: 15 de enero de 2026');
    // Without two-step verification there is nothing to turn off.
    expect(screen.queryByRole('heading', { name: 'Verificación en dos pasos' })).not.toBeInTheDocument();
  });

  it('saves the name, reads the profile again and leaves the form, saying so where it is announced', async () => {
    const user = userEvent.setup();
    api.updateProfile.mockResolvedValue({ data: {} } as never);
    const { auth } = renderProfile();
    const personal = screen.getByRole('heading', { name: 'Información personal' }).parentElement as HTMLElement;
    // On the page from the start, so what appears in it is announced.
    const status = within(personal).getByRole('status');
    expect(status).toBeEmptyDOMElement();

    await user.click(screen.getByRole('button', { name: 'Editar perfil' }));
    const name = screen.getByLabelText('Nombre');
    await user.clear(name);
    await user.type(name, 'Ana María');
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => expect(status).toHaveTextContent('Perfil actualizado correctamente.'));
    expect(api.updateProfile).toHaveBeenCalledWith({ firstName: 'Ana María', lastName: 'López' });
    expect(auth.refreshProfile).toHaveBeenCalled();
    expect(screen.queryByLabelText('Nombre')).not.toBeInTheDocument();
    // The form went away: the focus goes back to what opened it.
    expect(screen.getByRole('button', { name: 'Editar perfil' })).toHaveFocus();
    // Editing again, what it said goes away.
    await user.click(screen.getByRole('button', { name: 'Editar perfil' }));
    expect(status).toBeEmptyDOMElement();
  });

  it('cannot be cancelled while the name is being saved, so it is not saved twice', async () => {
    const user = userEvent.setup();
    let saved: (value: unknown) => void = () => undefined;
    api.updateProfile.mockReturnValue(new Promise((resolve) => { saved = resolve; }) as never);
    renderProfile();

    await user.click(screen.getByRole('button', { name: 'Editar perfil' }));
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    expect(screen.getByRole('button', { name: 'Guardando...' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cancelar la edición del perfil' })).toBeDisabled();
    saved({ data: {} });
    expect(await screen.findByText('Perfil actualizado correctamente.')).toBeInTheDocument();
    expect(api.updateProfile).toHaveBeenCalledTimes(1);
  });

  it('keeps the form and says why when the name could not be saved', async () => {
    const user = userEvent.setup();
    api.updateProfile.mockRejectedValue({ isAxiosError: true, response: { status: 400, data: { detail: 'El nombre es muy largo.' } } });
    renderProfile();

    await user.click(screen.getByRole('button', { name: 'Editar perfil' }));
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('El nombre es muy largo.');
    expect(screen.getByLabelText('Nombre')).toBeInTheDocument();
    // Cancelled, the error goes with the edit.
    await user.click(screen.getByRole('button', { name: 'Cancelar la edición del perfil' }));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('asks for the name and the last name instead of doing nothing when they are empty or only spaces', async () => {
    const user = userEvent.setup();
    renderProfile();

    await user.click(screen.getByRole('button', { name: 'Editar perfil' }));
    await user.clear(screen.getByLabelText('Nombre'));
    await user.type(screen.getByLabelText('Nombre'), '   ');
    await user.clear(screen.getByLabelText('Apellido'));
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    expect(await screen.findByText('Escribe tu nombre.')).toBeInTheDocument();
    expect(screen.getByLabelText('Nombre')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByLabelText('Nombre')).toHaveAccessibleDescription('Escribe tu nombre.');
    expect(screen.getByLabelText('Apellido')).toHaveAccessibleDescription('Escribe tu apellido.');
    expect(screen.getByLabelText('Nombre')).toHaveAttribute('maxLength', '100');
    expect(api.updateProfile).not.toHaveBeenCalled();
  });

  it('forgets what was typed when the edit is cancelled', async () => {
    const user = userEvent.setup();
    renderProfile();

    await user.click(screen.getByRole('button', { name: 'Editar perfil' }));
    await user.clear(screen.getByLabelText('Nombre'));
    await user.type(screen.getByLabelText('Nombre'), 'Otra');
    await user.click(screen.getByRole('button', { name: 'Cancelar la edición del perfil' }));
    await user.click(screen.getByRole('button', { name: 'Editar perfil' }));

    expect(screen.getByLabelText('Nombre')).toHaveValue('Ana');
  });

  it('uploads a photo and reads the profile again, and deletes it without saying it is uploading', async () => {
    const user = userEvent.setup();
    api.uploadAvatar.mockResolvedValue({ data: {} } as never);
    let deleted: (value: unknown) => void = () => undefined;
    api.deleteAvatar.mockReturnValue(new Promise((resolve) => { deleted = resolve; }) as never);
    const { auth, container } = renderProfile({ avatarUrl: '/files/avatar.png' });
    const photo = new File(['png'], 'yo.png', { type: 'image/png' });

    await user.upload(container.querySelector('input[type="file"]') as HTMLInputElement, photo);

    expect(await screen.findByText('Avatar actualizado.')).toBeInTheDocument();
    expect(api.uploadAvatar).toHaveBeenCalledWith(photo);
    expect(auth.refreshProfile).toHaveBeenCalledTimes(1);

    await user.click(screen.getByRole('button', { name: 'Eliminar' }));
    expect(screen.getByRole('button', { name: 'Eliminar' })).toBeDisabled();
    expect(screen.queryByText('Subiendo avatar')).not.toBeInTheDocument();
    deleted({ data: {} });
    expect(await screen.findByText('Avatar eliminado.')).toBeInTheDocument();
    expect(auth.refreshProfile).toHaveBeenCalledTimes(2);
  });

  it('says why the photo could not be deleted', async () => {
    const user = userEvent.setup();
    api.deleteAvatar.mockRejectedValue({ isAxiosError: true, response: { status: 404, data: { detail: 'No tienes foto de perfil.' } } });
    renderProfile({ avatarUrl: '/files/avatar.png' });

    await user.click(screen.getByRole('button', { name: 'Eliminar' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('No tienes foto de perfil.');
  });

  it('offers to delete the photo only when there is one', () => {
    renderProfile();

    expect(screen.getByRole('button', { name: 'Subir foto' })).toBeEnabled();
    expect(screen.queryByRole('button', { name: 'Eliminar' })).not.toBeInTheDocument();
  });

  it('changes the password and signs out a moment later, since every session ended', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    api.changePassword.mockResolvedValue({ data: {} } as never);
    const { auth } = renderProfile();

    await user.type(screen.getByLabelText('Contraseña actual'), 'Vieja123!');
    await user.type(screen.getByLabelText('Nueva contraseña'), 'Nueva-Segura-2026');
    await user.click(screen.getByRole('button', { name: 'Cambiar contraseña' }));

    const card = screen.getByRole('heading', { name: 'Cambiar contraseña' }).parentElement as HTMLElement;
    await waitFor(() => expect(within(card).getByRole('status')).toHaveTextContent('Contraseña actualizada. Cerramos tus sesiones en todos los dispositivos'));
    expect(api.changePassword).toHaveBeenCalledWith({ currentPassword: 'Vieja123!', newPassword: 'Nueva-Segura-2026' });
    expect(screen.getByLabelText('Contraseña actual')).toHaveValue('');
    expect(auth.logout).not.toHaveBeenCalled();
    await act(() => vi.advanceTimersByTimeAsync(3000));
    expect(auth.logout).toHaveBeenCalledTimes(1);
  });

  it('asks for both passwords and says why the server refused the change, without signing out', async () => {
    const user = userEvent.setup();
    api.changePassword.mockRejectedValue({ isAxiosError: true, response: { status: 400, data: { detail: 'La contraseña actual no es correcta.' } } });
    const { auth } = renderProfile();

    await user.click(screen.getByRole('button', { name: 'Cambiar contraseña' }));
    expect(screen.getAllByText('Requerido')).toHaveLength(2);
    for (const label of ['Contraseña actual', 'Nueva contraseña']) {
      expect(screen.getByLabelText(label)).toHaveAttribute('aria-invalid', 'true');
      expect(screen.getByLabelText(label)).toHaveAccessibleDescription('Requerido');
    }
    expect(api.changePassword).not.toHaveBeenCalled();

    await user.type(screen.getByLabelText('Contraseña actual'), 'Otra123!');
    await user.type(screen.getByLabelText('Nueva contraseña'), 'Nueva-Segura-2026');
    await user.click(screen.getByRole('button', { name: 'Cambiar contraseña' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('La contraseña actual no es correcta.');
    expect(auth.logout).not.toHaveBeenCalled();
  });

  describe('two-step verification', () => {
    it('turns it off with the code of the app, and says so once the profile no longer has it', async () => {
      const user = userEvent.setup();
      api.disableTwoFactor.mockResolvedValue({ data: {} } as never);
      const { auth, rerender } = renderProfile({ twoFactorEnabled: true });

      const card = screen.getByRole('heading', { name: 'Verificación en dos pasos' }).parentElement as HTMLElement;
      await user.type(within(card).getByLabelText('Código de verificación'), '12345');
      await user.click(within(card).getByRole('button', { name: 'Desactivar 2FA' }));
      expect(within(card).getByText('El código debe tener 6 dígitos')).toBeInTheDocument();
      await user.type(within(card).getByLabelText('Código de verificación'), '6');
      await user.click(within(card).getByRole('button', { name: 'Desactivar 2FA' }));

      await waitFor(() => expect(auth.refreshProfile).toHaveBeenCalled());
      expect(api.disableTwoFactor).toHaveBeenCalledWith({ code: '123456', useRecoveryCode: false });
      // The profile read again says it is off: the card stays only to say it.
      rerender(
        <AuthContext.Provider value={{ ...auth, user: { ...ANA, twoFactorEnabled: false } }}>
          <Profile />
        </AuthContext.Provider>,
      );
      const twoFactor = screen.getByRole('heading', { name: 'Verificación en dos pasos' }).parentElement as HTMLElement;
      expect(within(twoFactor).getByRole('status')).toHaveTextContent('Verificación en dos pasos desactivada.');
      expect(screen.queryByRole('button', { name: 'Desactivar 2FA' })).not.toBeInTheDocument();
    });

    it('turns it off with a recovery code, and says why a code was refused', async () => {
      const user = userEvent.setup();
      api.disableTwoFactor.mockRejectedValueOnce({ isAxiosError: true, response: { status: 400, data: { detail: 'El código no es válido.' } } })
        .mockResolvedValue({ data: {} } as never);
      renderProfile({ twoFactorEnabled: true });

      await user.click(screen.getByRole('button', { name: 'Usar un código de recuperación' }));
      await user.type(screen.getByLabelText('Código de recuperación'), 'ABCDE-12345');
      await user.click(screen.getByRole('button', { name: 'Desactivar 2FA' }));

      expect(await screen.findByRole('alert')).toHaveTextContent('El código no es válido.');
      expect(api.disableTwoFactor).toHaveBeenCalledWith({ code: 'ABCDE-12345', useRecoveryCode: true });
      // Back to the code of the app, it starts empty.
      await user.click(screen.getByRole('button', { name: 'Usar el código de la app de autenticación' }));
      expect(screen.getByLabelText('Código de verificación')).toHaveValue('');
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });
  });

  describe('phone', () => {
    it('changes the number with the password and the code sent to it', async () => {
      const user = userEvent.setup();
      vi.mocked(apiRequest).mockResolvedValue(undefined as never);
      const { auth } = renderProfile();
      const card = screen.getByRole('heading', { name: 'Teléfono' }).parentElement as HTMLElement;

      expect(card).toHaveTextContent('+525512345678Confirmado');
      await user.click(within(card).getByRole('button', { name: 'Cambiar número' }));
      // A confirmed number starts empty: it is replaced.
      expect(within(card).getByLabelText('Número')).toHaveValue('');
      const send = within(card).getByRole('button', { name: 'Enviar código' });
      expect(send).toBeDisabled();
      await user.type(within(card).getByLabelText('Número'), '5598765432');
      await user.type(within(card).getByLabelText('Tu contraseña'), 'Secreta123!');
      await user.click(send);

      await waitFor(() => expect(within(card).getByRole('status')).toHaveTextContent('Te enviamos un código al número nuevo.'));
      // The focus goes to the field of the new step.
      expect(within(card).getByLabelText('Código que llegó a 5598765432')).toHaveFocus();
      expect(apiRequest).toHaveBeenCalledWith('post', '/api/v1/auth/profile/phone', {
        body: { phoneNumber: '5598765432', currentPassword: 'Secreta123!' },
      });
      const confirm = within(card).getByRole('button', { name: 'Confirmar' });
      await user.type(within(card).getByLabelText('Código que llegó a 5598765432'), '1234');
      expect(confirm).toBeDisabled();
      await user.type(within(card).getByLabelText('Código que llegó a 5598765432'), '56');
      await user.click(confirm);

      expect(await within(card).findByText('Número confirmado. Con él puedes recuperar tu contraseña por SMS.')).toBeInTheDocument();
      expect(apiRequest).toHaveBeenLastCalledWith('post', '/api/v1/auth/profile/phone/confirm', {
        body: { phoneNumber: '5598765432', code: '123456' },
      });
      expect(auth.refreshProfile).toHaveBeenCalled();
    });

    it('proposes the unconfirmed number to confirm it, and says why the code was refused', async () => {
      const user = userEvent.setup();
      vi.mocked(apiRequest).mockResolvedValueOnce(undefined as never)
        .mockRejectedValueOnce({ isAxiosError: true, response: { status: 400, data: { detail: 'El código ya expiró.' } } });
      renderProfile({ phoneNumberConfirmed: false });
      const card = screen.getByRole('heading', { name: 'Teléfono' }).parentElement as HTMLElement;

      expect(card).toHaveTextContent('+525512345678Sin confirmar');
      await user.click(within(card).getByRole('button', { name: 'Confirmar o cambiar número' }));
      expect(within(card).getByLabelText('Número')).toHaveValue('+525512345678');
      await user.type(within(card).getByLabelText('Tu contraseña'), 'Secreta123!');
      await user.click(within(card).getByRole('button', { name: 'Enviar código' }));
      await user.type(await within(card).findByLabelText('Código que llegó a +525512345678'), '654321');
      await user.click(within(card).getByRole('button', { name: 'Confirmar' }));

      expect(await within(card).findByRole('alert')).toHaveTextContent('El código ya expiró.');
      // Back, the password is asked again before another code is sent, without what the previous one said.
      await user.click(within(card).getByRole('button', { name: 'Atrás' }));
      expect(within(card).getByLabelText('Tu contraseña')).toHaveValue('');
      expect(within(card).getByLabelText('Número')).toHaveFocus();
      expect(within(card).queryByRole('alert')).not.toBeInTheDocument();
      expect(within(card).getByRole('status')).toBeEmptyDOMElement();
      // Sent again, the code starts empty.
      vi.mocked(apiRequest).mockResolvedValueOnce(undefined as never);
      await user.type(within(card).getByLabelText('Tu contraseña'), 'Secreta123!');
      await user.click(within(card).getByRole('button', { name: 'Enviar código' }));
      expect(await within(card).findByLabelText('Código que llegó a +525512345678')).toHaveValue('');
    });

    it('confirms the number the code went to, though the field changed while it was sent', async () => {
      const user = userEvent.setup();
      let sent: (value: unknown) => void = () => undefined;
      vi.mocked(apiRequest).mockReturnValueOnce(new Promise((resolve) => { sent = resolve; }) as never).mockResolvedValue(undefined as never);
      renderProfile();
      const card = screen.getByRole('heading', { name: 'Teléfono' }).parentElement as HTMLElement;
      await user.click(within(card).getByRole('button', { name: 'Cambiar número' }));
      await user.type(within(card).getByLabelText('Número'), '5598765432');
      await user.type(within(card).getByLabelText('Tu contraseña'), 'Secreta123!');
      await user.click(within(card).getByRole('button', { name: 'Enviar código' }));

      await user.type(within(card).getByLabelText('Número'), '9');
      sent(undefined);
      await user.type(await within(card).findByLabelText('Código que llegó a 5598765432'), '123456');
      await user.click(within(card).getByRole('button', { name: 'Confirmar' }));

      await waitFor(() => expect(apiRequest).toHaveBeenLastCalledWith('post', '/api/v1/auth/profile/phone/confirm', {
        body: { phoneNumber: '5598765432', code: '123456' },
      }));
    });

    it('says why the code could not be sent, and forgets it when the change is cancelled', async () => {
      const user = userEvent.setup();
      vi.mocked(apiRequest).mockRejectedValueOnce({ isAxiosError: true, response: { status: 400, data: { detail: 'Tu contraseña no es correcta.' } } });
      renderProfile();
      const card = screen.getByRole('heading', { name: 'Teléfono' }).parentElement as HTMLElement;
      await user.click(within(card).getByRole('button', { name: 'Cambiar número' }));
      await user.type(within(card).getByLabelText('Número'), '5598765432');
      await user.type(within(card).getByLabelText('Tu contraseña'), 'Otra');
      await user.click(within(card).getByRole('button', { name: 'Enviar código' }));
      expect(await within(card).findByRole('alert')).toHaveTextContent('Tu contraseña no es correcta.');

      await user.click(within(card).getByRole('button', { name: 'Cancelar el cambio de número' }));

      expect(within(card).queryByRole('alert')).not.toBeInTheDocument();
      expect(within(card).getByRole('button', { name: 'Cambiar número' })).toHaveFocus();
    });
  });

  describe('notices by email', () => {
    it.each([
      ['answers the agenda', ['ManageAppointments'], TenantCapability.Appointments, 'agenda'],
      ['works the quotes of the services', ['ManageOrders'], TenantCapability.Service, 'cotizaciones, pedidos'],
      ['confirms the orders', ['ManageOrders'], TenantCapability.Retail, 'pedidos'],
    ])('are offered to whoever %s', (_, permissions, capabilities, offered) => {
      renderProfile({ permissions }, { capabilities });

      expect(screen.getByRole('region', { name: 'Avisos por correo' }).textContent).toBe(offered);
    });

    it('says the owner chooses for the team', () => {
      renderProfile({ role: 'TenantAdmin', tenantRole: 'Owner' }, { capabilities: TenantCapability.Retail });

      expect(screen.getByRole('region', { name: 'Avisos por correo' }).textContent).toBe('pedidos, dueño');
    });

    it.each([
      ['without any of those jobs', {}, true],
      ['to the platform\'s admin acting in a business', { role: 'SuperAdmin' }, true],
      ['outside a business', { permissions: ['ManageOrders'] }, false],
    ])('are not offered %s', (_, user, tenant) => {
      renderProfile(user, { capabilities: TenantCapability.Retail | TenantCapability.Appointments, tenant });

      expect(screen.queryByRole('region', { name: 'Avisos por correo' })).not.toBeInTheDocument();
    });
  });
});
