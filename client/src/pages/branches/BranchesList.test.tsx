import { fireEvent, render, screen, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { branchesApi, type Branch } from '../../api/branchesApi';
import { BranchServiceMode } from '../../types';
import BranchesList from './BranchesList';

vi.mock('../../api/branchesApi', () => ({
  branchesApi: { getAll: vi.fn(), create: vi.fn(), update: vi.fn(), setActive: vi.fn(), setDefault: vi.fn() },
}));
vi.mock('../../hooks/useToast', () => ({ useToast: () => ({ success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() }) }));
// The section tabs read the signed-in user; they are not what this test is about.
vi.mock('../../components/layout/SectionTabs', () => ({ default: () => null }));
const confirm = vi.fn();
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => ({ confirm }) }));

const MENU_HINT = /dirección de la sucursal principal se muestra en tu menú en línea/i;
const PRIVATE_HINT = 'Tu menú en línea dice «Solo a domicilio» y no muestra esta dirección ni su ubicación. Solo las ve tu equipo.';

function branch(overrides: Partial<Branch>): Branch {
  return {
    id: 'b1', name: 'Sucursal principal', code: 'PRINCIPAL', isDefault: true, isActive: true,
    addressLine1: 'Av. Juárez 123', city: 'Puebla', state: 'Puebla', serviceMode: BranchServiceMode.Premises,
    ...overrides,
  } as Branch;
}

