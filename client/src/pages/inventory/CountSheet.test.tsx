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
  unit: UnitType.Unit, isSupplyActive: true, countedQuantity: null, systemQuantity: null, currentQuantity: 24,
} as InventoryCountLine;
const SUGAR = {
  id: 'l-sugar', supplyItemId: 's2', supplyItemName: 'Azúcar estándar', unit: UnitType.Kg, isSupplyActive: true,
  countedQuantity: null, systemQuantity: null, currentQuantity: 10,
} as InventoryCountLine;

function page(items: InventoryCountLine[]) {
  return { items, pageNumber: 1, totalPages: 1, totalCount: items.length, hasPreviousPage: false, hasNextPage: false };
}

function apiError(detail: string): AxiosError {
  const headers = new AxiosHeaders();
  const response = { status: 400, statusText: '', headers, config: { headers }, data: { code: 'validation', detail } } as AxiosResponse;
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, response);
}

function renderSheet() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <CountSheet countId="c1" />
    </QueryClientProvider>,
  );
}

describe('CountSheet', () => {
  beforeEach(() => {
    request.mockReset();
    request.mockImplementation(async (method, path, options) => {
      if (method === 'get' && path === '/api/v1/inventory/counts/{id}/lines') return page([SODA, SUGAR]) as never;
      if (method === 'put' && path === '/api/v1/inventory/counts/{id}/lines') {
        const entry = (options?.body as unknown as { lines: { lineId: string; countedQuantity: number | null }[] }).lines[0];
        const line = entry.lineId === SODA.id ? SODA : SUGAR;
        return {
          count: { ...COUNT, countedLineCount: 1, revision: 3 },
          lines: [{ ...line, countedQuantity: entry.countedQuantity, systemQuantity: line.currentQuantity }],
        } as never;
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

  it('goes to the next box with Enter after saving', async () => {
    const user = userEvent.setup();
    renderSheet();

    await user.type(await screen.findByLabelText('Refresco de cola 600 ml'), '22{Enter}');

    expect(screen.getByLabelText('Azúcar estándar')).toHaveFocus();
    await waitFor(() => expect(request).toHaveBeenCalledWith('put', expect.anything(), expect.anything()));
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
});
