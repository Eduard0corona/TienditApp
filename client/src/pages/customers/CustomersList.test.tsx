import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { customersApi } from '../../api/customersApi';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { TenantCapability, type CustomerDto } from '../../types';
import CustomersList from './CustomersList';

const dialogs = vi.hoisted(() => ({ confirm: vi.fn() }));
const fiscal = vi.hoisted(() => ({ changed: true }));

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => dialogs }));
vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../api/customersApi', () => ({
  customersApi: { getAll: vi.fn(), create: vi.fn(), update: vi.fn(), setActive: vi.fn(), delete: vi.fn() },
}));
// The card, the credit and the fiscal data are dialogs of their own, with their own tests: here, only who opens them and
// what is read again when they close.
vi.mock('../../components/customers/CustomerCardModal', () => ({
  default: ({ name, onClose }: { name: string; onClose: () => void }) => (
    <div role="dialog" aria-label={`Ficha de ${name}`}><button type="button" onClick={onClose}>Cerrar ficha</button></div>
  ),
}));
vi.mock('../../components/customers/CreditLimitModal', () => ({
  default: ({ customer, onClose }: { customer: { name: string }; onClose: () => void }) => (
    <div role="dialog" aria-label={`Fiado de ${customer.name}`}><button type="button" onClick={onClose}>Cerrar fiado</button></div>
  ),
}));
vi.mock('../../components/customers/CustomerFiscalDataModal', () => ({
  default: ({ customer, onClose }: { customer: { name: string }; onClose: (changed: boolean) => void }) => (
    <div role="dialog" aria-label={`Datos fiscales de ${customer.name}`}>
      <button type="button" onClick={() => onClose(fiscal.changed)}>Cerrar datos fiscales</button>
    </div>
  ),
}));

function customer(patch: Partial<CustomerDto>): CustomerDto {
  return { id: 'c', name: 'Cliente', phone: '+525512345678', isPublicGeneral: false, isActive: true, ...patch } as CustomerDto;
}

/** The counter sale, a customer with credit, a term and an RFC, one without a phone, and one turned off. */
const counter = customer({ id: 'public', name: 'Público en general', phone: undefined, isPublicGeneral: true });
const marta = customer({
  id: 'marta', name: 'Marta Ruiz', email: 'marta@correo.mx', notes: 'Sin azúcar', creditLimit: 1500, creditTermDays: 15,
  fiscalData: { rfc: 'RUMM800101AB1', name: 'MARTA RUIZ MORA' } as CustomerDto['fiscalData'], invoiceEmail: 'facturas@ruiz.mx',
});
const luis = customer({ id: 'luis', name: 'Luis Peña', phone: undefined, creditTermDays: 1 });
const ana = customer({ id: 'ana', name: 'Ana Gil', phone: '+525598765432', isActive: false });

const listPage = (items: CustomerDto[], totalCount = items.length) =>
  ({ data: { items, pageNumber: 1, totalPages: Math.ceil(totalCount / 10), totalCount, hasPreviousPage: false, hasNextPage: totalCount > 10 } }) as never;

const row = async (name: string) => (await screen.findByText(name, { selector: '.fw-semibold' })).closest('tr') as HTMLElement;

function renderList({ path = '/customers', role = 'TenantAdmin' }: { path?: string; role?: string } = {}) {
  vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role, permissions: ['ManageCustomers'] } } as unknown as ReturnType<typeof useAuth>);
  return render(<MemoryRouter initialEntries={[path]}><CustomersList /></MemoryRouter>);
}

