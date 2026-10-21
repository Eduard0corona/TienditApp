import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { InventoryReconciliation, InventoryReconciliationDetail, InventoryReconciliationItem } from '../../api/inventoryReconciliation';
import { apiRequest } from '../../api/typedClient';
import ConfirmProvider from '../../components/feedback/ConfirmProvider';
import { InventoryReconciliationEventKind, UnitType, type InventoryLocationDto } from '../../types';
import LedgerReview from './LedgerReview';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

const request = vi.mocked(apiRequest);

const LOCATIONS = [
  { id: 'l1', name: 'Bodega', isActive: true, isDefault: true },
  { id: 'l2', name: 'Mostrador', isActive: true, isDefault: false },
] as InventoryLocationDto[];

const SODA: InventoryReconciliationItem = {
  supplyItemId: 's1', supplyItemName: 'Refresco de cola 600 ml', unit: UnitType.Unit, isActive: true, inventoryLocationId: 'l1',
  inventoryLocationName: 'Bodega', onHandQuantity: 30, historyQuantity: 24, difference: 6, movementCount: 3,
};
const SUGAR: InventoryReconciliationItem = {
  supplyItemId: 's3', supplyItemName: 'Azúcar estándar', unit: UnitType.Kg, isActive: false, inventoryLocationId: 'l1',
  inventoryLocationName: 'Bodega', onHandQuantity: 9.75, historyQuantity: 10, difference: -0.25, movementCount: 2,
};

const review = (items: InventoryReconciliationItem[], checkedCount = 12) => ({
  checkedCount,
  mismatches: { items, pageNumber: 1, totalPages: 1, totalCount: items.length, hasPreviousPage: false, hasNextPage: false },
}) as InventoryReconciliation;

const DETAIL = {
  ...SODA,
  reviewedCount: 3,
  eventCount: 1,
  events: [{ kind: InventoryReconciliationEventKind.AfterLastMovement, quantity: 6, quantityFrom: 24, quantityTo: 30, movementNumber: 'INV-20261021-0003' }],
} as InventoryReconciliationDetail;

function apiError(status: number, code: string, detail: string): AxiosError {
  const headers = new AxiosHeaders();
  const response = { status, statusText: '', headers, config: { headers }, data: { code, detail } } as AxiosResponse;
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, response);
}

function renderReview(onReconciled = vi.fn()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
        <LedgerReview locations={LOCATIONS} onBack={vi.fn()} onReconciled={onReconciled} />
      </ConfirmProvider>
    </QueryClientProvider>,
  );
  return onReconciled;
}

describe('LedgerReview', () => {
  beforeEach(() => {
    request.mockReset();
  });

  it('says that everything adds up when no stock differs from its history', async () => {
    request.mockResolvedValue(review([], 40) as never);
    renderReview();

    expect(await screen.findByText('Todo cuadra: las 40 existencias revisadas son lo que suman sus movimientos.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: '¿Cuadra el historial con las existencias?' })).toHaveFocus();
  });

  it('lists what does not add up and records one difference after confirming it, without moving the stock', async () => {
    const user = userEvent.setup();
    request.mockImplementation((method: string) => Promise.resolve(method === 'post'
      ? { reconciledCount: 1, alreadyAddedUpCount: 0, movements: [{ id: 'm9', movementNumber: 'INV-20261021-0009', movementType: 3 }] }
      : review([SODA, SUGAR])) as never);
    const onReconciled = renderReview();

    expect(await screen.findByText(/Se revisaron 12 existencias: 2 no cuadran con su historial\./)).toBeInTheDocument();
    const sugar = screen.getByRole('row', { name: /Azúcar estándar/ });
    expect(sugar).toHaveTextContent('Inactivo');
    expect(sugar).toHaveTextContent('9.75 kg');
    expect(sugar).toHaveTextContent('−0.25 kg');

    await user.click(screen.getByRole('button', { name: 'Cuadrar Refresco de cola 600 ml en Bodega' }));
    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveTextContent('Se anota +6 en el historial de Refresco de cola 600 ml en Bodega, para que sume 30. La existencia no cambia');
    await user.click(within(dialog).getByRole('button', { name: 'Cuadrar' }));

    expect(request).toHaveBeenCalledWith('post', '/api/v1/inventory/reconciliation', {
      body: { inventoryLocationId: 'l1', lines: [{ supplyItemId: 's1', expectedDifference: 6 }] },
    });
    expect(onReconciled).toHaveBeenCalledWith('Listo: el historial de Refresco de cola 600 ml ya suma su existencia (INV-20261021-0009).');
  });

  it('explains where a stock stops adding up, and says when what does not add up changed', async () => {
    const user = userEvent.setup();
    request.mockImplementation((method: string, path: string) => {
      if (method === 'post') return Promise.reject(apiError(409, 'conflict', 'Cambió lo que no cuadra de: Refresco de cola 600 ml (ahora +7). Vuelve a revisar antes de cuadrar.'));
      return Promise.resolve(path.endsWith('{inventoryLocationId}') ? DETAIL : review([SODA])) as never;
    });
    const onReconciled = renderReview();

    await user.click(await screen.findByRole('button', { name: 'Por qué no cuadra Refresco de cola 600 ml en Bodega' }));
    const detail = await screen.findByRole('dialog', { name: 'Por qué no cuadra' });
    expect(await within(detail).findByText('Después de INV-20261021-0003 la existencia pasó de 24 a 30 sin un movimiento.')).toBeInTheDocument();
    expect(detail).toHaveTextContent('Existencia 30 · el historial suma 24 · diferencia +6');

    await user.click(within(detail).getByRole('button', { name: 'Cuadrar' }));
    const confirm = await screen.findByRole('dialog', { name: 'Cuadrar el historial' });
    await user.click(within(confirm).getByRole('button', { name: 'Cuadrar' }));

    expect(await screen.findByText('Cambió lo que no cuadra de: Refresco de cola 600 ml (ahora +7). Vuelve a revisar antes de cuadrar.')).toBeInTheDocument();
    expect(onReconciled).not.toHaveBeenCalled();
  });
});
