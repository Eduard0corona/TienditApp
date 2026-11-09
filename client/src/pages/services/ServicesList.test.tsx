import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { categoriesApi } from '../../api/categoriesApi';
import { productsApi } from '../../api/productsApi';
import { useTenant } from '../../hooks/useTenant';
import { CfdiTaxObject, UnitType, type CategoryDto, type ProductDto } from '../../types';
import ServicesList from './ServicesList';

const dialogs = vi.hoisted(() => ({ confirm: vi.fn() }));

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => dialogs }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../api/categoriesApi', () => ({ categoriesApi: { getAll: vi.fn() } }));
vi.mock('../../api/productsApi', () => ({
  productsApi: { getAll: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn(), toggleAvailability: vi.fn() },
}));

const categories = [
  { id: 'repairs', name: 'Reparaciones' },
  { id: 'courses', name: 'Cursos' },
] as unknown as CategoryDto[];

function service(patch: Partial<ProductDto>): ProductDto {
  return {
    id: 's', name: 'Servicio', categoryId: 'repairs', priceAmount: 100, priceCurrency: 'MXN', sortOrder: 0, unit: UnitType.Service,
    isTaxExempt: false, taxObject: CfdiTaxObject.Subject, isAvailable: true, isTrackable: false, notifyCustomer: false,
    ...patch,
  } as ProductDto;
}

/** A maintenance with a schedule at the border rate and its CFDI codes, and a workshop of a category no longer listed. */
const maintenance = service({
  id: 'maint', name: 'Mantenimiento de cafetera', description: 'Limpieza profunda', priceAmount: 450, unit: UnitType.Service,
  preparationTimeMinutes: 90, availableFromTime: '09:00:00', availableToTime: '18:00:00', isTrackable: true, notifyCustomer: true,
  taxRate: 8, satProductCode: '81111500', satUnitCode: 'E48', sortOrder: 2, taxObject: CfdiTaxObject.NotSubject,
});
const workshop = service({ id: 'work', name: 'Taller de barismo', categoryId: 'gone', priceAmount: 650.5, unit: UnitType.Session, isAvailable: false });

const listPage = (items: ProductDto[], totalCount = items.length) =>
  ({ data: { items, pageNumber: 1, totalPages: Math.ceil(totalCount / 10), totalCount, hasPreviousPage: false, hasNextPage: totalCount > 10 } }) as never;

const row = async (name: string) => (await screen.findByText(name, { selector: 'td' })).closest('tr') as HTMLElement;

