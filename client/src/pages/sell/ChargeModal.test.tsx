import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import ChargeModal from './ChargeModal';

// What is charged is ChargePanel's: here, only the dialog around it.
vi.mock('../../components/sales/ChargePanel', () => ({
  default: ({ amountDue, allowTip }: { amountDue: number; allowTip: boolean }) => <p>Por cobrar {amountDue}{allowTip ? ' con propina' : ''}</p>,
}));

describe('ChargeModal', () => {
  it('shows the charge in a dialog named «Cobrar», closed with Escape', async () => {
    const onClose = vi.fn();
    render(<ChargeModal show amountDue={50} submitting={false} error="" allowTip onSubmit={vi.fn()} onClose={onClose} />);

    expect(screen.getByRole('dialog', { name: 'Cobrar' })).toHaveTextContent('Por cobrar 50 con propina');
    await userEvent.keyboard('{Escape}');
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('does not close while the charge is on its way', async () => {
    const onClose = vi.fn();
    render(<ChargeModal show amountDue={50} submitting error="" allowTip={false} onSubmit={vi.fn()} onClose={onClose} />);

    expect(screen.queryByRole('button', { name: /cerrar/i })).not.toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    expect(onClose).not.toHaveBeenCalled();
    expect(screen.getByRole('dialog', { name: 'Cobrar' })).toHaveTextContent('Por cobrar 50');
  });

  it('charges the rest of a deposit paid online, with the tip (Fase 31O)', () => {
    render(<ChargeModal show amountDue={280} paidOnline={120} total={400} submitting={false} error="" allowTip onSubmit={vi.fn()} onClose={vi.fn()} />);

    expect(screen.getByRole('note')).toHaveTextContent('El cliente ya pagó en línea un anticipo de $120.00: entra a la venta como pago «En línea» y aquí cobras el resto.');
    expect(screen.getByRole('dialog', { name: 'Cobrar' })).toHaveTextContent('Por cobrar 280 con propina');
  });

  it('offers no tip when the deposit covers the whole sale: there is no payment for it to go with', () => {
    render(<ChargeModal show amountDue={0} paidOnline={400} total={400} submitting={false} error="" allowTip onSubmit={vi.fn()} onClose={vi.fn()} />);

    expect(screen.getByRole('note')).toHaveTextContent('anticipo de $400.00, que cubre toda la venta: entra como pago «En línea», no queda nada por cobrar y en este cobro no hay propina.');
    expect(screen.getByRole('dialog', { name: 'Cobrar' })).toHaveTextContent('Por cobrar 0');
    expect(screen.getByRole('dialog', { name: 'Cobrar' })).not.toHaveTextContent('con propina');
  });
});