describe('CustomersList', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    fiscal.changed = true;
    vi.mocked(useTenant).mockReturnValue({ capabilities: TenantCapability.Retail } as unknown as ReturnType<typeof useTenant>);
    vi.mocked(customersApi.getAll).mockResolvedValue(listPage([counter, marta, luis, ana]));
  });

  it('lists each customer with what they may owe, their RFC, their contact, their type and whether they are active', async () => {
    renderList();

    const cells = (tr: HTMLElement) => within(tr).getAllByRole('cell').slice(0, 5).map((cell) => cell.textContent);
    expect(cells(await row('Marta Ruiz')))
      .toEqual(['Marta RuizFiado hasta $1,500.00Paga en 15 díasRFC RUMM800101AB1', '+525512345678', 'marta@correo.mx', 'Registrado', 'Activo']);
    expect(cells(await row('Luis Peña'))).toEqual(['Luis PeñaPaga en 1 día', '-', '-', 'Registrado', 'Activo']);
    expect(cells(await row('Ana Gil'))).toEqual(['Ana Gil', '+525598765432', '-', 'Registrado', 'Inactivo']);
    expect(cells(await row('Público en general'))).toEqual(['Público en general', '-', '-', 'Público general', 'Activo']);
    expect(within(await row('Ana Gil')).getByRole('button', { name: 'Activar Ana Gil' })).toBeInTheDocument();
    expect(customersApi.getAll).toHaveBeenCalledWith({ pageNumber: 1, pageSize: 10 });
  });

  it('leaves the counter sale untouched: no card, edit, credit, turning off or deleting', async () => {
    renderList();

    const actions = within(await row('Público en general')).getAllByRole('button');
    expect(actions.map((button) => button.textContent)).toEqual(['Ficha', 'Editar', 'Fiado', 'Desactivar', 'Eliminar']);
    actions.forEach((button) => expect(button).toBeDisabled());
  });

  it('opens with what the global search brought, and asks the server for the status and what is searched from the first page', async () => {
    const user = userEvent.setup();
    vi.mocked(customersApi.getAll).mockResolvedValue(listPage([marta, luis], 25));
    renderList({ path: '/customers?search=%20Marta%20' });

    await row('Marta Ruiz');
    expect(customersApi.getAll).toHaveBeenCalledWith({ pageNumber: 1, pageSize: 10, search: 'Marta' });
    expect(screen.getByLabelText('Nombre, teléfono o correo')).toHaveValue('Marta');

    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
    await waitFor(() => expect(customersApi.getAll).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 2 })));
    await user.selectOptions(screen.getByLabelText('Estado'), 'false');
    await waitFor(() => expect(customersApi.getAll).toHaveBeenLastCalledWith({ pageNumber: 1, pageSize: 10, search: 'Marta', isActive: false }));
    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
    await user.clear(screen.getByLabelText('Nombre, teléfono o correo'));
    await user.type(screen.getByLabelText('Nombre, teléfono o correo'), '5598');
    await waitFor(() => expect(customersApi.getAll).toHaveBeenLastCalledWith({ pageNumber: 1, pageSize: 10, search: '5598', isActive: false }));
  });

  it('creates a customer with the phone trimmed, and reads the list again', async () => {
    const user = userEvent.setup();
    vi.mocked(customersApi.create).mockResolvedValue({ data: {} } as never);
    renderList();
    await row('Marta Ruiz');

    await user.click(screen.getByRole('button', { name: 'Nuevo cliente' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo cliente' });
    await user.type(within(dialog).getByLabelText('Nombre *'), 'Rosa Díaz');
    await user.type(within(dialog).getByLabelText('Teléfono *'), '  55 2222 3333 ');
    await user.type(within(dialog).getByLabelText('Email'), 'rosa@correo.mx');
    await user.type(within(dialog).getByLabelText('Notas'), 'Leche de avena');
    // A new customer has no fiscal data to change yet.
    expect(within(dialog).queryByRole('button', { name: /datos fiscales/ })).not.toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Crear cliente' }));

    await waitFor(() => expect(customersApi.create).toHaveBeenCalledWith({
      name: 'Rosa Díaz', phone: '55 2222 3333', email: 'rosa@correo.mx', notes: 'Leche de avena',
    }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nuevo cliente' })).not.toBeInTheDocument());
    expect(customersApi.getAll).toHaveBeenCalledTimes(2);
  });

  it('asks for the name and a phone before sending, inside the dialog', async () => {
    const user = userEvent.setup();
    renderList();
    await row('Marta Ruiz');

    await user.click(screen.getByRole('button', { name: 'Nuevo cliente' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo cliente' });
    await user.click(within(dialog).getByRole('button', { name: 'Crear cliente' }));
    expect(await within(dialog).findByText('El nombre es requerido')).toBeInTheDocument();
    expect(within(dialog).getByText('El teléfono es requerido')).toBeInTheDocument();

    // A phone of spaces is no phone.
    await user.type(within(dialog).getByLabelText('Nombre *'), 'Rosa Díaz');
    await user.type(within(dialog).getByLabelText('Teléfono *'), '   ');
    await user.click(within(dialog).getByRole('button', { name: 'Crear cliente' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('El teléfono es obligatorio.');
    expect(screen.getAllByText('El teléfono es obligatorio.')).toHaveLength(1);
    expect(customersApi.create).not.toHaveBeenCalled();
  });

  it('edits a customer from what they have', async () => {
    const user = userEvent.setup();
    vi.mocked(customersApi.update).mockResolvedValue({ data: {} } as never);
    renderList();

    await user.click(within(await row('Marta Ruiz')).getByRole('button', { name: 'Editar Marta Ruiz' }));
    const dialog = await screen.findByRole('dialog', { name: 'Editar cliente' });
    expect(within(dialog).getByLabelText('Nombre *')).toHaveValue('Marta Ruiz');
    expect(within(dialog).getByLabelText('Teléfono *')).toHaveValue('+525512345678');
    expect(within(dialog).getByLabelText('Notas')).toHaveValue('Sin azúcar');
    expect(dialog).toHaveTextContent('RFC RUMM800101AB1 · MARTA RUIZ MORA');
    expect(dialog).toHaveTextContent('Sus facturas le llegan a facturas@ruiz.mx');
    await user.clear(within(dialog).getByLabelText('Email'));
    await user.type(within(dialog).getByLabelText('Email'), 'marta.ruiz@correo.mx');
    await user.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => expect(customersApi.update).toHaveBeenCalledWith('marta', {
      id: 'marta', name: 'Marta Ruiz', phone: '+525512345678', email: 'marta.ruiz@correo.mx', notes: 'Sin azúcar',
    }));
    expect(customersApi.create).not.toHaveBeenCalled();
  });

  it('changes the fiscal data in their own dialog only once the changes of the customer are saved, and reads the list again if they changed', async () => {
    const user = userEvent.setup();
    renderList();

    await user.click(within(await row('Marta Ruiz')).getByRole('button', { name: 'Editar Marta Ruiz' }));
    let dialog = await screen.findByRole('dialog', { name: 'Editar cliente' });
    await user.type(within(dialog).getByLabelText('Notas'), ', con canela');
    expect(within(dialog).getByRole('button', { name: 'Cambiar datos fiscales' })).toBeDisabled();
    expect(dialog).toHaveTextContent('Guarda primero los cambios del cliente.');
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Editar cliente' })).not.toBeInTheDocument());

    await user.click(within(await row('Luis Peña')).getByRole('button', { name: 'Editar Luis Peña' }));
    dialog = await screen.findByRole('dialog', { name: 'Editar cliente' });
    expect(dialog).toHaveTextContent('Sin datos fiscales: la factura de sus pedidos los pide.');
    await user.click(within(dialog).getByRole('button', { name: 'Agregar datos fiscales' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Editar cliente' })).not.toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: 'Cerrar datos fiscales' }));
    expect(screen.queryByRole('dialog', { name: 'Datos fiscales de Luis Peña' })).not.toBeInTheDocument();
    await waitFor(() => expect(customersApi.getAll).toHaveBeenCalledTimes(2));

    // Closed without changes: nothing to read again.
    fiscal.changed = false;
    await user.click(within(await row('Luis Peña')).getByRole('button', { name: 'Editar Luis Peña' }));
    await user.click(within(await screen.findByRole('dialog', { name: 'Editar cliente' })).getByRole('button', { name: 'Agregar datos fiscales' }));
    await user.click(await screen.findByRole('button', { name: 'Cerrar datos fiscales' }));
    expect(customersApi.getAll).toHaveBeenCalledTimes(2);
  });

  it('lets only the owner give credit, to a customer with a phone, and reads the list again after', async () => {
    const user = userEvent.setup();
    const { unmount } = renderList({ role: 'Employee' });
    expect(within(await row('Marta Ruiz')).queryByRole('button', { name: 'Fiado de Marta Ruiz' })).not.toBeInTheDocument();
    unmount();

    renderList();
    expect(within(await row('Luis Peña')).getByRole('button', { name: 'Fiado de Luis Peña' })).toBeDisabled();
    const calls = vi.mocked(customersApi.getAll).mock.calls.length;
    await user.click(within(await row('Marta Ruiz')).getByRole('button', { name: 'Fiado de Marta Ruiz' }));
    expect(screen.getByRole('dialog', { name: 'Fiado de Marta Ruiz' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Cerrar fiado' }));
    await waitFor(() => expect(customersApi.getAll).toHaveBeenCalledTimes(calls + 1));
  });

  it('opens the card of a customer', async () => {
    const user = userEvent.setup();
    renderList();

    await user.click(within(await row('Marta Ruiz')).getByRole('button', { name: 'Ficha de Marta Ruiz' }));
    expect(screen.getByRole('dialog', { name: 'Ficha de Marta Ruiz' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Cerrar ficha' }));
    expect(screen.queryByRole('dialog', { name: 'Ficha de Marta Ruiz' })).not.toBeInTheDocument();
  });

  it('turns a customer on or off, and says why when it cannot', async () => {
    const user = userEvent.setup();
    vi.mocked(customersApi.setActive)
      .mockResolvedValueOnce({ data: {} } as never)
      .mockRejectedValueOnce({ isAxiosError: true, response: { status: 409, data: { error: 'Marta Ruiz tiene pedidos fiados sin pagar.' } } });
    renderList();

    await user.click(within(await row('Ana Gil')).getByRole('button', { name: 'Activar Ana Gil' }));
    expect(customersApi.setActive).toHaveBeenCalledWith('ana', true);
    await waitFor(() => expect(customersApi.getAll).toHaveBeenCalledTimes(2));

    await user.click(within(await row('Marta Ruiz')).getByRole('button', { name: 'Desactivar Marta Ruiz' }));
    expect(customersApi.setActive).toHaveBeenLastCalledWith('marta', false);
    expect(await screen.findByText('Marta Ruiz tiene pedidos fiados sin pagar.')).toBeInTheDocument();

    // Editing someone does not make it go away: it is not an error of the dialog.
    await user.click(within(await row('Marta Ruiz')).getByRole('button', { name: 'Editar Marta Ruiz' }));
    const dialog = await screen.findByRole('dialog', { name: 'Editar cliente' });
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Editar cliente' })).not.toBeInTheDocument());
    expect(screen.getByText('Marta Ruiz tiene pedidos fiados sin pagar.')).toBeInTheDocument();
  });

  it('does not show in the next dialog what the server answered after the previous one was closed', async () => {
    const user = userEvent.setup();
    let refuse: (reason: unknown) => void = () => undefined;
    vi.mocked(customersApi.update).mockReturnValue(new Promise((_, reject) => { refuse = reject; }) as never);
    renderList();

    await user.click(within(await row('Ana Gil')).getByRole('button', { name: 'Editar Ana Gil' }));
    let dialog = await screen.findByRole('dialog', { name: 'Editar cliente' });
    await user.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Editar cliente' })).not.toBeInTheDocument());
    refuse({ isAxiosError: true, response: { status: 409, data: { error: 'Ya existe un cliente con ese teléfono.' } } });

    await user.click(screen.getByRole('button', { name: 'Nuevo cliente' }));
    dialog = await screen.findByRole('dialog', { name: 'Nuevo cliente' });
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('deletes a customer only once confirmed, and reads the list again', async () => {
    const user = userEvent.setup();
    vi.mocked(customersApi.delete).mockResolvedValue({ data: {} } as never);
    dialogs.confirm.mockResolvedValueOnce(false).mockResolvedValueOnce(true);
    renderList();

    await user.click(within(await row('Ana Gil')).getByRole('button', { name: 'Eliminar Ana Gil' }));
    expect(dialogs.confirm).toHaveBeenCalledWith(expect.objectContaining({ message: 'Se eliminará a Ana Gil. Sus pedidos se conservan.', danger: true }));
    expect(customersApi.delete).not.toHaveBeenCalled();

    await user.click(within(await row('Ana Gil')).getByRole('button', { name: 'Eliminar Ana Gil' }));
    expect(customersApi.delete).toHaveBeenCalledWith('ana');
    await waitFor(() => expect(customersApi.getAll).toHaveBeenCalledTimes(2));
  });

  it('shows inside the dialog why the server did not save, and forgets it once the dialog is closed', async () => {
    const user = userEvent.setup();
    vi.mocked(customersApi.update).mockRejectedValue({
      isAxiosError: true, response: { status: 409, data: { error: 'Ya existe un cliente con el teléfono +525512345678.' } },
    });
    renderList();

    await user.click(within(await row('Ana Gil')).getByRole('button', { name: 'Editar Ana Gil' }));
    const dialog = await screen.findByRole('dialog', { name: 'Editar cliente' });
    await user.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Ya existe un cliente con el teléfono +525512345678.');
    expect(screen.getAllByText('Ya existe un cliente con el teléfono +525512345678.')).toHaveLength(1);
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Editar cliente' })).not.toBeInTheDocument());
    expect(screen.queryByText('Ya existe un cliente con el teléfono +525512345678.')).not.toBeInTheDocument();

    // Opened again, it starts without it.
    await user.click(within(await row('Ana Gil')).getByRole('button', { name: 'Editar Ana Gil' }));
    expect(within(await screen.findByRole('dialog', { name: 'Editar cliente' })).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('says when the customers could not be read, and keeps saying it while a dialog opens and closes', async () => {
    const user = userEvent.setup();
    vi.mocked(customersApi.getAll).mockRejectedValue(new Error('Network Error'));
    renderList();

    expect(await screen.findByText('Error al cargar clientes.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Nuevo cliente' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo cliente' });
    // The error of the list is not one of the dialog.
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nuevo cliente' })).not.toBeInTheDocument());
    expect(screen.getByText('Error al cargar clientes.')).toBeInTheDocument();
  });
});
