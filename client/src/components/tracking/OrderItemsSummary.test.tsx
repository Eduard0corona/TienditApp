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

  it.each([
    [100.7, 50.4, 50.3],
    [145.8, 45.7, 100.1],
  ])('asks for nothing when what was paid and what the business forgave cover the total (%s = %s + %s)', (total, paid, writtenOff) => {
    render(<OrderItemsSummary items={[haircut]} pricesIncludeTax tax={0} total={total} paidAmount={paid} writtenOffAmount={writtenOff} cancelled={false} />);

    // In floats, 100.70 − 50.40 − 50.30 is not 0: in cents it is.
    expect(screen.queryByText('Por pagar')).not.toBeInTheDocument();
    // Nothing to pay, but not all of it was paid: it does not say «Pagado».
    expect(screen.getByText('Nada por pagar')).toBeInTheDocument();
    expect(screen.queryByText('Pagado')).not.toBeInTheDocument();
  });

  it('says what is left to pay after what the business forgave', () => {
    render(<OrderItemsSummary items={[haircut]} pricesIncludeTax tax={0} total={250} paidAmount={100} writtenOffAmount={50} cancelled={false} />);

    expect(screen.getByText('Por pagar').nextElementSibling).toHaveTextContent('$100.00');
  });
});
