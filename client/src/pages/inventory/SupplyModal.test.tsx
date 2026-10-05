import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { inventoryApi } from '../../api/inventoryApi';
import { UnitType, type SupplyItemDto } from '../../types';
import SupplyModal from './SupplyModal';

vi.mock('../../api/inventoryApi', () => ({
  inventoryApi: {
    createSupply: vi.fn(),
    updateSupply: vi.fn(),
  },
}));

const api = vi.mocked(inventoryApi);

// The stock of a product sold by the slice.
const productStock: SupplyItemDto = {
  id: 's1',
  name: 'Pan de elote',
  code: 'PAN-003',
  unit: UnitType.Slice,
  defaultCost: 18,
  lastCost: 18,
  averageCost: 18,
  minStock: 4,
  reorderPoint: 8,
  trackBatches: false,
  trackExpiration: false,
  isPerishable: false,
  isCritical: false,
  isActive: true,
  productId: 'p1',
  productName: 'Pan de elote',
  createdAt: '2026-10-01T12:00:00Z',
  totalOnHand: 5,
  totalReserved: 0,
  totalAvailable: 5,
  isLowStock: true,
};

describe('SupplyModal', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('shows a problem inside the dialog, where the person is looking, without calling the API', async () => {
    const onSaved = vi.fn();
    render(<SupplyModal show supply={null} onHide={vi.fn()} onSaved={onSaved} />);

    await userEvent.click(screen.getByRole('button', { name: 'Crear insumo' }));

    expect(within(screen.getByRole('dialog')).getByRole('alert')).toHaveTextContent('El nombre del insumo es obligatorio.');
    expect(api.createSupply).not.toHaveBeenCalled();
    expect(onSaved).not.toHaveBeenCalled();
  });

  it('keeps the stock of a product in the unit the product is sold in', async () => {
    const onSaved = vi.fn();
    api.updateSupply.mockResolvedValue({ data: productStock } as Awaited<ReturnType<typeof inventoryApi.updateSupply>>);
    render(<SupplyModal show supply={productStock} onHide={vi.fn()} onSaved={onSaved} />);

    const unit = screen.getByLabelText('Unidad');
    expect(unit).toBeDisabled();
    expect(unit).toHaveDisplayValue('Rebanada');
    expect(screen.getByText('Es la unidad del producto.')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    expect(api.updateSupply).toHaveBeenCalledWith('s1', expect.objectContaining({ id: 's1', unit: UnitType.Slice }));
    expect(onSaved).toHaveBeenCalledWith('Insumo actualizado correctamente.');
  });
});
