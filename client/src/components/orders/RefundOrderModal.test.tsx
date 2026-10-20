import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import {
  BusinessType,
  OrderChannel,
  OrderPaymentMethod,
  OrderType,
  PlatformCollectedBy,
  UnitType,
  type OrderDto,
} from '../../types';
import RefundOrderModal from './RefundOrderModal';

const sale: OrderDto = {
  id: 'o1',
  createdByBusiness: true,
  orderNumber: 'ORD-000123',
  businessType: BusinessType.Food,
  orderType: OrderType.Retail,
  status: 6 as OrderDto['status'],
  canCancel: false,
  allowedTransitions: [],
  customerName: 'Público en general',
  pricesIncludeTax: true,
  subTotal: 301.72,
  tax: 48.28,
  total: 350,
  taxBreakdown: [],
  channel: OrderChannel.Counter,
  paidAmount: 350,
  netPaidAmount: 350,
  refundedAmount: 0,
  balance: 0,
  createdAt: '2026-10-10T18:00:00Z',
  items: [{
    id: 'i1', productId: 'p1', productName: 'Paquete familiar', quantity: 1, unitPrice: 350, totalPrice: 350,
    taxRate: 16, isTaxExempt: false, unit: UnitType.Unit,
  }],
  statusHistory: [],
  servicePieces: [],
  payments: [{ id: 'p1', amount: 350, method: OrderPaymentMethod.Cash, paidAt: '2026-10-10T18:00:00Z' }],
  refunds: [],
};

/** An order Rappi charged in its app: its money is a «Plataforma» payment. */
const rappi: OrderDto = {
  ...sale,
  channel: OrderChannel.Rappi,
  platformOrder: { externalOrderNumber: 'BC953', collectedBy: PlatformCollectedBy.Platform },
  payments: [{ id: 'p1', amount: 350, method: OrderPaymentMethod.Platform, paidAt: '2026-10-10T18:00:00Z', reference: 'BC953' }],
};

/** An order of DiDi Food the courier paid in cash: its money is in the drawer. */
const didiPaidInCash: OrderDto = {
  ...sale,
  channel: OrderChannel.DidiFood,
  platformOrder: { externalOrderNumber: '12', collectedBy: PlatformCollectedBy.Business },
};

/** An order of the online menu paid in Mercado Pago (Fase 24I): its money is an «En línea» payment. */
const paidOnline: OrderDto = {
  ...sale,
  channel: OrderChannel.Online,
  payments: [{ id: 'p1', amount: 350, method: OrderPaymentMethod.Online, paidAt: '2026-10-10T18:00:00Z', reference: '987654321' }],
};

