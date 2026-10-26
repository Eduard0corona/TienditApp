import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { authApi } from '../../api/authApi';
import Register from './Register';

vi.mock('../../api/authApi', () => ({
  authApi: {
    registerBusiness: vi.fn(),
    registerCustomer: vi.fn(),
  },
}));

const api = vi.mocked(authApi);

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/register']}>
      <Register />
    </MemoryRouter>
  );
}

async function fillCustomer(user: ReturnType<typeof userEvent.setup>, phone: string) {
  await user.click(screen.getByRole('button', { name: /Soy cliente/ }));
  await user.type(screen.getByLabelText('Nombre *'), 'Ana');
  await user.type(screen.getByLabelText('Apellido *'), 'Cliente');
  await user.type(screen.getByLabelText('Correo *'), 'ana@demo.com');
  await user.type(screen.getByLabelText('Teléfono *'), phone);
  await user.type(screen.getByLabelText('Contraseña *'), 'Secreto12');
}

describe('Register', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('signs up a customer with the ten digits of the phone, without the +52', async () => {
    api.registerCustomer.mockResolvedValue({ data: { message: 'ok' } } as Awaited<ReturnType<typeof authApi.registerCustomer>>);
    const user = userEvent.setup();
    renderPage();

    await fillCustomer(user, '55 1234-5678');
    expect(screen.getByLabelText('Teléfono *')).toHaveAttribute('placeholder', '10 dígitos');
    await user.click(screen.getByRole('button', { name: 'Crear cuenta' }));

    expect(api.registerCustomer).toHaveBeenCalledWith(expect.objectContaining({ phoneNumber: '5512345678' }));
  });

  it('asks for the ten digits when the phone has the international prefix or is short', async () => {
    const user = userEvent.setup();
    renderPage();

    await fillCustomer(user, '+5215512345678');
    await user.click(screen.getByRole('button', { name: 'Crear cuenta' }));

    expect(await screen.findByText('Escribe los 10 dígitos de tu teléfono')).toBeInTheDocument();

    await user.clear(screen.getByLabelText('Teléfono *'));
    await user.type(screen.getByLabelText('Teléfono *'), '551234567');
    await user.click(screen.getByRole('button', { name: 'Crear cuenta' }));

    expect(screen.getByText('Escribe los 10 dígitos de tu teléfono')).toBeInTheDocument();
    expect(api.registerCustomer).not.toHaveBeenCalled();
  });

  it('signs up a business owner with the ten digits of the phone', async () => {
    api.registerBusiness.mockResolvedValue({ data: { message: 'ok' } } as Awaited<ReturnType<typeof authApi.registerBusiness>>);
    const user = userEvent.setup();
    renderPage();

    await user.click(screen.getByRole('button', { name: /Tengo un negocio/ }));
    await user.type(screen.getByLabelText('Nombre *'), 'Ana');
    await user.type(screen.getByLabelText('Apellido *'), 'Dueña');
    await user.type(screen.getByLabelText('Correo *'), 'ana@demo.com');
    await user.type(screen.getByLabelText('Teléfono *'), '5511112222');
    await user.type(screen.getByLabelText('Contraseña *'), 'Secreto12');
    await user.type(screen.getByLabelText('Nombre del negocio *'), 'Taquería Demo');
    await user.click(screen.getByRole('button', { name: 'Registrar negocio' }));

    expect(api.registerBusiness).toHaveBeenCalledWith(expect.objectContaining({ phoneNumber: '5511112222' }));
  });
});
