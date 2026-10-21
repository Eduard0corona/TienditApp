import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { InventoryCount, InventoryCountLine } from '../../api/inventoryCounts';
import { apiRequest } from '../../api/typedClient';
import { InventoryCountStatus, UnitType } from '../../types';
import CountSheet from './CountSheet';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

const request = vi.mocked(apiRequest);

const COUNT = {
  id: 'c1', countNumber: 'CNT-20261021-0001', inventoryLocationId: 'l1', inventoryLocationName: 'Bodega',
  status: InventoryCountStatus.InProgress, startedAt: '2026-10-21T18:00:00Z', startedBy: 'dueno',
  lineCount: 2, countedLineCount: 0, differenceLineCount: 0, surplusValue: 0, shortageValue: 0, revision: 2,
} as InventoryCount;

const SODA = {
  id: 'l-soda', supplyItemId: 's1', supplyItemName: 'Refresco de cola 600 ml', supplyCode: 'REF-600', supplyBarcode: '7501055300',
  unit: UnitType.Unit, isSupplyActive: true, countedQuantity: null, systemQuantity: null, currentQuantity: null,
} as InventoryCountLine;
const SUGAR = {
  id: 'l-sugar', supplyItemId: 's2', supplyItemName: 'Azúcar estándar', unit: UnitType.Kg, isSupplyActive: true,
  countedQuantity: null, systemQuantity: null, currentQuantity: null,
} as InventoryCountLine;
// What the system has, which only comes back once an item is counted.
const SYSTEM: Record<string, number> = { 'l-soda': 24, 'l-sugar': 10 };

function page(items: InventoryCountLine[], totalCount = items.length) {
  return { items, pageNumber: 1, totalPages: 1, totalCount, hasPreviousPage: false, hasNextPage: false, revision: 2 };
}

const puts = () => request.mock.calls.filter(([method]) => method === 'put');

function apiError(detail: string): AxiosError {
  const headers = new AxiosHeaders();
  const response = { status: 400, statusText: '', headers, config: { headers }, data: { code: 'validation', detail } } as AxiosResponse;
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, response);
}

function renderSheet(client = new QueryClient({ defaultOptions: { queries: { retry: false } } })) {
  return render(
    <QueryClientProvider client={client}>
      <CountSheet countId="c1" />
    </QueryClientProvider>,
  );
}

