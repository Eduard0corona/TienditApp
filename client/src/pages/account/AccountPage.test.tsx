import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { accountApi, type AccountAddress } from '../../api/accountApi';
import ConfirmProvider from '../../components/feedback/ConfirmProvider';
import ToastProvider from '../../components/feedback/ToastProvider';
import { AuthContext, type AuthContextType } from '../../contexts/AuthContextDefinition';
import AccountPage from './AccountPage';

// The title of the customer's frame only: its section tabs read the business, which a customer has none of.
vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/accountApi', () => ({
  MAX_ACCOUNT_ADDRESSES: 10,
  accountApi: { getAddresses: vi.fn(), saveAddress: vi.fn(), updateAddress: vi.fn(), deleteAddress: vi.fn() },
}));

const api = vi.mocked(accountApi);

const casa: AccountAddress = {
  id: 'a1', label: 'Casa', postalCode: '72000', addressLine: 'Av. Juárez 120', references: 'Portón verde', lastUsedAt: '2026-10-12T19:00:00Z',
};

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const auth = {
    isAuthenticated: true,
    isLoading: false,
    user: { id: 'u1', email: 'mariana@demo.com', firstName: 'Mariana', lastName: 'Ortiz', phoneNumber: '+5212221234567', role: 'Customer' },
  } as unknown as AuthContextType;
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthContext.Provider value={auth}>
        <MemoryRouter>
          <ToastProvider>
            <ConfirmProvider>
              <AccountPage />
            </ConfirmProvider>
          </ToastProvider>
        </MemoryRouter>
      </AuthContext.Provider>
    </QueryClientProvider>,
  );
}

describe('AccountPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('shows the account and its addresses, and adds one', async () => {
    api.getAddresses.mockResolvedValueOnce([casa]).mockResolvedValue([
      casa,
      { id: 'a2', label: 'Oficina', postalCode: '72160', addressLine: 'Calle 5 de Mayo 8', references: null, lastUsedAt: null },
    ]);
    api.saveAddress.mockResolvedValue({ id: 'a2', label: 'Oficina', postalCode: '72160', addressLine: 'Calle 5 de Mayo 8', references: null, lastUsedAt: null });

    renderPage();

    expect(await screen.findByText('Av. Juárez 120 · CP 72000')).toBeInTheDocument();
    expect(screen.getByText('mariana@demo.com')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Cambiar tu contraseña o tu teléfono' })).toHaveAttribute('href', '/profile');

    await userEvent.click(screen.getByRole('button', { name: 'Agregar dirección' }));
    const dialog = screen.getByRole('dialog', { name: 'Agregar una dirección' });
    const save = within(dialog).getByRole('button', { name: 'Guardar' });
    expect(save).toBeDisabled();
    await userEvent.type(within(dialog).getByLabelText('Nombre (opcional)'), 'Oficina');
    await userEvent.type(within(dialog).getByLabelText('Código postal'), '7216');
    expect(within(dialog).getByText('El código postal lleva 5 dígitos.')).toBeInTheDocument();
    await userEvent.type(within(dialog).getByLabelText('Código postal'), '0');
    await userEvent.type(within(dialog).getByLabelText('Dirección'), ' Calle 5 de Mayo 8 ');
    await userEvent.click(save);

    expect(api.saveAddress).toHaveBeenCalledWith({ label: 'Oficina', postalCode: '72160', addressLine: 'Calle 5 de Mayo 8', references: null });
    expect(await screen.findByText('Calle 5 de Mayo 8 · CP 72160')).toBeInTheDocument();
  });

  it('changes an address and deletes another after asking', async () => {
    api.getAddresses.mockResolvedValue([casa]);
    api.updateAddress.mockResolvedValue({ ...casa, references: 'Portón negro' });
    api.deleteAddress.mockResolvedValue(undefined as never);

    renderPage();

    await userEvent.click(await screen.findByRole('button', { name: 'Cambiar Casa' }));
    const dialog = screen.getByRole('dialog', { name: 'Cambiar Casa' });
    const references = within(dialog).getByLabelText('Referencias (opcional)');
    await userEvent.clear(references);
    await userEvent.type(references, 'Portón negro');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));
    expect(api.updateAddress).toHaveBeenCalledWith('a1', { label: 'Casa', postalCode: '72000', addressLine: 'Av. Juárez 120', references: 'Portón negro' });

    await userEvent.click(await screen.findByRole('button', { name: 'Borrar Casa' }));
    const confirm = await screen.findByRole('dialog', { name: 'Borrar dirección' });
    expect(confirm).toHaveTextContent('Los pedidos que ya enviaste ahí no cambian.');
    await userEvent.click(within(confirm).getByRole('button', { name: 'Borrar' }));
    expect(api.deleteAddress).toHaveBeenCalledWith('a1');
  });

  it('does not offer an eleventh address', async () => {
    api.getAddresses.mockResolvedValue(Array.from({ length: 10 }, (_, index) => ({ ...casa, id: `a${index}`, label: `Dirección ${index}` })));

    renderPage();

    expect(await screen.findByText('Guardas hasta 10 direcciones: borra una para agregar otra.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Agregar dirección' })).toBeDisabled();
  });
});
