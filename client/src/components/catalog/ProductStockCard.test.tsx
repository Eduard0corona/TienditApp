import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { inventoryApi } from '../../api/inventoryApi';
import ConfirmProvider from '../feedback/ConfirmProvider';
import ToastProvider from '../feedback/ToastProvider';
import { UnitType, type ProductDto, type ProductStockDto } from '../../types';
import ProductStockCard from './ProductStockCard';

vi.mock('../../api/inventoryApi', () => ({
  inventoryApi: {
    getProductStock: vi.fn(),
    getLocations: vi.fn(),
    getSupplies: vi.fn(),
    trackProductStock: vi.fn(),
    stopTrackingProductStock: vi.fn(),
  },
}));

const api = vi.mocked(inventoryApi);

// The card reads only these fields of the product.
function product(overrides: Partial<ProductDto> = {}): ProductDto {
  return { id: 'p1', name: 'Refresco', unit: UnitType.Unit, tracksStock: false, onHand: null, isLowStock: false, ...overrides } as ProductDto;
}

function stock(overrides: Partial<ProductStockDto> = {}): ProductStockDto {
  return { productId: 'p1', tracksStock: false, onHand: 0, reorderPoint: 0, isLowStock: false, locations: [], ...overrides };
}

function renderCard(ui: ReactNode) {
  return render(
    <MemoryRouter>
      <ToastProvider>
        <ConfirmProvider>{ui}</ConfirmProvider>
      </ToastProvider>
    </MemoryRouter>,
  );
}

describe('ProductStockCard', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('shows the stock of the catalog to whoever does not manage inventory, without asking the inventory API', () => {
    renderCard(<ProductStockCard product={product({ tracksStock: true, onHand: 0, isLowStock: true })} canManage={false} />);

    expect(screen.getByText('Agotado')).toBeInTheDocument();
    expect(screen.getByText('Quien maneja Inventario registra entradas y ajustes.')).toBeInTheDocument();
    expect(api.getProductStock).not.toHaveBeenCalled();
  });

  it('warns when few units are left', () => {
    renderCard(<ProductStockCard product={product({ tracksStock: true, onHand: 2, isLowStock: true })} canManage={false} />);

    expect(screen.getByText('2')).toBeInTheDocument();
    expect(screen.getByText('Poca existencia')).toBeInTheDocument();
  });

  it('does not offer stock for a service', () => {
    renderCard(<ProductStockCard product={product({ unit: UnitType.Service })} canManage={false} />);

    expect(screen.getByText('Los servicios no tienen existencias.')).toBeInTheDocument();
  });

  it('starts controlling the stock with an opening balance in the default location', async () => {
    api.getProductStock.mockResolvedValue({ data: stock() } as never);
    api.getLocations.mockResolvedValue({ data: [{ id: 'l2', name: 'Bodega', isDefault: false }, { id: 'l1', name: 'Mostrador', isDefault: true }] } as never);
    api.getSupplies.mockResolvedValue({ data: { items: [] } } as never);
    api.trackProductStock.mockResolvedValue({ data: stock({ tracksStock: true, onHand: 10, reorderPoint: 3 }) } as never);
    const user = userEvent.setup();
    renderCard(<ProductStockCard product={product()} canManage />);

    const initial = await screen.findByLabelText('Existencia inicial');
    // Nothing is suggested: the opening balance is what the person counts.
    expect(initial).toHaveValue('');
    expect(screen.getByLabelText('Avisar cuando queden')).toHaveValue(null);
    await user.type(initial, '10');
    await user.type(screen.getByLabelText('Avisar cuando queden'), '3');
    await user.click(screen.getByRole('button', { name: 'Controlar existencias' }));

    expect(api.trackProductStock).toHaveBeenCalledWith('p1', {
      initialQuantity: 10,
      inventoryLocationId: 'l1',
      unitCost: null,
      reorderPoint: 3,
    });
    expect(await screen.findByText('Punto de reorden: 3')).toBeInTheDocument();
  });

  it('refuses a fraction of a piece before calling the API', async () => {
    api.getProductStock.mockResolvedValue({ data: stock() } as never);
    api.getLocations.mockResolvedValue({ data: [{ id: 'l1', name: 'Mostrador', isDefault: true }] } as never);
    api.getSupplies.mockResolvedValue({ data: { items: [] } } as never);
    const user = userEvent.setup();
    renderCard(<ProductStockCard product={product()} canManage />);

    await user.type(await screen.findByLabelText('Existencia inicial'), '1.5');
    await user.click(screen.getByRole('button', { name: 'Controlar existencias' }));

    expect(await screen.findByText('La existencia es en piezas enteras.')).toBeInTheDocument();
    expect(api.trackProductStock).not.toHaveBeenCalled();
  });
});