describe('RefundOrderModal', () => {
  it('gives back an order paid online only through Mercado Pago, never out of the drawer', async () => {
    const onSubmit = vi.fn();
    render(<RefundOrderModal mode="refund" order={paidOnline} submitting={false} error="" onClose={vi.fn()} onSubmit={onSubmit} />);

    const method = screen.getByLabelText('Se devuelve en');
    expect(method).toHaveValue('En línea');
    expect(method).toHaveAttribute('readonly');
    expect(screen.getByText('Mercado Pago se lo devuelve al cliente a su tarjeta o cuenta; no sale de tu caja.')).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText('Monto a devolver'), '100');
    await userEvent.type(screen.getByLabelText('Motivo'), 'Faltó un refresco');
    await userEvent.click(screen.getByRole('button', { name: 'Reembolsar $100.00' }));

    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ amount: 100, method: OrderPaymentMethod.Online }));
  });

  it('gives back to the balance in the customer\'s favor only an order with their phone (Fase 26D)', async () => {
    const options = () => within(screen.getByLabelText('Se devuelve en')).getAllByRole('option').map((option) => option.textContent);
    const { unmount } = render(<RefundOrderModal mode="refund" order={sale} submitting={false} error="" onClose={vi.fn()} onSubmit={vi.fn()} />);
    expect(options()).not.toContain('Saldo a favor');
    unmount();

    const onSubmit = vi.fn();
    render(<RefundOrderModal mode="refund" order={{ ...sale, customerName: 'Ana López', customerPhone: '+525512345678' }}
      submitting={false} error="" onClose={vi.fn()} onSubmit={onSubmit} />);
    await userEvent.selectOptions(screen.getByLabelText('Se devuelve en'), 'Saldo a favor');
    expect(screen.getByText('Queda a favor del cliente para su próxima compra; no sale de tu caja.')).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Monto a devolver'), '50');
    await userEvent.type(screen.getByLabelText('Motivo'), 'No le gustó');
    await userEvent.click(screen.getByRole('button', { name: 'Reembolsar $50.00' }));

    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ amount: 50, method: OrderPaymentMethod.CustomerBalance }));
  });

  it('never offers «En línea» for an order not paid online', () => {
    render(<RefundOrderModal mode="refund" order={sale} submitting={false} error="" onClose={vi.fn()} onSubmit={vi.fn()} />);

    expect(within(screen.getByLabelText('Se devuelve en')).getAllByRole('option').map((option) => option.textContent)).not.toContain('En línea');
  });

  it('gives back what the platform charged only as «Plataforma», outside the drawer', async () => {
    const onSubmit = vi.fn();
    render(<RefundOrderModal mode="refund" order={rappi} submitting={false} error="" onClose={vi.fn()} onSubmit={onSubmit} />);

    const method = screen.getByLabelText('Se devuelve en');
    expect(method).toHaveValue('Plataforma');
    expect(method).toHaveAttribute('readonly');
    expect(screen.getByText('La devolución queda registrada para cuadrarla con la liquidación de Rappi; no sale de la caja.')).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText('Monto a devolver'), '100');
    await userEvent.type(screen.getByLabelText('Motivo'), 'Faltó un refresco');
    await userEvent.click(screen.getByRole('button', { name: 'Reembolsar $100.00' }));

    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ amount: 100, method: OrderPaymentMethod.Platform }));
  });

  it('never offers «Plataforma» for what the business charged, also in an order of a platform', () => {
    const { unmount } = render(<RefundOrderModal mode="refund" order={sale} submitting={false} error="" onClose={vi.fn()} onSubmit={vi.fn()} />);
    const offered = () => within(screen.getByLabelText('Se devuelve en')).getAllByRole('option').map((option) => option.textContent);
    expect(offered()).not.toContain('Plataforma');
    unmount();

    render(<RefundOrderModal mode="refund" order={didiPaidInCash} submitting={false} error="" onClose={vi.fn()} onSubmit={vi.fn()} />);
    expect(offered()).not.toContain('Plataforma');
    expect(screen.getByLabelText('Se devuelve en')).toHaveValue(String(OrderPaymentMethod.Cash));
  });

  it('voids an order of a platform with its reasons at hand and the supplies kept out unless the food was not made', async () => {
    const onSubmit = vi.fn();
    render(<RefundOrderModal mode="void" order={rappi} submitting={false} error="" onClose={vi.fn()} onSubmit={onSubmit} />);

    expect(screen.getByText('Anula el pedido si Rappi lo canceló o si lo registraste dos veces. Lo que cobró Rappi no sale de tu caja.'))
      .toBeInTheDocument();
    const restock = screen.getByRole('switch', { name: 'Regresar los insumos al inventario' });
    expect(restock).not.toBeChecked();
    expect(restock).toHaveAccessibleDescription('Márcalo solo si la comida no se preparó.');

    // A reason fills the field and stays editable.
    expect(screen.getAllByRole('button', { name: /lo canceló|dos veces|repartidor/ }).map((button) => button.textContent))
      .toEqual(['Rappi lo canceló', 'Lo registré dos veces', 'No llegó el repartidor']);
    await userEvent.click(screen.getByRole('button', { name: 'Rappi lo canceló' }));
    const reason = screen.getByLabelText('Motivo');
    expect(reason).toHaveValue('Rappi lo canceló');
    await userEvent.type(reason, ' por falta de repartidor');
    await userEvent.click(screen.getByRole('button', { name: 'Anular venta' }));

    expect(onSubmit).toHaveBeenCalledWith({ reason: 'Rappi lo canceló por falta de repartidor', restockItems: false });
  });

  it('does not say the drawer is untouched when the courier paid the business', () => {
    render(<RefundOrderModal mode="void" order={didiPaidInCash} submitting={false} error="" onClose={vi.fn()} onSubmit={vi.fn()} />);

    expect(screen.getByText('Anula el pedido si DiDi Food lo canceló o si lo registraste dos veces.')).toBeInTheDocument();
    expect(screen.queryByText(/no sale de tu caja/)).not.toBeInTheDocument();
    expect(screen.getByText('Efectivo: $350.00')).toBeInTheDocument();
  });

  it('voids a counter sale as always, with the supplies back to inventory', () => {
    render(<RefundOrderModal mode="void" order={sale} submitting={false} error="" onClose={vi.fn()} onSubmit={vi.fn()} />);

    expect(screen.getByRole('switch', { name: 'Regresar los insumos al inventario' })).toBeChecked();
    expect(screen.queryByRole('button', { name: 'Lo registré dos veces' })).not.toBeInTheDocument();
    expect(screen.queryByText(/Anula el pedido si/)).not.toBeInTheDocument();
  });
});
