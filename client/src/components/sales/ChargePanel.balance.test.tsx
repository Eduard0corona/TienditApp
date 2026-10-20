import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { OrderPaymentMethod } from '../../types';
import ChargePanel, { type ChargeBalance } from './ChargePanel';

function renderPanel(balance?: ChargeBalance, amountDue = 116) {
  const onSubmit = vi.fn();
  render(<ChargePanel amountDue={amountDue} submitting={false} onSubmit={onSubmit} onCancel={vi.fn()} balance={balance} />);
  return onSubmit;
}

describe('ChargePanel with the balance in the customer\'s favor (Fase 26D)', () => {
  it('offers no «Saldo a favor» to a customer without one', () => {
    renderPanel({ name: 'Ana López', available: 0 });

    expect(screen.queryByRole('radio', { name: /Saldo a favor/ })).not.toBeInTheDocument();
    expect(screen.queryByText(/a favor\./)).not.toBeInTheDocument();
  });

  it('pays with all she has and the rest another way', async () => {
    const onSubmit = renderPanel({ name: 'Ana López', available: 50 });

    expect(screen.getByText(/tiene.*a favor\./)).toHaveTextContent('Ana López tiene $50.00 a favor.');
    await userEvent.click(screen.getByRole('radio', { name: /Saldo a favor/ }));
    // Empty takes what she has, never more than what is owed; there is no reference to write.
    expect(screen.getByLabelText('Monto')).toHaveAttribute('placeholder', '50.00');
    expect(screen.queryByLabelText(/Referencia/)).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Agregar otro pago' }));
    expect(screen.getByText(/tiene.*a favor\./)).toHaveTextContent('Ana López tiene $0.00 a favor.');
    await userEvent.click(screen.getByRole('button', { name: 'Cobrar $116.00' }));

    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({
      payments: [
        expect.objectContaining({ method: OrderPaymentMethod.CustomerBalance, amount: 50 }),
        // After it, the rest goes in cash unless the cashier picks another way.
        expect.objectContaining({ method: OrderPaymentMethod.Cash, amount: 66 }),
      ],
    }));
  });

  it('refuses more than she has, and a tip paid with it', async () => {
    const onSubmit = renderPanel({ name: 'Ana López', available: 200 });

    await userEvent.click(screen.getByRole('radio', { name: /Saldo a favor/ }));
    await userEvent.type(screen.getByLabelText('Monto'), '120');
    expect(screen.getByText('No puede ser mayor a lo que falta.')).toBeInTheDocument();
    await userEvent.clear(screen.getByLabelText('Monto'));
    await userEvent.click(screen.getByRole('radio', { name: '10 %' }));

    expect(screen.getByText(/tiene.*a favor\./)).toHaveTextContent('La propina no se paga con saldo a favor.');
    expect(screen.getByRole('button', { name: /^Cobrar/ })).toBeDisabled();
    expect(onSubmit).not.toHaveBeenCalled();
  });
});
