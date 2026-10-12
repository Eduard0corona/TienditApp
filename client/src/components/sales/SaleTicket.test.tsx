import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { OrderChannel, OrderPaymentMethod, OrderType, PlatformCollectedBy, UnitType, type OrderDto } from '../../types';
import SaleTicket from './SaleTicket';

const order: OrderDto = {
  id: 'o1',
  createdByBusiness: true,
  orderNumber: 'ORD-000007',
  businessType: 1 as OrderDto['businessType'],
  orderType: OrderType.Retail,
  status: 2 as OrderDto['status'],
  canCancel: false,
  allowedTransitions: [],
  customerName: 'Público en general',
  pricesIncludeTax: true,
  subTotal: 64.66,
  tax: 10.34,
  total: 75,
  taxBreakdown: [],
  paidAmount: 75,
  balance: 0,
  createdAt: '2026-10-08T20:00:00Z',
  items: [{
    id: 'i1',
    productId: 'p1',
    productName: 'Tacos',
    quantity: 3,
    unitPrice: 25,
    totalPrice: 75,
    taxRate: 16,
    isTaxExempt: false,
    unit: UnitType.Unit,
  }],
  statusHistory: [],
  servicePieces: [],
  payments: [],
};

describe('SaleTicket', () => {
  it('prints the logo of the business above its name', () => {
    const { container } = render(<SaleTicket order={order} businessName="Fonda Lupita" logoUrl="https://cdn/lupita_sm.webp" />);

    const ticket = screen.getByLabelText('Ticket ORD-000007');
    expect(container.querySelector('.ta-ticket-logo')).toHaveAttribute('src', 'https://cdn/lupita_sm.webp');
    // Its name is right below: the logo needs no alternative text.
    expect(container.querySelector('.ta-ticket-logo')).toHaveAttribute('alt', '');
    expect(ticket).toHaveTextContent('Fonda Lupita');
  });

  it('leaves the logo out when it does not load, as on a sale charged without connection', () => {
    const { container } = render(<SaleTicket order={order} businessName="Fonda Lupita" logoUrl="https://cdn/lupita_sm.webp" provisional />);

    fireEvent.error(container.querySelector('.ta-ticket-logo')!);

    expect(container.querySelector('.ta-ticket-logo')).toBeNull();
    expect(screen.getByText('Fonda Lupita')).toBeInTheDocument();
  });

  it('prints only the name while the business has no logo', () => {
    const { container } = render(<SaleTicket order={order} businessName="Fonda Lupita" />);

    expect(container.querySelector('.ta-ticket-logo')).toBeNull();
    expect(screen.getByText('Fonda Lupita')).toHaveClass('ta-ticket-business');
  });
});

describe('SaleTicket of an order of a delivery platform', () => {
  const rappi: OrderDto = {
    ...order,
    channel: OrderChannel.Rappi,
    customerName: 'Juan',
    platformOrder: { externalOrderNumber: 'BC953', collectedBy: PlatformCollectedBy.Platform },
    items: [{ ...order.items[0], notes: 'Sin cebolla', options: [{ optionId: 'o1', groupName: 'Salsa', optionName: 'Verde', priceDelta: 0 }] }],
    payments: [{ id: 'p1', amount: 75, method: OrderPaymentMethod.Platform, paidAt: '2026-10-08T20:00:00Z', reference: 'BC953' }],
  };

  it('packs the bag of an order the platform charged: its folio and the products, without prices or totals', () => {
    render(<SaleTicket order={rappi} businessName="Fonda Lupita" />);

    const ticket = screen.getByLabelText('Ticket ORD-000007');
    expect(screen.getByText('Rappi · Folio BC953')).toHaveClass('fw-bold');
    expect(ticket).toHaveTextContent('Bolsa: Juan');
    expect(ticket).not.toHaveTextContent('Cliente:');
    expect(ticket).toHaveTextContent('3 ×Tacos');
    expect(ticket).toHaveTextContent('Verde');
    expect(ticket).toHaveTextContent('Sin cebolla');
    expect(ticket).toHaveTextContent('Pagado en la app de Rappi');
    // Until each platform has its own prices, the bag never contradicts what the app charged.
    expect(ticket).not.toHaveTextContent('$');
    expect(ticket).not.toHaveTextContent('Total');
    expect(ticket).not.toHaveTextContent('Plataforma');
    // Nor what the platform keeps: the commission, withholdings and net are for Reportes only.
    expect(ticket).not.toHaveTextContent(/comisi[oó]n|retenci|neto/i);
  });

  it('charges the courier what is left of an order the business collects, with the folio on top', () => {
    const didi: OrderDto = {
      ...rappi,
      channel: OrderChannel.DidiFood,
      customerName: 'Público en general',
      platformOrder: { externalOrderNumber: '12', collectedBy: PlatformCollectedBy.Business },
      payments: [],
      paidAmount: 0,
      balance: 75,
    };
    render(<SaleTicket order={didi} businessName="Fonda Lupita" />);

    const ticket = screen.getByLabelText('Ticket ORD-000007');
    expect(ticket).toHaveTextContent('DiDi Food · Folio 12');
    expect(ticket).not.toHaveTextContent('Bolsa:');
    expect(ticket).toHaveTextContent('Total$75.00');
    expect(ticket).toHaveTextContent('Cobrar al repartidor$75.00');
    expect(ticket).not.toHaveTextContent('Saldo pendiente');
    expect(ticket).not.toHaveTextContent(/comisi[oó]n|retenci|neto/i);
  });
});
