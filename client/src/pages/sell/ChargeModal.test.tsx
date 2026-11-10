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
});
