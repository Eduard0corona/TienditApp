import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { OrderType, UnitType, type OrderDto } from '../../types';
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
