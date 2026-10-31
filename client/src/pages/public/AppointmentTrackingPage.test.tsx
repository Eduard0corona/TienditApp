import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { publicMenuApi, type CustomerAppointment } from '../../api/publicMenuApi';
import ConfirmProvider from '../../components/feedback/ConfirmProvider';
import { AppointmentStatus } from '../../types';
import AppointmentTrackingPage from './AppointmentTrackingPage';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/publicMenuApi', () => ({
  publicMenuApi: { trackAppointment: vi.fn(), cancelAppointmentByLink: vi.fn() },
}));
const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

const api = vi.mocked(publicMenuApi);
const LINK = 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE';

function appointment(overrides: Partial<CustomerAppointment> = {}): CustomerAppointment {
  return {
    id: 'a1',
    serviceName: 'Corte de cabello',
    startAt: '2026-10-05T16:00:00Z',
    endAt: '2026-10-05T16:45:00Z',
    dateLabel: 'lunes 5 de octubre',
    timeLabel: '10:00 a. m.',
    status: AppointmentStatus.Requested,
    statusLabel: 'Por confirmar',
    canCancel: true,
    businessName: 'Barbería Luis',
    businessSlug: 'barberia-luis',
    businessContactPhone: '2221234567',
    address: 'Av. Juárez 123, Centro',
    ...overrides,
  };
}

function renderPage(url: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <ConfirmProvider>
        <MemoryRouter initialEntries={[url]}>
          <Routes>
            <Route path="/cita" element={<AppointmentTrackingPage />} />
          </Routes>
        </MemoryRouter>
      </ConfirmProvider>
    </QueryClientProvider>,
  );
}

describe('AppointmentTrackingPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('shows the appointment of the link and cancels it after asking', async () => {
    const user = userEvent.setup();
    api.trackAppointment.mockResolvedValue(appointment({ staffName: 'Ana L.' }) as never);
    api.cancelAppointmentByLink.mockResolvedValue(appointment({
      status: AppointmentStatus.Cancelled, statusLabel: 'La cancelaste', canCancel: false,
    }) as never);
    renderPage(`/cita#${LINK}`);

    expect(await screen.findByRole('heading', { name: 'Corte de cabello' })).toBeInTheDocument();
    expect(api.trackAppointment).toHaveBeenCalledWith(LINK, expect.anything());
    expect(screen.getByText('Por confirmar')).toBeInTheDocument();
    expect(screen.getByText('Lunes 5 de octubre · 10:00 a. m.')).toBeInTheDocument();
    // Who attends it, by the name the business gave them (Fase 24J).
    expect(screen.getByText('Te atiende Ana L.')).toBeInTheDocument();
    expect(screen.getByText('Barbería Luis te confirma por WhatsApp.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Agendar otra cita en Barbería Luis' })).toHaveAttribute('href', '/m/barberia-luis/agendar');
    const write = screen.getByRole('link', { name: 'Escribir a Barbería Luis por WhatsApp' });
    expect(write).toHaveAttribute('href', expect.stringContaining('https://wa.me/522221234567'));
    // «a. m.» already ends what the customer writes: it takes no second period.
    expect(new URL(write.getAttribute('href') ?? '').searchParams.get('text'))
      .toBe('Hola, les escribo por mi cita de Corte de cabello del lunes 5 de octubre a las 10:00 a. m.');

    await user.click(screen.getByRole('button', { name: 'Cancelar la cita de Corte de cabello del lunes 5 de octubre' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Cancelar cita' }));
    expect(api.cancelAppointmentByLink).not.toHaveBeenCalled();
    await user.click(dialog.getByRole('button', { name: 'Cancelar cita' }));

    await waitFor(() => expect(api.cancelAppointmentByLink).toHaveBeenCalledWith(LINK));
    expect(await screen.findByText('La cancelaste')).toBeInTheDocument();
    expect(toast.success).toHaveBeenCalledWith('Cancelaste tu cita.');
    expect(screen.queryByRole('button', { name: /Cancelar la cita/ })).not.toBeInTheDocument();
  });

  it('offers no cancel once the server says it is too late', async () => {
    api.trackAppointment.mockResolvedValue(appointment({
      status: AppointmentStatus.Confirmed, statusLabel: 'Confirmada', canCancel: false,
    }) as never);
    renderPage(`/cita#${LINK}`);

    expect(await screen.findByText('Confirmada')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Cancelar la cita/ })).not.toBeInTheDocument();
  });

  it('asks for the link when there is none', async () => {
    renderPage('/cita');
    expect(screen.getByRole('heading', { name: 'Abre el enlace de tu cita' })).toBeInTheDocument();
    expect(api.trackAppointment).not.toHaveBeenCalled();
  });

  it('says the link finds nothing', async () => {
    api.trackAppointment.mockRejectedValue({
      isAxiosError: true,
      response: { status: 404, data: { detail: 'No encontramos esa cita. El enlace pudo haber vencido.' } },
    });
    renderPage(`/cita#${LINK}`);

    expect(await screen.findByText('No encontramos esa cita. El enlace pudo haber vencido.')).toBeInTheDocument();
  });
});
