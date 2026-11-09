import { render, screen, waitFor, within } from '@testing-library/react';
import { useState } from 'react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from '../../api/typedClient';
import { InventoryCountStatus, type InventoryLocationDto, type SupplyItemDto } from '../../types';
import NewCountModal from './NewCountModal';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

const request = vi.mocked(apiRequest);

const LOCATIONS = [
  { id: 'l1', name: 'Bodega', code: 'BOD', isDefault: true, isActive: true },
  { id: 'l2', name: 'Mostrador', code: 'MOS', isDefault: false, isActive: true },
] as InventoryLocationDto[];
const SUPPLIES = [
  { id: 's1', name: 'Refresco de cola 600 ml', categoryName: 'Bebidas', barcode: '7501055300' },
  { id: 's2', name: 'Papas 45 g', categoryName: 'Botanas' },
] as SupplyItemDto[];

function renderModal(onStarted = vi.fn()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <NewCountModal show activeLocations={LOCATIONS} supplyOptions={SUPPLIES} onHide={vi.fn()} onStarted={onStarted} />
    </QueryClientProvider>,
  );
  return onStarted;
}

describe('NewCountModal', () => {
  beforeEach(() => {
    request.mockReset();
    request.mockResolvedValue({ id: 'c1', countNumber: 'CNT-20261021-0001', status: InventoryCountStatus.InProgress, lineCount: 1 } as never);
  });

  it('counts everything with stock in the default location unless told otherwise', async () => {
    const user = userEvent.setup();
    const onStarted = renderModal();

    await user.type(screen.getByLabelText('Notas (opcional)'), 'Cierre de mes');
    await user.click(screen.getByRole('button', { name: 'Empezar conteo' }));

    await waitFor(() => expect(request).toHaveBeenCalledWith('post', '/api/v1/inventory/counts', {
      body: { inventoryLocationId: 'l1', categoryName: null, supplyItemIds: [], notes: 'Cierre de mes' },
    }));
    expect(onStarted).toHaveBeenCalledWith(expect.objectContaining({ id: 'c1' }));
  });

  it('counts one category of the location chosen', async () => {
    const user = userEvent.setup();
    renderModal();

    await user.selectOptions(screen.getByLabelText('Ubicación'), 'l2');
    await user.click(screen.getByLabelText('Una categoría'));
    await user.click(screen.getByRole('button', { name: 'Empezar conteo' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Elige la categoría que vas a contar.');

    await user.selectOptions(screen.getByLabelText('Categoría'), 'Botanas');
    await user.click(screen.getByRole('button', { name: 'Empezar conteo' }));

    await waitFor(() => expect(request).toHaveBeenCalledWith('post', '/api/v1/inventory/counts', {
      body: { inventoryLocationId: 'l2', categoryName: 'Botanas', supplyItemIds: [], notes: null },
    }));
  });

  it('counts only the items chosen, found by their barcode', async () => {
    const user = userEvent.setup();
    renderModal();

    await user.click(screen.getByLabelText('Solo los artículos que elijas'));
    await user.click(screen.getByRole('button', { name: 'Empezar conteo' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Elige al menos un artículo.');

    await user.type(screen.getByLabelText('Buscar artículo'), '75010');
    expect(screen.queryByLabelText(/Papas/)).not.toBeInTheDocument();
    await user.click(screen.getByLabelText(/Refresco de cola/));
    await user.click(screen.getByRole('button', { name: 'Empezar conteo' }));

    await waitFor(() => expect(request).toHaveBeenCalledWith('post', '/api/v1/inventory/counts', {
      body: { inventoryLocationId: 'l1', categoryName: null, supplyItemIds: ['s1'], notes: null },
    }));
  });
  it('cannot be closed while the count is being started', async () => {
    const user = userEvent.setup();
    request.mockReturnValue(new Promise(() => undefined) as never);
    const onHide = vi.fn();
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <NewCountModal show activeLocations={LOCATIONS} supplyOptions={SUPPLIES} onHide={onHide} onStarted={vi.fn()} />
      </QueryClientProvider>,
    );
    const dialog = screen.getByRole('dialog', { name: 'Nuevo conteo' });

    await user.click(within(dialog).getByRole('button', { name: 'Empezar conteo' }));
    expect(within(dialog).getByRole('button', { name: 'Empezando…' })).toBeDisabled();
    expect(within(dialog).getByRole('button', { name: 'Cancelar' })).toBeDisabled();
    expect(within(dialog).queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(onHide).not.toHaveBeenCalled();
  });

  it('starts one count, even with a double click when the server answers at once', async () => {
    const user = userEvent.setup();
    // As Inventario does: starting closes the dialog.
    function Harness() {
      const [show, setShow] = useState(true);
      return <NewCountModal show={show} activeLocations={LOCATIONS} supplyOptions={SUPPLIES} onHide={() => setShow(false)} onStarted={() => setShow(false)} />;
    }
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(<QueryClientProvider client={client}><Harness /></QueryClientProvider>);

    // The second click lands while the dialog fades out.
    await user.dblClick(within(screen.getByRole('dialog', { name: 'Nuevo conteo' })).getByRole('button', { name: 'Empezar conteo' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(request.mock.calls.filter(([method]) => method === 'post')).toHaveLength(1);
  });
});