function renderBranches() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/settings/branches']}>
        <BranchesList />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('BranchesList', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(branchesApi.getAll).mockResolvedValue([
      branch({}),
      branch({ id: 'b2', name: 'Centro', code: 'CENTRO', isDefault: false, addressLine1: '5 de Mayo 10' }),
    ] as Awaited<ReturnType<typeof branchesApi.getAll>>);
  });

  it('says that the address of the main branch is the one customers see in the online menu', async () => {
    renderBranches();

    fireEvent.click(await screen.findByRole('button', { name: 'Editar Sucursal principal' }));
    expect(within(screen.getByRole('dialog', { name: 'Editar Sucursal principal' })).getByText(MENU_HINT)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }));
    fireEvent.click(screen.getByRole('button', { name: 'Editar Centro' }));
    expect(within(screen.getByRole('dialog', { name: 'Editar Centro' })).queryByText(MENU_HINT)).not.toBeInTheDocument();
  });

  it('says it for a new branch once it is marked as the main one', async () => {
    renderBranches();

    fireEvent.click(await screen.findByRole('button', { name: /Nueva sucursal/ }));
    const dialog = screen.getByRole('dialog', { name: 'Nueva sucursal' });
    expect(within(dialog).queryByText(MENU_HINT)).not.toBeInTheDocument();

    fireEvent.click(within(dialog).getByLabelText('Sucursal principal'));
    expect(within(dialog).getByText(MENU_HINT)).toBeInTheDocument();
  });

  it('keeps the address of a main branch that only delivers off the menu, and saves how it serves', async () => {
    vi.mocked(branchesApi.update).mockResolvedValue(branch({ serviceMode: BranchServiceMode.DeliveryOnly }) as never);
    renderBranches();

    fireEvent.click(await screen.findByRole('button', { name: 'Editar Sucursal principal' }));
    const dialog = screen.getByRole('dialog', { name: 'Editar Sucursal principal' });
    expect(within(dialog).getByRole('radiogroup', { name: '¿Cómo atiende?' })).toBeInTheDocument();
    expect(within(dialog).getByRole('radio', { name: 'Local abierto al público' })).toBeChecked();

    fireEvent.click(within(dialog).getByRole('radio', { name: 'Solo a domicilio, sin local' }));
    expect(within(dialog).getByText(PRIVATE_HINT)).toBeInTheDocument();
    expect(within(dialog).queryByText(MENU_HINT)).not.toBeInTheDocument();
    expect(within(dialog).getByRole('radio', { name: 'Solo a domicilio, sin local' })).toHaveAccessibleDescription(
      'Solo entregas. Su dirección es para tu equipo y nunca se muestra a tus clientes.',
    );

    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));
    await vi.waitFor(() => expect(branchesApi.update).toHaveBeenCalledWith('b1', expect.objectContaining({
      id: 'b1', addressLine1: 'Av. Juárez 123', serviceMode: BranchServiceMode.DeliveryOnly,
    })));
  });

  it('marks in the list the branches that only deliver', async () => {
    vi.mocked(branchesApi.getAll).mockResolvedValue([
      branch({ serviceMode: BranchServiceMode.DeliveryOnly }),
      branch({ id: 'b2', name: 'Centro', code: 'CENTRO', isDefault: false }),
    ] as Awaited<ReturnType<typeof branchesApi.getAll>>);
    renderBranches();

    const main = (await screen.findByText('Sucursal principal')).closest('tr') as HTMLElement;
    const centro = screen.getByText('Centro').closest('tr') as HTMLElement;
    expect(within(main).getByText('Solo a domicilio')).toBeInTheDocument();
    expect(within(centro).queryByText('Solo a domicilio')).not.toBeInTheDocument();
  });

  it('does not send how a branch serves when the person did not change it, so a list read before stays harmless', async () => {
    // The list says premises; someone else may have marked it «Solo a domicilio» since: the server keeps what it has.
    vi.mocked(branchesApi.update).mockResolvedValue(branch({}) as never);
    renderBranches();

    fireEvent.click(await screen.findByRole('button', { name: 'Editar Sucursal principal' }));
    const dialog = screen.getByRole('dialog', { name: 'Editar Sucursal principal' });
    fireEvent.change(within(dialog).getByLabelText('Teléfono'), { target: { value: '222 123 4567' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));

    await vi.waitFor(() => expect(branchesApi.update).toHaveBeenCalled());
    const [, body] = vi.mocked(branchesApi.update).mock.calls[0];
    expect(body).toMatchObject({ id: 'b1', contactPhone: '222 123 4567' });
    expect(body.serviceMode).toBeUndefined();
    expect(body.location).toBeUndefined();
  });

  it('puts a branch on the map with what Google Maps copies, opens it to check, and takes it off (Fase 24G)', async () => {
    vi.mocked(branchesApi.getAll).mockResolvedValue([
      branch({ latitude: 19.043577, longitude: -98.198626 }),
      branch({ id: 'b2', name: 'Centro', code: 'CENTRO', isDefault: false }),
    ] as Awaited<ReturnType<typeof branchesApi.getAll>>);
    vi.mocked(branchesApi.update).mockResolvedValue(branch({}) as never);
    renderBranches();

    // In the list, the branch on the map says so.
    const main = (await screen.findByText('Sucursal principal')).closest('tr') as HTMLElement;
    expect(within(main).getByText('En el mapa')).toBeInTheDocument();
    expect(within(screen.getByText('Centro').closest('tr') as HTMLElement).queryByText('En el mapa')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Editar Centro' }));
    let dialog = screen.getByRole('dialog', { name: 'Editar Centro' });
    const field = within(dialog).getByLabelText('Ubicación en el mapa (opcional)');
    fireEvent.change(field, { target: { value: 'por el parque' } });
    expect(field).toHaveAttribute('aria-invalid', 'true');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));
    expect(await within(dialog).findByText(/No entendemos esas coordenadas/, { selector: '.alert' })).toBeInTheDocument();
    expect(branchesApi.update).not.toHaveBeenCalled();

    // A link of the place works too: its point is in it.
    fireEvent.change(field, { target: { value: 'https://www.google.com/maps/place/Z%C3%B3calo/@19.04,-98.19,17z/data=!3m1!4b1!4m6!3m5!8m2!3d19.0435774!4d-98.1986262' } });
    expect(within(dialog).getByRole('link', { name: 'Revisar en el mapa' }))
      .toHaveAttribute('href', 'https://www.google.com/maps/search/?api=1&query=19.043577,-98.198626');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));
    await vi.waitFor(() => expect(branchesApi.update).toHaveBeenCalledTimes(1));
    expect(vi.mocked(branchesApi.update).mock.calls[0][1].location).toEqual({ latitude: 19.043577, longitude: -98.198626 });

    // The main branch shows its point; emptied, it leaves the map.
    fireEvent.click(await screen.findByRole('button', { name: 'Editar Sucursal principal' }));
    dialog = screen.getByRole('dialog', { name: 'Editar Sucursal principal' });
    expect(within(dialog).getByLabelText('Ubicación en el mapa (opcional)')).toHaveValue('19.043577, -98.198626');
    fireEvent.change(within(dialog).getByLabelText('Ubicación en el mapa (opcional)'), { target: { value: '' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Guardar cambios' }));
    await vi.waitFor(() => expect(branchesApi.update).toHaveBeenCalledTimes(2));
    expect(vi.mocked(branchesApi.update).mock.calls[1][1].location).toEqual({ latitude: null, longitude: null });
  });

  it('fills the point with where the owner stands, and says what to do when the browser does not tell (Fase 24G)', async () => {
    const getCurrentPosition = vi.fn((success: PositionCallback) =>
      success({ coords: { latitude: 19.04357749, longitude: -98.19862651 } } as GeolocationPosition));
    Object.defineProperty(navigator, 'geolocation', { value: { getCurrentPosition }, configurable: true });
    renderBranches();

    fireEvent.click(await screen.findByRole('button', { name: 'Editar Centro' }));
    const dialog = screen.getByRole('dialog', { name: 'Editar Centro' });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Usar mi ubicación' }));
    expect(within(dialog).getByLabelText('Ubicación en el mapa (opcional)')).toHaveValue('19.043577, -98.198627');

    getCurrentPosition.mockImplementation(((_: PositionCallback, failure: PositionErrorCallback) =>
      failure({ code: 1, PERMISSION_DENIED: 1 } as GeolocationPositionError)) as never);
    fireEvent.click(within(dialog).getByRole('button', { name: 'Usar mi ubicación' }));
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Permite tu ubicación en el navegador, o pega las coordenadas desde Google Maps.');
  });

  it('starts a new branch serving like the main one, and sends it', async () => {
    vi.mocked(branchesApi.getAll).mockResolvedValue([
      branch({ serviceMode: BranchServiceMode.DeliveryOnly }),
    ] as Awaited<ReturnType<typeof branchesApi.getAll>>);
    vi.mocked(branchesApi.create).mockResolvedValue(branch({ id: 'b3' }) as never);
    renderBranches();

    await screen.findByText('Sucursal principal');
    fireEvent.click(screen.getByRole('button', { name: /Nueva sucursal/ }));
    const dialog = screen.getByRole('dialog', { name: 'Nueva sucursal' });
    expect(within(dialog).getByRole('radio', { name: 'Solo a domicilio, sin local' })).toBeChecked();
    // Not the main one: its address never reaches the menu, so there is nothing to warn about.
    expect(within(dialog).queryByText(PRIVATE_HINT)).not.toBeInTheDocument();
    expect(within(dialog).queryByText(MENU_HINT)).not.toBeInTheDocument();

    fireEvent.change(within(dialog).getByLabelText('Nombre'), { target: { value: 'Cocina norte' } });
    fireEvent.change(within(dialog).getByLabelText('Clave'), { target: { value: 'norte' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'Crear sucursal' }));
    await vi.waitFor(() => expect(branchesApi.create).toHaveBeenCalledWith(expect.objectContaining({
      name: 'Cocina norte', code: 'NORTE', serviceMode: BranchServiceMode.DeliveryOnly, isDefault: false,
    })));
  });

  it('says what the menu will show before making a branch the main one', async () => {
    vi.mocked(branchesApi.getAll).mockResolvedValue([
      branch({}),
      branch({ id: 'b2', name: 'Centro', code: 'CENTRO', isDefault: false }),
      branch({ id: 'b3', name: 'Cocina', code: 'COCINA', isDefault: false, serviceMode: BranchServiceMode.DeliveryOnly }),
    ] as Awaited<ReturnType<typeof branchesApi.getAll>>);
    confirm.mockResolvedValue(false);
    renderBranches();

    fireEvent.click(await screen.findByRole('button', { name: 'Hacer principal Cocina' }));
    await vi.waitFor(() => expect(confirm).toHaveBeenCalledTimes(1));
    expect(confirm.mock.calls[0][0].message).toContain('Tu menú en línea dirá «Solo a domicilio» y no mostrará ninguna dirección.');

    fireEvent.click(screen.getByRole('button', { name: 'Hacer principal Centro' }));
    await vi.waitFor(() => expect(confirm).toHaveBeenCalledTimes(2));
    expect(confirm.mock.calls[1][0].message).toContain('Su dirección se mostrará en tu menú en línea.');
    expect(branchesApi.setDefault).not.toHaveBeenCalled();
  });
});
