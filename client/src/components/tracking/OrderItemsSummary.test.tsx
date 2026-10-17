import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import OrderItemsSummary from './OrderItemsSummary';

const haircut = { productId: 'corte', productName: 'Corte de cabello', quantity: 1, amount: 250, notes: null, options: [], optionIds: [] };

describe('OrderItemsSummary', () => {
  it('shows the charge of a home visit, inside the total the server gives', () => {
    render(<OrderItemsSummary items={[haircut]} visitFee={80} pricesIncludeTax tax={45.52} total={330} paidAmount={330} cancelled={false} />);

    expect(screen.getByText('Visita a domicilio').nextElementSibling).toHaveTextContent('$80.00');
    expect(screen.getByText('Total').nextElementSibling).toHaveTextContent('$330.00');
    expect(screen.getByText('Pagado')).toBeInTheDocument();
    expect(screen.queryByText('Envío')).not.toBeInTheDocument();
    expect(screen.queryByText('IVA')).not.toBeInTheDocument();
  });

  it('adds the IVA line when the prices do not include it, and says what is left to pay', () => {
    render(<OrderItemsSummary items={[haircut]} pricesIncludeTax={false} tax={40} total={290} paidAmount={100} cancelled={false} />);

    expect(screen.getByText('IVA').nextElementSibling).toHaveTextContent('$40.00');
    expect(screen.getByText('Por pagar').nextElementSibling).toHaveTextContent('$190.00');
  });
});
