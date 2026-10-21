import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { InventoryCount, InventoryCountLine } from '../../api/inventoryCounts';
import { apiRequest } from '../../api/typedClient';
import { useConfirm } from '../../hooks/useConfirm';
import { InventoryCountStatus, UnitType } from '../../types';
import CountDetail from './CountDetail';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: vi.fn() }));

const request = vi.mocked(apiRequest);
const confirm = vi.fn();

const OPEN = {
  id: 'c1', countNumber: 'CNT-20261021-0001', inventoryLocationId: 'l1', inventoryLocationName: 'Bodega',
  status: InventoryCountStatus.InProgress, startedAt: '2026-10-21T18:00:00Z', startedBy: 'dueno', categoryName: null,
  lineCount: 3, countedLineCount: 2, differenceLineCount: 1, surplusValue: 0, shortageValue: 24, revision: 5,
} as InventoryCount;

const SODA = {
  id: 'l-soda', supplyItemId: 's1', supplyItemName: 'Refresco de cola 600 ml', unit: UnitType.Unit, isSupplyActive: true,
  countedQuantity: 22, systemQuantity: 24, difference: -2, currentQuantity: 21, resultingQuantity: 19, differenceValue: -24,
  countedBy: 'cajero',
} as InventoryCountLine;

function page(items: InventoryCountLine[], revision = 5) {
  return { items, pageNumber: 1, totalPages: 1, totalCount: items.length, hasPreviousPage: false, hasNextPage: false, revision };
}

function apiError(detail: string, status = 400, code = 'business_rule'): AxiosError {
  const headers = new AxiosHeaders();
  const response = { status, statusText: '', headers, config: { headers }, data: { code, detail } } as AxiosResponse;
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, response);
}

const isReview = (options: unknown) => (options as { query?: { withDifference?: boolean } } | undefined)?.query?.withDifference === true;

function renderDetail(handlers: {
  onPosted?: (message: string) => void;
  onOpenMovement?: (id: string) => void;
  onMessage?: (message: string) => void;
} = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <CountDetail
        countId="c1"
        supplyOptions={[]}
        onBack={vi.fn()}
        onOpenMovement={handlers.onOpenMovement ?? vi.fn()}
        onPosted={handlers.onPosted ?? vi.fn()}
        onMessage={handlers.onMessage ?? vi.fn()}
      />
    </QueryClientProvider>,
  );
}

