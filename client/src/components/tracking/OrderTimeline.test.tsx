import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { OrderTrackingStep, OrderTrackingStepState } from '../../types';
import OrderTimeline, { OrderStepChip } from './OrderTimeline';

const steps = [
  { step: OrderTrackingStep.Received, label: 'Recibido', state: OrderTrackingStepState.Done, at: '2026-10-12T19:00:00Z' },
  { step: OrderTrackingStep.Confirmed, label: 'Confirmado', state: OrderTrackingStepState.Done, at: null },
  { step: OrderTrackingStep.Preparing, label: 'En preparación', state: OrderTrackingStepState.Current, at: '2026-10-12T19:05:00Z' },
  { step: OrderTrackingStep.OnTheWay, label: 'En camino', state: OrderTrackingStepState.Upcoming, at: null },
  { step: OrderTrackingStep.Delivered, label: 'Entregado', state: OrderTrackingStepState.Upcoming, at: null },
];

describe('OrderTimeline', () => {
  it('lists the steps in order, says which are done, where it is now and what is to come', () => {
    render(<OrderTimeline steps={steps} />);

    const items = within(screen.getByRole('list', { name: 'Seguimiento del pedido' })).getAllByRole('listitem');
    expect(items.map((item) => item.textContent?.replace(/\d{1,2}:\d{2}.*$/, '').trim())).toEqual([
      'Recibido, hecho',
      'Confirmado, hecho',
      'En preparación, ahora',
      'En camino, pendiente',
      'Entregado, pendiente',
    ]);
    expect(items[2]).toHaveAttribute('aria-current', 'step');
    expect(items.filter((item) => item.hasAttribute('aria-current'))).toHaveLength(1);
    // The time it reached each step; a step the business skipped has none.
    expect(items[0].querySelector('time')).toHaveAttribute('dateTime', '2026-10-12T19:00:00Z');
    expect(items[1].querySelector('time')).toBeNull();
    expect(items[3].querySelector('time')).toBeNull();
  });

  it('shows a step of another day with its date', () => {
    render(<OrderTimeline steps={[
      { step: OrderTrackingStep.Received, label: 'Recibido', state: OrderTrackingStepState.Done, at: '2026-10-12T19:00:00Z' },
      { step: OrderTrackingStep.Delivered, label: 'Terminado', state: OrderTrackingStepState.Current, at: '2026-10-15T19:00:00Z' },
    ]} />);

    const [received, finished] = screen.getAllByRole('listitem');
    expect(received.querySelector('time')?.textContent).not.toMatch(/\//);
    expect(finished.querySelector('time')?.textContent).toMatch(/15\/10\/26/);
  });

  it('colors the step like the state of an order, with its words', () => {
    const { rerender } = render(<OrderStepChip step={OrderTrackingStep.Cancelled} label="Cancelado" />);
    expect(screen.getByText('Cancelado')).toHaveClass('ta-chip', 'ta-chip-cancelled');

    rerender(<OrderStepChip step={OrderTrackingStep.OnTheWay} label="En camino" />);
    expect(screen.getByText('En camino')).toHaveClass('ta-chip', 'ta-chip-ready');
  });
});