describe('ServicesList', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useTenant).mockReturnValue({ tenant: { defaultTaxRate: 16 } } as unknown as ReturnType<typeof useTenant>);
    vi.mocked(categoriesApi.getAll).mockResolvedValue({ data: categories } as never);
    vi.mocked(productsApi.getAll).mockResolvedValue(listPage([maintenance, workshop]));
  });

  it('lists the services with their category, price, unit, schedule, duration and whether they are tracked', async () => {
    render(<ServicesList />);

    const cells = (tr: HTMLElement) => within(tr).getAllByRole('cell').slice(0, 8).map((cell) => cell.textContent);
    // The category name arrives after the services: the row waits for it.
    await waitFor(async () => expect(cells(await row('Mantenimiento de cafetera')))
      .toEqual(['Mantenimiento de cafetera', 'Reparaciones', '$450.00', 'Servicio', '09:00 - 18:00', '90', 'Si', 'Si']));
    expect(cells(await row('Taller de barismo'))).toEqual(['Taller de barismo', '-', '$650.50', 'Sesión', 'Todo el día', '-', 'No', 'No']);
    expect(screen.getByLabelText('Disponible: Mantenimiento de cafetera')).toBeChecked();
    expect(screen.getByLabelText('Disponible: Taller de barismo')).not.toBeChecked();
    expect(productsApi.getAll).toHaveBeenCalledWith({ itemType: 'Service', search: undefined, pageNumber: 1, pageSize: 10 });
  });

  it('asks the server for the category, the availability and the name searched, from the first page', async () => {
    const user = userEvent.setup();
    vi.mocked(productsApi.getAll).mockResolvedValue(listPage([maintenance, workshop], 25));
    render(<ServicesList />);
    await row('Mantenimiento de cafetera');
    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
    await waitFor(() => expect(productsApi.getAll).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 2 })));

    // Each filter takes the list back to its first page.
    await user.selectOptions(screen.getByLabelText('Categoría'), 'courses');
    await waitFor(() => expect(productsApi.getAll).toHaveBeenLastCalledWith({
      itemType: 'Service', search: undefined, pageNumber: 1, pageSize: 10, categoryId: 'courses',
    }));
    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
    await user.selectOptions(screen.getByLabelText('Disponibilidad'), 'false');
    await waitFor(() => expect(productsApi.getAll).toHaveBeenLastCalledWith({
      itemType: 'Service', search: undefined, pageNumber: 1, pageSize: 10, categoryId: 'courses', isAvailable: false,
    }));
    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
    await user.type(screen.getByLabelText('Nombre del servicio'), 'barismo');
    await waitFor(() => expect(productsApi.getAll).toHaveBeenLastCalledWith({
      itemType: 'Service', search: 'barismo', pageNumber: 1, pageSize: 10, categoryId: 'courses', isAvailable: false,
    }));
  });

  it('creates a service with the numbers, times and codes the API takes, and reads the list again', async () => {
    const user = userEvent.setup();
    vi.mocked(productsApi.create).mockResolvedValue({ data: {} } as never);
    render(<ServicesList />);
    await row('Mantenimiento de cafetera');

    await user.click(screen.getByRole('button', { name: 'Nuevo servicio' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo servicio' });
    await user.type(within(dialog).getByLabelText(/^Nombre/), '  Reparación de molino  ');
    await user.selectOptions(within(dialog).getByLabelText(/^Categoría/), 'repairs');
    await user.type(within(dialog).getByLabelText('Descripción'), ' Cambio de fresas ');
    await user.clear(within(dialog).getByLabelText(/^Precio/));
    await user.type(within(dialog).getByLabelText(/^Precio/), '380.5');
    await user.selectOptions(within(dialog).getByLabelText('IVA'), 'exempt');
    await user.selectOptions(within(dialog).getByLabelText('Unidad de servicio'), 'Hora');
    await user.type(within(dialog).getByLabelText('Duración (min)'), '120');
    await user.type(within(dialog).getByLabelText('Disponible desde'), '10:00');
    await user.type(within(dialog).getByLabelText('Disponible hasta'), '19:30');
    await user.type(within(dialog).getByLabelText('Clave de producto o servicio'), ' 81111500 ');
    await user.type(within(dialog).getByLabelText('Clave de unidad'), 'e48');
    await user.selectOptions(within(dialog).getByLabelText('Objeto de impuesto'), String(CfdiTaxObject.NotSubject));
    // Notifying needs a tracked piece.
    expect(within(dialog).getByLabelText('Notificar al cliente')).toBeDisabled();
    await user.click(within(dialog).getByLabelText('Rastreable'));
    await user.click(within(dialog).getByLabelText('Notificar al cliente'));
    await user.click(within(dialog).getByRole('button', { name: 'Crear servicio' }));

    await waitFor(() => expect(productsApi.create).toHaveBeenCalledWith({
      categoryId: 'repairs', name: 'Reparación de molino', description: 'Cambio de fresas', priceAmount: 380.5, priceCurrency: 'MXN',
      sortOrder: 0, isFeatured: false, unit: UnitType.Hour, preparationTimeMinutes: 120, availableFromTime: '10:00:00', availableToTime: '19:30:00',
      isTrackable: true, notifyCustomer: true, taxRate: undefined, isTaxExempt: true, satProductCode: '81111500', satUnitCode: 'E48',
      taxObject: CfdiTaxObject.NotSubject,
    }, 'Service'));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nuevo servicio' })).not.toBeInTheDocument());
    expect(productsApi.getAll).toHaveBeenCalledTimes(2);
  });

  it('stops notifying the customer when the service stops being tracked', async () => {
    const user = userEvent.setup();
    vi.mocked(productsApi.update).mockResolvedValue({ data: {} } as never);
    render(<ServicesList />);

    await user.click(within(await row('Mantenimiento de cafetera')).getByRole('button', { name: 'Editar Mantenimiento de cafetera' }));
    const dialog = await screen.findByRole('dialog', { name: 'Editar servicio' });
    expect(within(dialog).getByLabelText('Notificar al cliente')).toBeChecked();
    await user.click(within(dialog).getByLabelText('Rastreable'));
    expect(within(dialog).getByLabelText('Notificar al cliente')).not.toBeChecked();
    await user.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => expect(productsApi.update).toHaveBeenCalledWith(
      'maint', expect.objectContaining({ isTrackable: false, notifyCustomer: false }), 'Service',
    ));
  });

  it('edits a service from what it has, and sends again its rate and its codes', async () => {
    const user = userEvent.setup();
    vi.mocked(productsApi.update).mockResolvedValue({ data: {} } as never);
    render(<ServicesList />);

    await user.click(within(await row('Mantenimiento de cafetera')).getByRole('button', { name: 'Editar Mantenimiento de cafetera' }));
    const dialog = await screen.findByRole('dialog', { name: 'Editar servicio' });
    expect(within(dialog).getByLabelText(/^Nombre/)).toHaveValue('Mantenimiento de cafetera');
    expect(within(dialog).getByLabelText(/^Categoría/)).toHaveValue('repairs');
    expect(within(dialog).getByLabelText('IVA')).toHaveValue('8');
    expect(within(dialog).getByLabelText('Disponible desde')).toHaveValue('09:00');
    expect(within(dialog).getByLabelText('Disponible hasta')).toHaveValue('18:00');
    await user.clear(within(dialog).getByLabelText(/^Precio/));
    await user.type(within(dialog).getByLabelText(/^Precio/), '480');
    await user.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => expect(productsApi.update).toHaveBeenCalledWith('maint', {
      id: 'maint', categoryId: 'repairs', name: 'Mantenimiento de cafetera', description: 'Limpieza profunda', priceAmount: 480,
      priceCurrency: 'MXN', sortOrder: 2, isFeatured: false, unit: UnitType.Service, preparationTimeMinutes: 90,
      availableFromTime: '09:00:00', availableToTime: '18:00:00', isTrackable: true, notifyCustomer: true, taxRate: 8, isTaxExempt: false,
      satProductCode: '81111500', satUnitCode: 'E48', taxObject: CfdiTaxObject.NotSubject,
    }, 'Service'));
    expect(productsApi.create).not.toHaveBeenCalled();
  });

  it('asks for a category again when the one of the service is no longer listed', async () => {
    const user = userEvent.setup();
    render(<ServicesList />);

    await user.click(within(await row('Taller de barismo')).getByRole('button', { name: 'Editar Taller de barismo' }));
    const dialog = await screen.findByRole('dialog', { name: 'Editar servicio' });
    await user.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));

    expect(await within(dialog).findByText('Selecciona una categoría')).toBeInTheDocument();
    expect(productsApi.update).not.toHaveBeenCalled();
  });

  it('asks for what a service needs before sending it', async () => {
    const user = userEvent.setup();
    render(<ServicesList />);
    await row('Mantenimiento de cafetera');

    await user.click(screen.getByRole('button', { name: 'Nuevo servicio' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo servicio' });
    await user.click(within(dialog).getByRole('button', { name: 'Crear servicio' }));

    expect(await within(dialog).findByText('El nombre es requerido')).toBeInTheDocument();
    expect(within(dialog).getByText('Selecciona una categoría')).toBeInTheDocument();
    expect(within(dialog).getByText('Debe ser mayor a 0')).toBeInTheDocument();
    expect(productsApi.create).not.toHaveBeenCalled();
  });

  it('shows inside the dialog why the server did not save, and forgets it once the dialog is closed', async () => {
    const user = userEvent.setup();
    vi.mocked(productsApi.update).mockRejectedValue({
      isAxiosError: true, response: { status: 409, data: { error: 'Ya existe un servicio con ese nombre.' } },
    });
    render(<ServicesList />);

    await user.click(within(await row('Mantenimiento de cafetera')).getByRole('button', { name: 'Editar Mantenimiento de cafetera' }));
    const dialog = await screen.findByRole('dialog', { name: 'Editar servicio' });
    await user.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Ya existe un servicio con ese nombre.');
    expect(screen.getAllByText('Ya existe un servicio con ese nombre.')).toHaveLength(1);
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Editar servicio' })).not.toBeInTheDocument());
    expect(screen.queryByText('Ya existe un servicio con ese nombre.')).not.toBeInTheDocument();

    // Opened again, it starts without it.
    await user.click(within(await row('Mantenimiento de cafetera')).getByRole('button', { name: 'Editar Mantenimiento de cafetera' }));
    expect(within(await screen.findByRole('dialog', { name: 'Editar servicio' })).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('deletes a service only once confirmed, and reads the list again', async () => {
    const user = userEvent.setup();
    vi.mocked(productsApi.delete).mockResolvedValue({ data: {} } as never);
    dialogs.confirm.mockResolvedValueOnce(false).mockResolvedValueOnce(true);
    render(<ServicesList />);
    const workshopRow = await row('Taller de barismo');

    await user.click(within(workshopRow).getByRole('button', { name: 'Eliminar Taller de barismo' }));
    expect(dialogs.confirm).toHaveBeenCalledWith(expect.objectContaining({ message: 'Se eliminará “Taller de barismo” del catálogo.', danger: true }));
    expect(productsApi.delete).not.toHaveBeenCalled();

    await user.click(within(workshopRow).getByRole('button', { name: 'Eliminar Taller de barismo' }));
    expect(productsApi.delete).toHaveBeenCalledWith('work');
    await waitFor(() => expect(productsApi.getAll).toHaveBeenCalledTimes(2));
  });

  it('turns a service on or off, and says so when it cannot', async () => {
    const user = userEvent.setup();
    vi.mocked(productsApi.toggleAvailability).mockResolvedValueOnce({ data: {} } as never).mockRejectedValueOnce(new Error('Network Error'));
    render(<ServicesList />);
    await row('Taller de barismo');

    await user.click(screen.getByLabelText('Disponible: Taller de barismo'));
    expect(productsApi.toggleAvailability).toHaveBeenCalledWith('work', true);
    await waitFor(() => expect(productsApi.getAll).toHaveBeenCalledTimes(2));

    await user.click(screen.getByLabelText('Disponible: Mantenimiento de cafetera'));
    expect(productsApi.toggleAvailability).toHaveBeenLastCalledWith('maint', false);
    expect(await screen.findByText('Error al actualizar la disponibilidad.')).toBeInTheDocument();
  });

  it('says when the services could not be read, and keeps saying it while a dialog opens and closes', async () => {
    const user = userEvent.setup();
    vi.mocked(productsApi.getAll).mockRejectedValue(new Error('Network Error'));
    render(<ServicesList />);

    expect(await screen.findByText('Error al cargar los servicios.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Nuevo servicio' }));
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo servicio' });
    // The error of the list is not one of the dialog.
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nuevo servicio' })).not.toBeInTheDocument());
    expect(screen.getByText('Error al cargar los servicios.')).toBeInTheDocument();
  });
});