describe('CountDetail', () => {
  let count: InventoryCount;

  beforeEach(() => {
    count = OPEN;
    request.mockReset();
    confirm.mockReset();
    vi.mocked(useConfirm).mockReturnValue({ confirm } as unknown as ReturnType<typeof useConfirm>);
    request.mockImplementation(async (method, path) => {
      if (method === 'get' && path === '/api/v1/inventory/counts/{id}') return count as never;
      if (method === 'get' && path === '/api/v1/inventory/counts/{id}/lines') return page([SODA], count.revision) as never;
      if (method === 'post' && path === '/api/v1/inventory/counts/{id}/post') {
        count = { ...OPEN, status: InventoryCountStatus.Posted, postedBy: 'dueno', postedAt: '2026-10-21T19:00:00Z',
          decreaseMovementId: 'm1', decreaseMovementNumber: 'INV-20261021-0007' };
        return count as never;
      }
      throw new Error(`${method} ${path}`);
    });
  });

  it('reviews the differences with what they are worth, and what was not counted stays', async () => {
    const user = userEvent.setup();
    renderDetail();

    await user.click(await screen.findByRole('button', { name: 'Revisar diferencias' }));

    expect(await screen.findByText('2 de 3')).toBeInTheDocument();
    expect(screen.getByText('Falta por contar 1 artículo: se queda como está.')).toBeInTheDocument();
    // The sheet stays behind, hidden, with its own row of the soda.
    expect(await screen.findByText('Refresco de cola 600 ml', { selector: 'div' })).toBeInTheDocument();
    expect(screen.getByText('−2')).toBeInTheDocument();
    expect(screen.getByText('−$24.00')).toBeInTheDocument();
    expect(screen.getByText('Quedará en')).toBeInTheDocument();
  });

  it('posts the revision it reviewed after asking, and shows its adjustment', async () => {
    const user = userEvent.setup();
    const onPosted = vi.fn();
    const onOpenMovement = vi.fn();
    confirm.mockResolvedValue(true);
    renderDetail({ onPosted, onOpenMovement });

    await user.click(await screen.findByRole('button', { name: 'Revisar diferencias' }));
    await user.click(await screen.findByRole('button', { name: 'Aplicar conteo' }));

    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({
      title: 'Aplicar el conteo CNT-20261021-0001',
      message: expect.stringContaining('Se ajustan 1 artículo: sobrante $0.00 y faltante $24.00.'),
    }));
    await waitFor(() => expect(request).toHaveBeenCalledWith('post', '/api/v1/inventory/counts/{id}/post', {
      path: { id: 'c1' }, body: { expectedRevision: 5 },
    }));
    expect(onPosted).toHaveBeenCalledWith('Conteo CNT-20261021-0001 aplicado.');
    await user.click(await screen.findByRole('button', { name: 'Ver salida INV-20261021-0007' }));
    expect(onOpenMovement).toHaveBeenCalledWith('m1');
    expect(screen.getByText('Quedó en')).toBeInTheDocument();
  });

  it('does not post when the question is declined, and says why the API refused', async () => {
    const user = userEvent.setup();
    confirm.mockResolvedValueOnce(false).mockResolvedValueOnce(true);
    renderDetail();
    await user.click(await screen.findByRole('button', { name: 'Revisar diferencias' }));

    await user.click(await screen.findByRole('button', { name: 'Aplicar conteo' }));
    expect(request).not.toHaveBeenCalledWith('post', expect.anything(), expect.anything());

    request.mockImplementation(async (method, path) => {
      if (method === 'post') throw apiError('El conteo cambió mientras lo revisabas: revísalo otra vez antes de aplicarlo.', 409, 'concurrency_conflict');
      return (path === '/api/v1/inventory/counts/{id}' ? count : page([SODA])) as never;
    });
    await user.click(screen.getByRole('button', { name: 'Aplicar conteo' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('El conteo cambió mientras lo revisabas: ya se leyó otra vez, revísalo y vuelve a aplicarlo.');
  });

  it('reviews again what was counted meanwhile, and posts that revision', async () => {
    const user = userEvent.setup();
    confirm.mockResolvedValue(true);
    renderDetail();
    await user.click(await screen.findByRole('button', { name: 'Revisar diferencias' }));
    expect(await screen.findByText('−2')).toBeInTheDocument();

    // Back to the sheet, the soda is counted again (23) and reviewed once more.
    await user.click(screen.getByRole('button', { name: 'Contar' }));
    count = { ...OPEN, revision: 6, shortageValue: 12 };
    const recounted = { ...SODA, countedQuantity: 23, difference: -1, resultingQuantity: 20, differenceValue: -12 };
    request.mockImplementation(async (method, path, options) => {
      if (method === 'get' && path === '/api/v1/inventory/counts/{id}') return count as never;
      if (method === 'get') return page(isReview(options) ? [recounted] : [SODA], 6) as never;
      if (method === 'post') return { ...count, status: InventoryCountStatus.Posted } as never;
      throw new Error(`${method} ${path}`);
    });
    await user.click(screen.getByRole('button', { name: 'Revisar diferencias' }));

    expect(await screen.findByText('−1')).toBeInTheDocument();
    expect(screen.queryByText('−2')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Aplicar conteo' }));
    await waitFor(() => expect(request).toHaveBeenCalledWith('post', '/api/v1/inventory/counts/{id}/post', {
      path: { id: 'c1' }, body: { expectedRevision: 6 },
    }));
  });

  it('does not post items of another revision than the count shown, and reads them again', async () => {
    const user = userEvent.setup();
    let reads = 0;
    request.mockImplementation(async (method, path, options) => {
      if (method === 'get' && path === '/api/v1/inventory/counts/{id}') return count as never;
      // The first page reviewed was read before the last save: revision 4 against the count's 5.
      if (method === 'get' && isReview(options)) return page([SODA], ++reads === 1 ? 4 : 5) as never;
      if (method === 'get') return page([SODA]) as never;
      throw new Error(`${method} ${path}`);
    });
    renderDetail();

    await user.click(await screen.findByRole('button', { name: 'Revisar diferencias' }));

    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Aplicar conteo' })).toBeEnabled());
  });

  it('waits for what is being saved before letting the count be posted', async () => {
    const user = userEvent.setup();
    let answer: (value: unknown) => void = () => {};
    request.mockImplementation(async (method, path) => {
      if (method === 'get' && path === '/api/v1/inventory/counts/{id}') return count as never;
      if (method === 'get') return page([{ ...SODA, countedQuantity: null, systemQuantity: null, difference: null }], count.revision) as never;
      if (method === 'put') {
        return new Promise((resolve) => {
          answer = () => {
            count = { ...OPEN, revision: 6 };
            resolve({ count, lines: [SODA] });
          };
        }) as never;
      }
      throw new Error(`${method} ${path}`);
    });
    renderDetail();

    // Typed, and straight to the review: leaving the box saves it.
    await user.type(await screen.findByLabelText('Refresco de cola 600 ml'), '22');
    await user.click(screen.getByRole('button', { name: 'Revisar diferencias' }));

    expect(await screen.findByText('Guardando lo contado…')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Aplicar conteo' })).toBeDisabled();
    answer(undefined);
    await waitFor(() => expect(screen.queryByText('Guardando lo contado…')).not.toBeInTheDocument());
    await waitFor(() => expect(screen.getByRole('button', { name: 'Aplicar conteo' })).toBeEnabled());
  });

  it('clears what was counted of an item to count it again', async () => {
    const user = userEvent.setup();
    const onMessage = vi.fn();
    request.mockImplementation(async (method, path, options) => {
      if (method === 'get' && path === '/api/v1/inventory/counts/{id}') return count as never;
      if (method === 'get') return page([SODA], count.revision) as never;
      if (method === 'put') {
        count = { ...OPEN, countedLineCount: 1, differenceLineCount: 0, shortageValue: 0, revision: 6 };
        return { count, lines: [{ ...SODA, countedQuantity: null, systemQuantity: null, difference: null }] } as never;
      }
      throw new Error(`${method} ${path} ${JSON.stringify(options)}`);
    });
    renderDetail({ onMessage });
    await user.click(await screen.findByRole('button', { name: 'Revisar diferencias' }));

    // One under the item (phones) and one in its column (from md up): the CSS shows only one.
    await user.click((await screen.findAllByRole('button', { name: 'Volver a contar Refresco de cola 600 ml' }))[0]);

    await waitFor(() => expect(request).toHaveBeenCalledWith('put', '/api/v1/inventory/counts/{id}/lines', {
      path: { id: 'c1' }, body: { lines: [{ lineId: 'l-soda', countedQuantity: null }] },
    }));
    expect(onMessage).toHaveBeenCalledWith('Se borró lo contado de Refresco de cola 600 ml: cuéntalo otra vez en «Contar» (está en «Sin contar»).');
  });

  it('shows why a cancelled count closed, without values', async () => {
    count = { ...OPEN, status: InventoryCountStatus.Cancelled, cancelledBy: 'dueno', cancelledAt: '2026-10-21T19:00:00Z',
      cancelReason: 'Se contó la ubicación equivocada', surplusValue: null, shortageValue: null };
    renderDetail();

    expect(await screen.findByText('Cancelado sin mover existencias: Se contó la ubicación equivocada')).toBeInTheDocument();
    expect(screen.queryByText('Faltante')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Aplicar conteo' })).not.toBeInTheDocument();
  });
});
