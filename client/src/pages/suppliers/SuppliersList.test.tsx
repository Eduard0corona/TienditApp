import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { suppliersApi } from '../../api/suppliersApi';
import type { SupplierDto } from '../../types';
import SuppliersList from './SuppliersList';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/suppliersApi', () => ({
  suppliersApi: { getAll: vi.fn(), create: vi.fn(), update: vi.fn(), setActive: vi.fn() },
}));

/** A dairy with all its data, and a roaster turned off with only its name. */
const dairy = {
  id: 'dairy', name: 'Lácteos del Valle', contactName: 'Jorge Mena', phone: '55 4444 1212', email: 'pedidos@lacteosdelvalle.mx',
  taxId: 'LVA990101AB2', address: 'Av. Central 12, Toluca', notes: 'Entrega martes y viernes', isActive: true,
} as SupplierDto;
const roaster = { id: 'roaster', name: 'Tostadores de Altura', isActive: false } as SupplierDto;

const listPage = (items: SupplierDto[], totalCount = items.length) =>
  ({ data: { items, pageNumber: 1, totalPages: Math.ceil(totalCount / 10), totalCount, hasPreviousPage: false, hasNextPage: totalCount > 10 } }) as never;

const row = async (name: string) => (await screen.findByText(name, { selector: '.fw-semibold' })).closest('tr') as HTMLElement;

