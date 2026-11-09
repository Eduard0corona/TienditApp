import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { reportsApi } from '../../api/reportsApi';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { TenantCapability, type OperationalReportDto } from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import OperationalReports from './OperationalReports';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
// Each card reads its own report and has its own tests.
vi.mock('../../components/reports/SalesByBranchCard', () => ({ default: () => <section>Ventas por sucursal</section> }));
vi.mock('../../components/reports/SalesByChannelCard', () => ({ default: () => <section>Ventas por canal</section> }));
vi.mock('../../api/reportsApi', () => ({ reportsApi: { getOperationalSummary: vi.fn() } }));
vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));

/**
 * October of a café: $11,600.00 charged with IVA, $1,600.00 of it IVA. The gross profit is the server's, not the
 * difference on screen: a figure that is not $10,000.00 − $3,850.00 shows it is read, not computed.
 */
const october: OperationalReportDto = {
  rangeFrom: '2026-10-01T06:00:00Z', rangeTo: '2026-11-01T05:59:59Z',
  totalRevenue: 11600, totalTax: 1600, netRevenue: 10000, totalOrderPayments: 11250, totalPurchases: 4200.5,
  totalCogs: 3850, grossProfit: 6149.25, ordersCompleted: 312, appointmentsScheduled: 18, appointmentsCompleted: 15,
  // The API names each method by its enum name.
  revenueByPaymentMethod: { Cash: 7250, Card: 3600.75, Online: 399.25 },
};

function renderReports({ permissions = ['ViewReports'] }: { permissions?: string[] } = {}) {
  vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role: 'Employee', permissions } } as unknown as ReturnType<typeof useAuth>);
  return render(<MemoryRouter><OperationalReports /></MemoryRouter>);
}

const card = (title: string) => screen.getByText(title).parentElement as HTMLElement;

describe('OperationalReports', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useTenant).mockReturnValue({ capabilities: TenantCapability.Food } as unknown as ReturnType<typeof useTenant>);
    vi.mocked(reportsApi.getOperationalSummary).mockResolvedValue({ data: october } as never);
  });

  afterEach(() => setBusinessTimeZone(null));

  it('shows the figures the server gives, the operation and what came in by each payment method', async () => {
    renderReports();

    expect(await screen.findByText('Ventas netas (sin IVA)')).toBeInTheDocument();
    expect(reportsApi.getOperationalSummary).toHaveBeenCalledWith({});
    expect(card('Ventas netas (sin IVA)')).toHaveTextContent('$10,000.00Con IVA: $11,600.00 · IVA: $1,600.00');
    expect(card('Costo de ventas')).toHaveTextContent('$3,850.00');
    expect(card('Utilidad bruta')).toHaveTextContent('$6,149.25');
    expect(card('Compras')).toHaveTextContent('$4,200.50');
    expect(screen.getByText('Órdenes completadas:').parentElement).toHaveTextContent('Órdenes completadas: 312');
    expect(screen.getByText('Citas programadas:').parentElement).toHaveTextContent('Citas programadas: 18');
    expect(screen.getByText('Citas completadas:').parentElement).toHaveTextContent('Citas completadas: 15');

    const payments = within(screen.getByRole('table')).getAllByRole('row').slice(1).map((row) => row.textContent);
    expect(payments).toEqual(['Efectivo$7,250.00', 'Tarjeta$3,600.75', 'En línea$399.25']);
    expect(screen.getByText('Ventas por sucursal')).toBeInTheDocument();
    expect(screen.getByText('Ventas por canal')).toBeInTheDocument();
  });

  it('asks for the days of the business that are chosen, and reads them again on «Actualizar»', async () => {
    const user = userEvent.setup();
    // Not the zone of the team's machines: a range read in the device's zone fails here too.
    setBusinessTimeZone('America/Cancun');
    renderReports();
    await screen.findByText('Ventas netas (sin IVA)');

    await user.type(screen.getByLabelText('Desde'), '2026-10-01');
    await user.type(screen.getByLabelText('Hasta'), '2026-10-31');
    // Cancún is UTC-5 all year: its day starts at 05:00 UTC and ends at 04:59:59 UTC of the next.
    await waitFor(() => expect(reportsApi.getOperationalSummary).toHaveBeenLastCalledWith({
      fromDate: '2026-10-01T05:00:00.000Z', toDate: '2026-11-01T04:59:59.000Z',
    }));

    const calls = vi.mocked(reportsApi.getOperationalSummary).mock.calls.length;
    await user.click(screen.getByRole('button', { name: 'Actualizar' }));
    await waitFor(() => expect(reportsApi.getOperationalSummary).toHaveBeenCalledTimes(calls + 1));
    expect(reportsApi.getOperationalSummary).toHaveBeenLastCalledWith({
      fromDate: '2026-10-01T05:00:00.000Z', toDate: '2026-11-01T04:59:59.000Z',
    });
  });

  it('says when there were no payments in the period', async () => {
    vi.mocked(reportsApi.getOperationalSummary).mockResolvedValue({ data: { ...october, revenueByPaymentMethod: {} } } as never);
    renderReports();

    expect(await screen.findByText('Sin pagos en el periodo.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('leads to Campañas only whoever may send them', async () => {
    const { unmount } = renderReports();
    await screen.findByText('Ventas netas (sin IVA)');
    expect(screen.queryByRole('link', { name: 'Ir a Campañas' })).not.toBeInTheDocument();
    unmount();

    renderReports({ permissions: ['ViewReports', 'ManageCampaigns'] });
    expect(await screen.findByRole('link', { name: 'Ir a Campañas' })).toHaveAttribute('href', '/campaigns');
  });

  it('says the report could not be read, and stops saying it once «Actualizar» reads it', async () => {
    const user = userEvent.setup();
    vi.mocked(reportsApi.getOperationalSummary)
      .mockRejectedValueOnce({ isAxiosError: true, response: { status: 500, data: { error: 'Timeout en SQL' } } })
      .mockResolvedValueOnce({ data: october } as never);
    renderReports();

    // A server failure says what the page could not do, not what the server wrote.
    expect(await screen.findByText('No se pudo cargar el reporte.')).toBeInTheDocument();
    expect(screen.queryByText('Ventas netas (sin IVA)')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Actualizar' }));

    expect(await screen.findByText('Ventas netas (sin IVA)')).toBeInTheDocument();
    expect(screen.queryByText('No se pudo cargar el reporte.')).not.toBeInTheDocument();
  });
});
