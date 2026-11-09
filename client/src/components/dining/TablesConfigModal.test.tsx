import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { tablesApi, type DiningTable } from '../../api/tablesApi';
import TablesConfigModal from './TablesConfigModal';

vi.mock('../../api/tablesApi', () => ({ tablesApi: { getDiningRoom: vi.fn(), create: vi.fn(), update: vi.fn() } }));
const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

const table = (n: number): DiningTable => ({ id: `t${n}`, name: `Mesa ${n}`, area: null, seats: 4, sortOrder: n, isActive: true } as DiningTable);
const room = (count: number) => ({ tables: Array.from({ length: count }, (_, i) => table(i + 1)) });

function renderModal(onClose = vi.fn()) {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <TablesConfigModal onClose={onClose} />
    </QueryClientProvider>,
  );
  return onClose;
}

describe('TablesConfigModal', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('names a new table after the ones the dining room has, once it has read them', async () => {
    let answer: (value: unknown) => void = () => undefined;
    vi.mocked(tablesApi.getDiningRoom).mockReturnValue(new Promise((resolve) => { answer = resolve; }) as never);
    renderModal();

    const dialog = screen.getByRole('dialog', { name: 'Configurar mesas' });
    expect(within(dialog).getByRole('button', { name: 'Agregar mesa' })).toBeDisabled();

    answer(room(2));
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Agregar mesa' })).toBeEnabled());
    await userEvent.click(within(dialog).getByRole('button', { name: 'Agregar mesa' }));
    expect(within(dialog).getByLabelText('Nombre')).toHaveValue('Mesa 3');
  });

  it('stays open while a table is saved', async () => {
    const user = userEvent.setup();
    vi.mocked(tablesApi.getDiningRoom).mockResolvedValue(room(2) as never);
    let saved: (value: unknown) => void = () => undefined;
    vi.mocked(tablesApi.create).mockReturnValue(new Promise((resolve) => { saved = resolve; }) as never);
    const onClose = renderModal();

    const dialog = screen.getByRole('dialog', { name: 'Configurar mesas' });
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Agregar mesa' })).toBeEnabled());
    await user.click(within(dialog).getByRole('button', { name: 'Agregar mesa' }));
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }));

    expect(within(dialog).getByRole('button', { name: 'Guardando…' })).toBeDisabled();
    expect(within(dialog).getByRole('button', { name: 'Volver' })).toBeDisabled();
    expect(within(dialog).queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(onClose).not.toHaveBeenCalled();

    saved(table(3));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Mesa 3 agregada.'));
    expect(tablesApi.create).toHaveBeenCalledWith({ name: 'Mesa 3', area: null, seats: 4, sortOrder: 3 });
  });

  it('goes back to the list with the answer, and waits for the list before adding or editing another', async () => {
    const user = userEvent.setup();
    let reread: (value: unknown) => void = () => undefined;
    vi.mocked(tablesApi.getDiningRoom)
      .mockResolvedValueOnce(room(2) as never)
      .mockReturnValueOnce(new Promise((resolve) => { reread = resolve; }) as never);
    vi.mocked(tablesApi.create).mockResolvedValue(table(3) as never);
    renderModal();

    const dialog = screen.getByRole('dialog', { name: 'Configurar mesas' });
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Agregar mesa' })).toBeEnabled());
    await user.click(within(dialog).getByRole('button', { name: 'Agregar mesa' }));
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }));

    // Back in the list while it is read again: nothing is still «Guardando…», and nothing opens from the old list.
    const add = await within(dialog).findByRole('button', { name: 'Agregar mesa' });
    expect(add).toBeDisabled();
    expect(within(dialog).getByRole('button', { name: 'Editar Mesa 1' })).toBeDisabled();
    expect(within(dialog).queryByRole('button', { name: 'Guardando…' })).not.toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Close' })).toBeInTheDocument();

    reread(room(3));
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Agregar mesa' })).toBeEnabled());
    await user.click(within(dialog).getByRole('button', { name: 'Agregar mesa' }));
    expect(within(dialog).getByLabelText('Nombre')).toHaveValue('Mesa 4');
    expect(within(dialog).getByRole('button', { name: 'Guardar' })).toBeEnabled();
  });

  it('edits a table from what it has, and says why it could not be saved', async () => {
    const user = userEvent.setup();
    vi.mocked(tablesApi.getDiningRoom).mockResolvedValue(room(1) as never);
    vi.mocked(tablesApi.update).mockImplementation(() => Promise.reject(new Error('sin red')));
    renderModal();

    const dialog = screen.getByRole('dialog', { name: 'Configurar mesas' });
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Editar Mesa 1' })).toBeEnabled());
    await user.click(within(dialog).getByRole('button', { name: 'Editar Mesa 1' }));
    expect(within(dialog).getByLabelText('Nombre')).toHaveValue('Mesa 1');
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('No se pudo guardar la mesa.');
    expect(tablesApi.update).toHaveBeenCalledWith('t1', { id: 't1', name: 'Mesa 1', area: null, seats: 4, sortOrder: 1, isActive: true });
  });
});
