import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { InventoryTransfer } from '../../api/inventoryTransfers';
import { apiRequest } from '../../api/typedClient';
import { UnitType } from '../../types';
import TransferDetail from './TransferDetail';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

const request = vi.mocked(apiRequest);

const TRANSFER = {
  id: 't1', transferNumber: 'TRF-20261021-0001', fromLocationId: 'l1', fromLocationName: 'Bodega', toLocationId: 'l2',
  toLocationName: 'Mostrador', transferredAt: '2026-10-21T18:00:00Z', transferredBy: 'encargado', lineCount: 2, totalCost: 195,
  notes: 'Para el fin de semana', outMovementId: 'm-out', outMovementNumber: 'INV-20261021-0007', inMovementId: 'm-in',
  inMovementNumber: 'INV-20261021-0008',
  lines: [
    { supplyItemId: 's3', supplyItemName: 'Azúcar estándar', unit: UnitType.Kg, quantity: 2.5, unitCost: 30, totalCost: 75,
      fromQuantityBefore: 10, fromQuantityAfter: 7.5, toQuantityBefore: 0, toQuantityAfter: 2.5 },
    { supplyItemId: 's1', supplyItemName: 'Refresco de cola 600 ml', unit: UnitType.Unit, quantity: 10, unitCost: 12, totalCost: 120,
      fromQuantityBefore: 24, fromQuantityAfter: 14, toQuantityBefore: 6, toQuantityAfter: 16 },
  ],
} as InventoryTransfer;

describe('TransferDetail', () => {
  beforeEach(() => {
    request.mockReset();
    request.mockResolvedValue(TRANSFER as never);
  });

  it('shows what moved, what each location had and opens its two movements', async () => {
    const user = userEvent.setup();
    const onOpenMovement = vi.fn();
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <TransferDetail transferId="t1" onBack={vi.fn()} onOpenMovement={onOpenMovement} />
      </QueryClientProvider>,
    );

    expect(await screen.findByRole('heading', { name: 'TRF-20261021-0001 · Bodega → Mostrador' })).toHaveFocus();
    expect(screen.getByText(/Transfirió encargado/)).toHaveTextContent('2 artículos');
    expect(screen.getByText('Para el fin de semana')).toBeInTheDocument();
    const sugar = screen.getByRole('row', { name: /Azúcar estándar/ });
    expect(sugar).toHaveTextContent('2.5 kg');
    expect(sugar).toHaveTextContent('10 kg → 7.5 kg');
    expect(sugar).toHaveTextContent('0 kg → 2.5 kg');

    await user.click(screen.getByRole('button', { name: 'Ver salida INV-20261021-0007' }));
    await user.click(screen.getByRole('button', { name: 'Ver entrada INV-20261021-0008' }));
    expect(onOpenMovement.mock.calls).toEqual([['m-out'], ['m-in']]);
  });
});
