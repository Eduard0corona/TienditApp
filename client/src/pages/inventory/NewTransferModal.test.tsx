import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { InventoryStock } from '../../api/inventoryTransfers';
import { apiRequest } from '../../api/typedClient';
import { UnitType, type InventoryLocationDto } from '../../types';
import NewTransferModal from './NewTransferModal';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

const request = vi.mocked(apiRequest);

const LOCATIONS = [
  { id: 'l1', name: 'Bodega', code: 'BOD', isDefault: true, isActive: true },
  { id: 'l2', name: 'Mostrador', code: 'MOS', isDefault: false, isActive: true },
  { id: 'l3', name: 'Cocina', code: 'COC', isDefault: false, isActive: true },
] as InventoryLocationDto[];

const stock = (supplyItemId: string, supplyItemName: string, onHandQuantity: number, extra: Partial<InventoryStock> = {}) =>
  ({ supplyItemId, supplyItemName, unit: UnitType.Unit, onHandQuantity, isSupplyActive: true, ...extra }) as InventoryStock;

const STOCK: Record<string, InventoryStock[]> = {
  l1: [
    stock('s1', 'Refresco de cola 600 ml', 24, { supplyBarcode: '7501055300' }),
    stock('s2', 'Papas 45 g', 15),
    stock('s3', 'Azúcar estándar', 10, { unit: UnitType.Kg }),
    stock('s4', 'Servilletas', 0),
    stock('s5', 'Vasos', 8, { isSupplyActive: false }),
  ],
  l2: [stock('s1', 'Refresco de cola 600 ml', 6)],
};

function apiError(detail: string, status = 400): AxiosError {
  const headers = new AxiosHeaders();
  const response = { status, statusText: '', headers, config: { headers }, data: { code: 'conflict', detail } } as AxiosResponse;
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, response);
}

function renderModal(onCreated = vi.fn(), locations = LOCATIONS) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <NewTransferModal show activeLocations={locations} onHide={vi.fn()} onCreated={onCreated} />
    </QueryClientProvider>,
  );
  return onCreated;
}

describe('NewTransferModal', () => {
  beforeEach(() => {
    request.mockReset();
    request.mockImplementation(((method: string, path: string, options?: { query?: { inventoryLocationId?: string } }) => {
      if (method === 'get' && path === '/api/v1/inventory/stocks') {
        const items = STOCK[options?.query?.inventoryLocationId ?? ''] ?? [];
        return Promise.resolve({ items, pageNumber: 1, totalPages: 1, totalCount: items.length, hasPreviousPage: false, hasNextPage: false });
      }
      return Promise.resolve({ id: 't1', transferNumber: 'TRF-20261021-0001', lineCount: 2 });
    }) as never);
  });

  it('offers what the main location has and sends each quantity with the key of the dialog', async () => {
    const user = userEvent.setup();
    const onCreated = renderModal();

    expect(screen.getByLabelText('De')).toHaveValue('l1');
    expect(screen.getByLabelText('A')).toHaveValue('l2');
    const offered = await screen.findByRole('list', { name: 'Artículos con existencias' });
    // Nothing to give of what has no stock or is inactive.
    expect(within(offered).getAllByRole('listitem').map((item) => item.textContent)).toEqual([
      expect.stringContaining('Azúcar estándar'),
      expect.stringContaining('Papas 45 g'),
      expect.stringContaining('Refresco de cola 600 ml'),
    ]);

    await user.type(screen.getByLabelText(/Agregar artículo/), '75010');
    await user.click(screen.getByRole('button', { name: 'Agregar Refresco de cola 600 ml' }));
    await user.clear(screen.getByLabelText(/Agregar artículo/));
    await user.click(screen.getByRole('button', { name: 'Agregar Azúcar estándar' }));
    expect(screen.queryByRole('button', { name: 'Agregar Refresco de cola 600 ml' })).not.toBeInTheDocument();
    await user.type(screen.getByLabelText('Cantidad de Refresco de cola 600 ml'), '10');
    await user.type(screen.getByLabelText('Cantidad de Azúcar estándar'), '2.5');
    await user.selectOptions(screen.getByLabelText('A'), 'l3');
    await user.type(screen.getByLabelText('Notas (opcional)'), 'Para el fin de semana');
    await user.click(screen.getByRole('button', { name: 'Transferir' }));

    await waitFor(() => expect(onCreated).toHaveBeenCalledWith(expect.objectContaining({ id: 't1' })));
    const sent = request.mock.calls.find(([method]) => method === 'post')!;
    expect(sent[1]).toBe('/api/v1/inventory/transfers');
    expect(sent[2]).toEqual({
      body: {
        idempotencyKey: expect.stringMatching(/^[0-9a-f-]{36}$/),
        fromLocationId: 'l1',
        toLocationId: 'l3',
        notes: 'Para el fin de semana',
        lines: [{ supplyItemId: 's1', quantity: 10 }, { supplyItemId: 's3', quantity: 2.5 }],
      },
    });
  });

  it('refuses more than the origin has, and another origin drops what was chosen', async () => {
    const user = userEvent.setup();
    renderModal();

    await user.click(await screen.findByRole('button', { name: 'Agregar Papas 45 g' }));
    await user.type(screen.getByLabelText('Cantidad de Papas 45 g'), '20');
    await user.click(screen.getByRole('button', { name: 'Transferir' }));
    expect(screen.getByRole('alert')).toHaveTextContent('En Bodega solo hay 15 de Papas 45 g.');
    expect(request).not.toHaveBeenCalledWith('post', expect.anything(), expect.anything());

    await user.selectOptions(screen.getByLabelText('De'), 'l2');
    expect(screen.queryByLabelText('Cantidad de Papas 45 g')).not.toBeInTheDocument();
    // The destination is never the origin.
    expect(screen.getByLabelText('A')).toHaveValue('l1');
    expect(await screen.findByRole('button', { name: 'Agregar Refresco de cola 600 ml' })).toBeInTheDocument();
  });

  it('keeps the same key when sent again after the API refused it', async () => {
    const user = userEvent.setup();
    const posts: unknown[] = [];
    const answer = request.getMockImplementation()!;
    request.mockImplementation(((method: string, path: string, options?: { body?: unknown }) => {
      if (method !== 'post') return answer(method as never, path as never, options as never);
      posts.push(options?.body);
      return posts.length === 1
        ? Promise.reject(apiError('Se vendió o se movió un artículo mientras se transfería: revisa lo que hay en el origen y vuelve a intentarlo.', 409))
        : Promise.resolve({ id: 't1', transferNumber: 'TRF-20261021-0001', lineCount: 1 });
    }) as never);
    const onCreated = renderModal();

    await user.click(await screen.findByRole('button', { name: 'Agregar Papas 45 g' }));
    await user.type(screen.getByLabelText('Cantidad de Papas 45 g'), '3');
    await user.click(screen.getByRole('button', { name: 'Transferir' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Se vendió o se movió un artículo mientras se transfería');
    await user.click(screen.getByRole('button', { name: 'Transferir' }));

    await waitFor(() => expect(onCreated).toHaveBeenCalled());
    expect((posts[0] as { idempotencyKey: string }).idempotencyKey).toBe((posts[1] as { idempotencyKey: string }).idempotencyKey);
  });

  it('asks for a second location when there is only one', () => {
    renderModal(vi.fn(), [LOCATIONS[0]]);

    expect(screen.getByText(/Para transferir necesitas dos ubicaciones activas/)).toHaveAttribute('role', 'status');
    expect(screen.getByRole('button', { name: 'Transferir' })).toBeDisabled();
  });
});
