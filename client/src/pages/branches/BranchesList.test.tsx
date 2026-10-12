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
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => ({ confirm: vi.fn() }) }));

const MENU_HINT = /dirección de la sucursal principal se muestra en tu menú en línea/i;
const PRIVATE_HINT = 'Tu menú en línea dice «Solo a domicilio» y no muestra esta dirección. Solo la ve tu equipo.';

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
});
