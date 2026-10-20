import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { OrderPaymentMethod } from '../../types';
import ChargePanel, { type ChargeCredit } from './ChargePanel';

function renderPanel(credit?: ChargeCredit, amountDue = 116) {
  const onSubmit = vi.fn();
  render(<ChargePanel amountDue={amountDue} submitting={false} onSubmit={onSubmit} onCancel={vi.fn()} credit={credit} />);
  return onSubmit;
}

describe('ChargePanel on credit (fiado)', () => {
  it('offers no credit to a customer without fiado', () => {
    renderPanel();

    expect(screen.queryByRole('button', { name: /Fiar/ })).not.toBeInTheDocument();
  });

  it('sells all of it on credit when nothing is typed: an empty field is no payment here', async () => {
    const onSubmit = renderPanel({ name: 'Ana López', available: 500 });

    expect(screen.getByText(/tiene fiado: le quedan/)).toHaveTextContent('Ana López tiene fiado: le quedan $500.00.');
    await userEvent.click(screen.getByRole('button', { name: 'Fiar $116.00' }));

    expect(onSubmit).toHaveBeenCalledWith({ payments: [], change: 0, cashReceived: 0, tip: 0, onCredit: true });
  });

  it('tells the cashier what the customer has overdue before selling more on credit', () => {
    renderPanel({ name: 'Ana López', available: 500, overdue: 80 });

    expect(screen.getByText(/tiene fiado: le quedan/)).toHaveTextContent('Ana López tiene fiado: le quedan $500.00. Debe $80.00 vencido.');
    expect(screen.getByRole('button', { name: 'Fiar $116.00' })).toBeEnabled();
  });

  it('takes what the customer leaves now and sells the rest on credit', async () => {
    const onSubmit = renderPanel({ name: 'Ana López', available: 500 });

    await userEvent.click(screen.getByRole('radio', { name: /Tarjeta/ }));
    await userEvent.type(screen.getByLabelText('Monto'), '16');
    await userEvent.click(screen.getByRole('button', { name: 'Fiar $100.00' }));

    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({
      onCredit: true,
      payments: [expect.objectContaining({ method: OrderPaymentMethod.Card, amount: 16 })],
    }));
  });

  it('says how much to charge when the rest does not fit in what they may still owe', async () => {
    const onSubmit = renderPanel({ name: 'Ana López', available: 50 });

    expect(screen.getByRole('button', { name: 'Fiar $116.00' })).toBeDisabled();
    expect(screen.getByText(/cobra al menos/)).toHaveTextContent('Para fiarle el resto, cobra al menos $66.00.');

    // The least to charge is of the whole sale: it stays as the cashier types it.
    await userEvent.type(screen.getByLabelText('Efectivo recibido'), '30');
    expect(screen.getByText(/cobra al menos/)).toHaveTextContent('Para fiarle el resto, cobra al menos $66.00.');
    expect(screen.getByRole('button', { name: 'Fiar $86.00' })).toBeDisabled();

    await userEvent.clear(screen.getByLabelText('Efectivo recibido'));
    await userEvent.type(screen.getByLabelText('Efectivo recibido'), '70');
    await userEvent.click(screen.getByRole('button', { name: 'Fiar $46.00' }));

    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({
      onCredit: true,
      cashReceived: 70,
      payments: [expect.objectContaining({ method: OrderPaymentMethod.Cash, amount: 70, cashReceived: 70 })],
    }));
  });

  it('offers no «Fiar» to who has no fiado left, only says to charge it all', () => {
    renderPanel({ name: 'Roberto', available: 0 });

    expect(screen.getByText(/tiene fiado/)).toHaveTextContent('Roberto tiene fiado: le quedan $0.00. Ya no le queda fiado: cobra la venta completa.');
    expect(screen.queryByRole('button', { name: /Fiar/ })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cobrar $116.00' })).toBeEnabled();
  });

  it('sells on credit what is left after a payment already added', async () => {
    const onSubmit = renderPanel({ name: 'Ana López', available: 500 });

    await userEvent.click(screen.getByRole('radio', { name: /Tarjeta/ }));
    await userEvent.type(screen.getByLabelText('Monto'), '16');
    await userEvent.click(screen.getByRole('button', { name: 'Agregar otro pago' }));
    await userEvent.type(screen.getByLabelText('Efectivo recibido'), '20');
    await userEvent.click(screen.getByRole('button', { name: 'Fiar $80.00' }));

    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({
      onCredit: true,
      payments: [
        expect.objectContaining({ method: OrderPaymentMethod.Card, amount: 16 }),
        expect.objectContaining({ method: OrderPaymentMethod.Cash, amount: 20, cashReceived: 20 }),
      ],
    }));
  });

  it('never sells all of it on credit while the cash or the tip typed is not an amount', async () => {
    renderPanel({ name: 'Ana López', available: 500 });

    await userEvent.type(screen.getByLabelText('Efectivo recibido'), '1OO');
    expect(screen.getByRole('button', { name: 'Fiar $116.00' })).toBeDisabled();

    await userEvent.clear(screen.getByLabelText('Efectivo recibido'));
    await userEvent.click(screen.getByRole('radio', { name: 'Otra' }));
    await userEvent.type(screen.getByLabelText('Monto de la propina'), 'diez');
    expect(screen.getByRole('button', { name: 'Fiar $116.00' })).toBeDisabled();
  });

  it('asks to take the tip away: a tip has no payment to go with', async () => {
    renderPanel({ name: 'Ana López', available: 500 });

    await userEvent.click(screen.getByRole('radio', { name: '10 %' }));

    expect(screen.getByRole('button', { name: 'Fiar $116.00' })).toBeDisabled();
    expect(screen.getByText(/quita la propina/)).toBeInTheDocument();
  });

  it('hides «Fiar» once the cash covers the whole sale', async () => {
    renderPanel({ name: 'Ana López', available: 500 });

    await userEvent.type(screen.getByLabelText('Efectivo recibido'), '200');

    expect(screen.queryByRole('button', { name: /Fiar/ })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cobrar $116.00' })).toBeEnabled();
  });
});
