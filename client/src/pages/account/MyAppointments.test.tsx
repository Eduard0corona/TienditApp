import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { accountApi } from '../../api/accountApi';
import type { CustomerAppointment } from '../../api/publicMenuApi';
import ConfirmProvider from '../../components/feedback/ConfirmProvider';
import { AppointmentStatus, MyAppointmentsScope } from '../../types';
import MyAppointments from './MyAppointments';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/accountApi', () => ({ accountApi: { getAppointments: vi.fn(), cancelAppointment: vi.fn() } }));
const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

const api = vi.mocked(accountApi);

function appointment(overrides: Partial<CustomerAppointment> = {}): CustomerAppointment {
  return {
    id: 'a1',
    serviceName: 'Corte de cabello',
    startAt: '2026-10-05T16:00:00Z',
    endAt: '2026-10-05T16:45:00Z',
    dateLabel: 'lunes 5 de octubre',
    timeLabel: '10:00 a. m.',
    status: AppointmentStatus.Scheduled,
    statusLabel: 'Confirmada',
    canCancel: true,
    businessName: 'Barbería Luis',
    businessSlug: 'barberia-luis',
    businessContactPhone: null,
    address: null,
    ...overrides,
  };
}

const page = (items: CustomerAppointment[], overrides: Record<string, unknown> = {}) => ({
  items, pageNumber: 1, totalPages: 1, totalCount: items.length, hasPreviousPage: false, hasNextPage: false, ...overrides,
});

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <ConfirmProvider>
        <MemoryRouter>
          <MyAppointments />
        </MemoryRouter>
      </ConfirmProvider>
    </QueryClientProvider>,
  );
}

describe('MyAppointments', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('shows the upcoming ones and the ones before, and cancels one after asking', async () => {
    const user = userEvent.setup();
    let cancelled = false;
    api.getAppointments.mockImplementation(async (scope) => (scope === MyAppointmentsScope.Upcoming
      ? page(cancelled ? [] : [appointment()])
      : page(cancelled
        ? [appointment({ status: AppointmentStatus.Cancelled, statusLabel: 'La cancelaste', canCancel: false })]
        : [appointment({ id: 'a0', serviceName: 'Barba', status: AppointmentStatus.Completed, statusLabel: 'Terminada', canCancel: false })])) as never);
    api.cancelAppointment.mockImplementation(async () => {
      cancelled = true;
      return appointment({ status: AppointmentStatus.Cancelled, statusLabel: 'La cancelaste', canCancel: false }) as never;
    });
    renderPage();

    const upcoming = within(await screen.findByRole('region', { name: 'Próximas' }));
    expect(upcoming.getByText('Corte de cabello')).toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: 'Anteriores' })).getByText('Terminada')).toBeInTheDocument();
    expect(api.getAppointments).toHaveBeenCalledWith(MyAppointmentsScope.Past, 1, 10, expect.anything());

    await user.click(upcoming.getByRole('button', { name: 'Cancelar la cita de Corte de cabello del lunes 5 de octubre' }));
    await user.click(within(await screen.findByRole('dialog', { name: 'Cancelar cita' })).getByRole('button', { name: 'Cancelar cita' }));

    await waitFor(() => expect(api.cancelAppointment).toHaveBeenCalledWith('a1'));
    expect(toast.success).toHaveBeenCalledWith('Cancelaste tu cita.');
    expect(await screen.findByText('La cancelaste')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Próximas' })).not.toBeInTheDocument();
  });

  it('says what goes here while there are none', async () => {
    api.getAppointments.mockResolvedValue(page([]) as never);
    renderPage();

    expect(await screen.findByRole('heading', { name: 'Todavía no tienes citas' })).toBeInTheDocument();
  });

  it('pages through the ones before', async () => {
    const user = userEvent.setup();
    api.getAppointments.mockImplementation(async (scope, pageNumber) => (scope === MyAppointmentsScope.Upcoming
      ? page([])
      : page([appointment({ id: `p${pageNumber}`, serviceName: `Servicio ${pageNumber}`, status: AppointmentStatus.Completed })], {
        pageNumber, totalPages: 2, hasNextPage: pageNumber < 2,
      })) as never);
    renderPage();

    expect(await screen.findByText('Servicio 1')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Más antiguas' }));
    expect(await screen.findByText('Servicio 2')).toBeInTheDocument();
    expect(screen.getByText('Página 2 de 2')).toBeInTheDocument();
  });
});
