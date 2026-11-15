import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import ChargePanel from './ChargePanel';

describe('ChargePanel tip', () => {
  it('takes the percentages of what is owed now', async () => {
    const onSubmit = vi.fn();
    render(<ChargePanel amountDue={280} submitting={false} onSubmit={onSubmit} onCancel={vi.fn()} />);

    await userEvent.click(screen.getByRole('radio', { name: '10 %' }));
    await userEvent.click(screen.getByRole('button', { name: /^Cobrar/ }));

    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ tip: 28 }));
  });

  it('takes them of the whole sale when a deposit paid online was taken off it (Fase 31O)', async () => {
    const onSubmit = vi.fn();
    render(<ChargePanel amountDue={280} tipBase={400} submitting={false} onSubmit={onSubmit} onCancel={vi.fn()} />);

    await userEvent.click(screen.getByRole('radio', { name: '10 %' }));
    await userEvent.click(screen.getByRole('button', { name: /^Cobrar/ }));

    // The cashier charges the rest, and the tip of 10 % of the $400 of the service.
    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ tip: 40 }));
  });

  it('lets that tip be more than the rest, never more than the whole sale', async () => {
    const onSubmit = vi.fn();
    render(<ChargePanel amountDue={40} tipBase={400} submitting={false} onSubmit={onSubmit} onCancel={vi.fn()} />);

    // A deposit of 90 %: the 15 % of the service is more than the $40 left to charge, and still fits in the sale.
    await userEvent.click(screen.getByRole('radio', { name: '15 %' }));
    await userEvent.click(screen.getByRole('button', { name: /^Cobrar/ }));
    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ tip: 60 }));

    await userEvent.click(screen.getByRole('radio', { name: 'Otra' }));
    await userEvent.type(screen.getByRole('textbox', { name: 'Monto de la propina' }), '400');
    expect(screen.getByRole('button', { name: /^Cobrar/ })).toBeEnabled();
    await userEvent.type(screen.getByRole('textbox', { name: 'Monto de la propina' }), '.01');
    expect(screen.getByRole('button', { name: /^Cobrar/ })).toBeDisabled();
  });
});