describe('SuppliersList', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(suppliersApi.getAll).mockResolvedValue(listPage([dairy, roaster]));
  });

  it('lists each supplier with its RFC, contact, address, email, phone and whether it is active', async () => {
    render(<SuppliersList />);

    const cells = (tr: HTMLElement) => within(tr).getAllByRole('cell').slice(0, 4).map((cell) => cell.textContent);
    expect(cells(await row('Lácteos del Valle'))).toEqual([
      'Lácteos del ValleLVA990101AB2', 'Jorge MenaAv. Central 12, Toluca', 'pedidos@lacteosdelvalle.mx55 4444 1212', 'Activo',
    ]);
    expect(cells(await row('Tostadores de Altura'))).toEqual(['Tostadores de Altura-', '--', '--', 'Inactivo']);
    expect(suppliersApi.getAll).toHaveBeenCalledWith({ pageNumber: 1, pageSize: 10 });
  });

  it('asks the server for the status and what is searched, from the first page', async () => {
    const user = userEvent.setup();
    vi.mocked(suppliersApi.getAll).mockResolvedValue(listPage([dairy, roaster], 25));
    render(<SuppliersList />);
    await row('Lácteos del Valle');

    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
    await waitFor(() => expect(suppliersApi.getAll).toHaveBeenLastCalledWith({ pageNumber: 2, pageSize: 10 }));
    await user.selectOptions(screen.getByLabelText('Estado'), 'true');
    await waitFor(() => expect(suppliersApi.getAll).toHaveBeenLastCalledWith({ pageNumber: 1, pageSize: 10, isActive: true }));
    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
    await user.type(screen.getByLabelText('Nombre, contacto, teléfono o correo'), 'lácteos');
    await waitFor(() => expect(suppliersApi.getAll).toHaveBeenLastCalledWith({ pageNumber: 1, pageSize: 10, isActive: true, search: 'lácteos' }));
  });

  it('creates a supplier with what is written trimmed and nothing for what is left empty, says so and reads the list again', async () => {
    const user = userEvent.setup();
    vi.mocked(suppliersApi.create).mockResolvedValue({ data: {} } as never);
    render(<SuppliersList />);
    await row('Lácteos del Valle');

    await user.click(screen.getByRole('button', { name: 'Nuevo proveedor' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo proveedor' });
    await user.type(within(dialog).getByLabelText('Nombre *'), '  Panadería La Espiga ');
    await user.type(within(dialog).getByLabelText('Contacto'), ' Rosa ');
    await user.type(within(dialog).getByLabelText('Teléfono'), '   ');
    await user.type(within(dialog).getByLabelText('Email'), ' ventas@laespiga.mx ');
    await user.type(within(dialog).getByLabelText('Dirección'), 'Calle 5 #20');
    await user.type(within(dialog).getByLabelText('Notas'), 'Pan antes de las 7');
    await user.click(within(dialog).getByRole('button', { name: 'Crear proveedor' }));

    expect(suppliersApi.create).toHaveBeenCalledWith({
      name: 'Panadería La Espiga', contactName: 'Rosa', phone: undefined, email: 'ventas@laespiga.mx', taxId: undefined,
      address: 'Calle 5 #20', notes: 'Pan antes de las 7',
    });
    expect(await screen.findByText('Proveedor creado.')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nuevo proveedor' })).not.toBeInTheDocument());
    expect(suppliersApi.getAll).toHaveBeenCalledTimes(2);
  });

  it('edits a supplier from what it has', async () => {
    const user = userEvent.setup();
    vi.mocked(suppliersApi.update).mockResolvedValue({ data: {} } as never);
    render(<SuppliersList />);

    await user.click(within(await row('Lácteos del Valle')).getByRole('button', { name: 'Editar Lácteos del Valle' }));
    const dialog = await screen.findByRole('dialog', { name: 'Editar proveedor' });
    expect(within(dialog).getByLabelText('Nombre *')).toHaveValue('Lácteos del Valle');
    expect(within(dialog).getByLabelText('Notas')).toHaveValue('Entrega martes y viernes');
    await user.clear(within(dialog).getByLabelText('Teléfono'));
    await user.type(within(dialog).getByLabelText('Teléfono'), '55 4444 9999');
    await user.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));

    expect(suppliersApi.update).toHaveBeenCalledWith('dairy', {
      id: 'dairy', name: 'Lácteos del Valle', contactName: 'Jorge Mena', phone: '55 4444 9999', email: 'pedidos@lacteosdelvalle.mx',
      taxId: 'LVA990101AB2', address: 'Av. Central 12, Toluca', notes: 'Entrega martes y viernes',
    });
    expect(await screen.findByText('Proveedor actualizado.')).toBeInTheDocument();
    expect(suppliersApi.create).not.toHaveBeenCalled();
  });

  it('asks for the name inside the dialog, and forgets it once the dialog is closed', async () => {
    const user = userEvent.setup();
    render(<SuppliersList />);
    await row('Lácteos del Valle');

    await user.click(screen.getByRole('button', { name: 'Nuevo proveedor' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo proveedor' });
    await user.type(within(dialog).getByLabelText('Nombre *'), '   ');
    await user.click(within(dialog).getByRole('button', { name: 'Crear proveedor' }));

    expect(within(dialog).getByRole('alert')).toHaveTextContent('El nombre del proveedor es obligatorio.');
    expect(screen.getAllByText('El nombre del proveedor es obligatorio.')).toHaveLength(1);
    expect(suppliersApi.create).not.toHaveBeenCalled();
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nuevo proveedor' })).not.toBeInTheDocument());
    expect(screen.queryByText('El nombre del proveedor es obligatorio.')).not.toBeInTheDocument();

    // Opened again, it starts without it.
    await user.click(screen.getByRole('button', { name: 'Nuevo proveedor' }));
    expect(within(await screen.findByRole('dialog', { name: 'Nuevo proveedor' })).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows inside the dialog why the server did not save, with the button ready to try again', async () => {
    const user = userEvent.setup();
    vi.mocked(suppliersApi.update).mockRejectedValue({
      isAxiosError: true, response: { status: 409, data: { error: 'Ya existe un proveedor con el RFC LVA990101AB2.' } },
    });
    render(<SuppliersList />);

    await user.click(within(await row('Lácteos del Valle')).getByRole('button', { name: 'Editar Lácteos del Valle' }));
    const dialog = await screen.findByRole('dialog', { name: 'Editar proveedor' });
    await user.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Ya existe un proveedor con el RFC LVA990101AB2.');
    expect(screen.getAllByText('Ya existe un proveedor con el RFC LVA990101AB2.')).toHaveLength(1);
    expect(within(dialog).getByRole('button', { name: 'Guardar cambios' })).toBeEnabled();
    expect(screen.queryByText('Proveedor actualizado.')).not.toBeInTheDocument();
  });

  it('turns a supplier on or off, says so, and says why when it cannot', async () => {
    const user = userEvent.setup();
    vi.mocked(suppliersApi.setActive)
      .mockResolvedValueOnce({ data: {} } as never)
      .mockRejectedValueOnce({ isAxiosError: true, response: { status: 409, data: { error: 'Tiene una compra sin recibir.' } } });
    render(<SuppliersList />);

    await user.click(within(await row('Tostadores de Altura')).getByRole('button', { name: 'Activar Tostadores de Altura' }));
    expect(suppliersApi.setActive).toHaveBeenCalledWith('roaster', true);
    expect(await screen.findByText('Estado actualizado.')).toBeInTheDocument();
    await waitFor(() => expect(suppliersApi.getAll).toHaveBeenCalledTimes(2));

    await user.click(within(await row('Lácteos del Valle')).getByRole('button', { name: 'Desactivar Lácteos del Valle' }));
    expect(suppliersApi.setActive).toHaveBeenLastCalledWith('dairy', false);
    expect(await screen.findByText('Tiene una compra sin recibir.')).toBeInTheDocument();
    expect(screen.queryByText('Estado actualizado.')).not.toBeInTheDocument();
  });

  it('says why the suppliers could not be read, keeps saying it while a dialog opens and closes, and stops once a new read works', async () => {
    const user = userEvent.setup();
    vi.mocked(suppliersApi.getAll).mockRejectedValueOnce(new Error('Network Error')).mockResolvedValue(listPage([dairy]));
    render(<SuppliersList />);

    expect(await screen.findByText('No se pudieron cargar los proveedores.')).toBeInTheDocument();
    // Opening and closing a dialog does not make it go away: the error of the list is not one of the dialog.
    await user.click(screen.getByRole('button', { name: 'Nuevo proveedor' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo proveedor' });
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument();
    // Neither does sending the dialog without its name.
    await user.click(within(dialog).getByRole('button', { name: 'Crear proveedor' }));
    expect(within(dialog).getByRole('alert')).toHaveTextContent('El nombre del proveedor es obligatorio.');
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nuevo proveedor' })).not.toBeInTheDocument());
    expect(screen.getByText('No se pudieron cargar los proveedores.')).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Estado'), 'true');
    await row('Lácteos del Valle');
    expect(screen.queryByText('No se pudieron cargar los proveedores.')).not.toBeInTheDocument();
  });
});
