import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { useVisitRoute } from '../../api/homeVisits';
import { AppointmentStatus } from '../../types';
import VisitRoute from './VisitRoute';

vi.mock('../../api/homeVisits', () => ({ useVisitRoute: vi.fn() }));

const stop = (overrides: Record<string, unknown>) => ({
  appointmentId: 'a1',
  customerName: 'Ana Ruiz',
  customerPhone: '55 1234 5678',
  serviceName: 'Corte',
  startAt: '2026-10-14T16:00:00Z',
  endAt: '2026-10-14T17:00:00Z',
  status: AppointmentStatus.Confirmed,
  postalCode: '72000',
  addressLine: 'Av. Juárez 123',
  references: 'Portón negro',
  zoneName: 'Centro',
  fee: 80,
  saleOrderId: null,
  ...overrides,
});

function giveRoute(staff: unknown[]) {
  vi.mocked(useVisitRoute).mockReturnValue({ isPending: false, isError: false, isSuccess: true, data: { date: '2026-10-14', staff } } as never);
}

describe('VisitRoute', () => {
  it('lists the visits of each person in order, with what they charge, the maps and the call', async () => {
    giveRoute([{
      staffUserId: 'u1',
      staffName: 'Luis Paz',
      feeTotal: 160,
      stops: [stop({}), stop({ appointmentId: 'a2', customerName: 'Mara', postalCode: '72005', addressLine: '5 Sur 300', references: null, saleOrderId: 'o1' })],
    }]);
    const onCharge = vi.fn();
    render(<VisitRoute staff={[]} pricesIncludeTax canCharge onCharge={onCharge} />);

    const route = within(screen.getByRole('region', { name: 'Ruta de Luis Paz' }));
    expect(route.getByText(/2 visitas/)).toHaveTextContent('cargos por visita $160.00');
    expect(route.getByText('CP 72000 · Centro · Cargo por visita $80.00')).toBeInTheDocument();
    expect(route.getByText('Ref.: Portón negro')).toBeInTheDocument();
    expect(route.getByRole('link', { name: 'Llamar a Ana Ruiz' })).toHaveAttribute('href', 'tel:5512345678');
    expect(route.getByRole('link', { name: 'Abrir en Maps la visita de Mara' }).getAttribute('href')).toContain('5%20Sur%20300');
    const mapsRoute = new URL(route.getByRole('link', { name: 'Ruta en Maps de Luis Paz' }).getAttribute('href')!);
    expect(mapsRoute.searchParams.get('destination')).toBe('5 Sur 300, CP 72005, México');

    // Only the visit not charged yet can be charged.
    await userEvent.click(route.getByRole('button', { name: 'Cobrar la cita de Ana Ruiz' }));
    expect(onCharge).toHaveBeenCalledWith('a1');
    expect(route.queryByRole('button', { name: 'Cobrar la cita de Mara' })).not.toBeInTheDocument();
  });

  it('says when there are no visits, and offers no charge to whoever does not sell', () => {
    giveRoute([]);
    const { rerender } = render(<VisitRoute staff={[]} pricesIncludeTax canCharge={false} onCharge={vi.fn()} />);
    expect(screen.getByText('No hay visitas a domicilio ese día.')).toBeInTheDocument();

    giveRoute([{ staffUserId: null, staffName: null, feeTotal: 0, stops: [stop({ fee: 0 })] }]);
    rerender(<VisitRoute staff={[]} pricesIncludeTax={false} canCharge={false} onCharge={vi.fn()} />);
    const route = within(screen.getByRole('region', { name: 'Ruta de Sin asignar' }));
    expect(route.getByText('CP 72000 · Centro · Visita sin cargo')).toBeInTheDocument();
    expect(route.queryByRole('button', { name: /Cobrar/ })).not.toBeInTheDocument();
  });
});
