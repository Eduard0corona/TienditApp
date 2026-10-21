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

function page(items: InventoryCountLine[]) {
  return { items, pageNumber: 1, totalPages: 1, totalCount: items.length, hasPreviousPage: false, hasNextPage: false };
}

function apiError(detail: string): AxiosError {
  const headers = new AxiosHeaders();
  const response = { status: 400, statusText: '', headers, config: { headers }, data: { code: 'validation', detail } } as AxiosResponse;
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, response);
}

function renderDetail(handlers: { onPosted?: (message: string) => void; onOpenMovement?: (id: string) => void } = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <CountDetail
        countId="c1"
        supplyOptions={[]}
        onBack={vi.fn()}
        onOpenMovement={handlers.onOpenMovement ?? vi.fn()}
        onPosted={handlers.onPosted ?? vi.fn()}
        onMessage={vi.fn()}
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
      if (method === 'get' && path === '/api/v1/inventory/counts/{id}/lines') return page([SODA]) as never;
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
    expect(screen.getByText('Falta contar 1 artículo: se queda como está.')).toBeInTheDocument();
    expect(await screen.findByText('Refresco de cola 600 ml')).toBeInTheDocument();
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
    await user.click(await screen.findByRole('button', { name: 'Ver el ajuste de salida INV-20261021-0007' }));
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
      if (method === 'post') throw apiError('El conteo cambió mientras lo revisabas: revísalo otra vez antes de aplicarlo.');
      return (path === '/api/v1/inventory/counts/{id}' ? count : page([SODA])) as never;
    });
    await user.click(screen.getByRole('button', { name: 'Aplicar conteo' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('El conteo cambió mientras lo revisabas');
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