describe('CountSheet', () => {
  // What the API has saved, as the server keeps it.
  let saved: Record<string, number | null>;
  const withSaved = (line: InventoryCountLine) =>
    (line.id in saved ? { ...line, countedQuantity: saved[line.id], systemQuantity: saved[line.id] === null ? null : SYSTEM[line.id] } : line);

  beforeEach(() => {
    saved = {};
    request.mockReset();
    request.mockImplementation(async (method, path, options) => {
      if (method === 'get' && path === '/api/v1/inventory/counts/{id}/lines') return page([SODA, SUGAR].map(withSaved)) as never;
      if (method === 'put' && path === '/api/v1/inventory/counts/{id}/lines') {
        const entry = (options?.body as unknown as { lines: { lineId: string; countedQuantity: number | null }[] }).lines[0];
        saved[entry.lineId] = entry.countedQuantity;
        const line = entry.lineId === SODA.id ? SODA : SUGAR;
        return { count: { ...COUNT, countedLineCount: 1, revision: 2 + puts().length }, lines: [withSaved(line)] } as never;
      }
      throw new Error(`${method} ${path}`);
    });
  });

  it('saves what was counted when the box is left, without showing what the system has', async () => {
    const user = userEvent.setup();
    renderSheet();

    const soda = await screen.findByLabelText('Refresco de cola 600 ml');
    await user.type(soda, '22');
    await user.tab();

    expect(await screen.findByText('Guardado')).toBeInTheDocument();
    expect(request).toHaveBeenCalledWith('put', '/api/v1/inventory/counts/{id}/lines', {
      path: { id: 'c1' },
      body: { lines: [{ lineId: 'l-soda', countedQuantity: 22 }] },
    });
    expect(screen.queryByText('24')).not.toBeInTheDocument();
  });

  it('goes to the next box with Enter, saving once', async () => {
    const user = userEvent.setup();
    renderSheet();

    await user.type(await screen.findByLabelText('Refresco de cola 600 ml'), '22{Enter}');

    expect(screen.getByLabelText('Azúcar estándar')).toHaveFocus();
    expect(await screen.findByText('Guardado')).toBeInTheDocument();
    expect(puts()).toHaveLength(1);
  });

  it('keeps what was saved when the sheet is shown again', async () => {
    const user = userEvent.setup();
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { unmount } = renderSheet(client);
    await user.type(await screen.findByLabelText('Refresco de cola 600 ml'), '22');
    await user.tab();
    expect(await screen.findByText('Guardado')).toBeInTheDocument();

    unmount();
    renderSheet(client);

    expect(await screen.findByLabelText('Refresco de cola 600 ml')).toHaveValue('22');
  });

  it('stays in the box with Enter when it does not take the quantity', async () => {
    const user = userEvent.setup();
    renderSheet();

    const soda = await screen.findByLabelText('Refresco de cola 600 ml');
    await user.type(soda, '2.5{Enter}');

    expect(soda).toHaveFocus();
    expect(screen.getByRole('alert')).toHaveTextContent('en piezas enteras');
    expect(puts()).toHaveLength(0);
  });

  it('says why it does not take a quantity, without sending it', async () => {
    const user = userEvent.setup();
    renderSheet();

    const soda = await screen.findByLabelText('Refresco de cola 600 ml');
    await user.type(soda, '2.5');
    await user.tab();

    expect(await screen.findByRole('alert')).toHaveTextContent('La cantidad contada va de 0 a 9,999,999, en piezas enteras.');
    expect(soda).toHaveAttribute('aria-invalid', 'true');
    expect(request).not.toHaveBeenCalledWith('put', expect.anything(), expect.anything());
  });

  it('shows what the API answered when it refuses the quantity', async () => {
    const user = userEvent.setup();
    request.mockImplementation(async (method) => {
      if (method === 'get') return page([SODA, SUGAR]) as never;
      throw apiError('El conteo ya se aplicó o se canceló: ya no cambia.');
    });
    renderSheet();

    await user.type(await screen.findByLabelText('Azúcar estándar'), '1.25');
    await user.tab();

    expect(await screen.findByRole('alert')).toHaveTextContent('El conteo ya se aplicó o se canceló: ya no cambia.');
  });

  it('puts the cursor in the box of the item whose code was scanned', async () => {
    const user = userEvent.setup();
    request.mockImplementation(async (_method, _path, options) => {
      const search = (options?.query as { search?: string } | undefined)?.search;
      return page(search ? [SODA] : [SODA, SUGAR]) as never;
    });
    renderSheet();
    await screen.findByLabelText('Azúcar estándar');

    await user.type(screen.getByRole('searchbox', { name: 'Buscar o escanear un código' }), '7501055300');

    await waitFor(() => expect(screen.getByLabelText('Refresco de cola 600 ml')).toHaveFocus());
  });

  it('goes back to the search after a scanned item is counted, ready for the next code', async () => {
    const user = userEvent.setup();
    request.mockImplementation(async (method, _path, options) => {
      if (method === 'put') {
        return { count: { ...COUNT, countedLineCount: 1, revision: 3 }, lines: [{ ...SODA, countedQuantity: 3, systemQuantity: 24 }] } as never;
      }
      const search = (options?.query as { search?: string } | undefined)?.search;
      return page(search ? [SODA] : [SODA, SUGAR]) as never;
    });
    renderSheet();
    await screen.findByLabelText('Azúcar estándar');
    const searchBox = screen.getByRole('searchbox', { name: 'Buscar o escanear un código' });
    await user.type(searchBox, '7501055300');
    await waitFor(() => expect(screen.getByLabelText('Refresco de cola 600 ml')).toHaveFocus());

    await user.keyboard('3{Enter}');

    expect(searchBox).toHaveFocus();
    // The code is selected: the next scan replaces it instead of landing in the quantity.
    expect((searchBox as HTMLInputElement).selectionStart).toBe(0);
    expect((searchBox as HTMLInputElement).selectionEnd).toBe('7501055300'.length);
    await waitFor(() => expect(puts()).toHaveLength(1));
    expect(puts()[0][2]).toEqual({ path: { id: 'c1' }, body: { lines: [{ lineId: 'l-soda', countedQuantity: 3 }] } });
  });

  it('reads what is left from its first page after counting, so no item is skipped', async () => {
    const user = userEvent.setup();
    request.mockImplementation(async (method) => {
      if (method === 'put') {
        return { count: { ...COUNT, countedLineCount: 1, revision: 3 }, lines: [{ ...SODA, countedQuantity: 22, systemQuantity: 24 }] } as never;
      }
      // 120 items left to count, 50 a page.
      return page([SODA, SUGAR], 120) as never;
    });
    renderSheet();
    await screen.findByLabelText('Azúcar estándar');
    await user.selectOptions(screen.getByLabelText('Mostrar'), 'pending');
    await user.type(await screen.findByLabelText('Refresco de cola 600 ml'), '22');
    await user.tab();
    expect(await screen.findByText('Guardado')).toBeInTheDocument();
    const reads = () => request.mock.calls.filter(([method]) => method === 'get');
    const before = reads().length;

    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));

    await waitFor(() => expect(reads().length).toBeGreaterThan(before));
    expect(reads().slice(before).every(([, , options]) => (options?.query as unknown as { pageNumber?: number }).pageNumber === 1)).toBe(true);
  });
});
