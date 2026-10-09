import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { KitchenTicketStatus, UnitType, type OrderItemDto } from '../../types';
import CancelTabItemForm from './CancelTabItemForm';

const tacos: OrderItemDto = {
  id: 'i1',
  productId: 'p1',
  productName: 'Tacos al pastor',
  quantity: 3,
  cancelledQuantity: 1,
  unitPrice: 25,
  totalPrice: 75,
  chargedPrice: 50,
  taxRate: 16,
  isTaxExempt: false,
  unit: UnitType.Unit,
  round: 2,
};

describe('CancelTabItemForm', () => {
  it('asks why, and sends one dish back to stock while the kitchen has not started it', async () => {
    const onSubmit = vi.fn();
    render(<CancelTabItemForm item={tacos} roundStatus={KitchenTicketStatus.Pending} submitting={false} onSubmit={onSubmit} onBack={vi.fn()} />);

    // Without a reason nothing is sent.
    expect(screen.getByRole('button', { name: 'Cancelar platillo' })).toBeDisabled();
    expect(screen.getByText(/2 en la cuenta/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'El cliente ya no lo quiso' }));
    await userEvent.click(screen.getByRole('button', { name: 'Cancelar platillo' }));

    expect(onSubmit).toHaveBeenCalledWith({ quantity: 1, reason: 'El cliente ya no lo quiso', returnToStock: true });
  });

  it('treats what the kitchen already prepared as lost, and refuses more than the tab still charges', async () => {
    const onSubmit = vi.fn();
    render(<CancelTabItemForm item={tacos} roundStatus={KitchenTicketStatus.Ready} submitting={false} onSubmit={onSubmit} onBack={vi.fn()} />);
    expect(screen.getByRole('switch', { name: 'Regresar sus insumos al inventario' })).not.toBeChecked();

    const quantity = screen.getByLabelText('Cantidad a cancelar');
    await userEvent.clear(quantity);
    await userEvent.type(quantity, '3');
    await userEvent.type(screen.getByLabelText('Motivo'), 'Se quemaron');

    expect(screen.getByText('Escribe una cantidad de hasta 2.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancelar platillo' })).toBeDisabled();

    await userEvent.clear(quantity);
    await userEvent.type(quantity, '2');
    await userEvent.click(screen.getByRole('button', { name: 'Cancelar platillo' }));

    expect(onSubmit).toHaveBeenCalledWith({ quantity: 2, reason: 'Se quemaron', returnToStock: false });
  });

  it('takes the weight of a weighed dish, all of it by default', async () => {
    const onSubmit = vi.fn();
    const steak: OrderItemDto = { ...tacos, productName: 'Arrachera', quantity: 1.255, cancelledQuantity: 0, unit: UnitType.Kg };
    render(<CancelTabItemForm item={steak} submitting={false} onSubmit={onSubmit} onBack={vi.fn()} />);

    expect(screen.getByLabelText('Cantidad a cancelar')).toHaveValue('1.255');
    await userEvent.type(screen.getByLabelText('Motivo'), 'Se pasó de cocción');
    await userEvent.click(screen.getByRole('button', { name: 'Cancelar platillo' }));

    expect(onSubmit).toHaveBeenCalledWith({ quantity: 1.255, reason: 'Se pasó de cocción', returnToStock: true });
  });
});
