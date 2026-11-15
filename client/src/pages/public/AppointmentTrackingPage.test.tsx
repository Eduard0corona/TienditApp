import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AppointmentDepositStatus, HomeVisitChargePolicy } from '../../api/generated/enums';
import { publicMenuApi, type CustomerAppointment } from '../../api/publicMenuApi';
import ConfirmProvider from '../../components/feedback/ConfirmProvider';
import { AppointmentStatus } from '../../types';
import { linkUploads, resetLinkUploads } from '../../hooks/useProblemPhotoUploads';
import AppointmentTrackingPage from './AppointmentTrackingPage';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/publicMenuApi', () => ({
  publicMenuApi: { trackAppointment: vi.fn(), cancelAppointmentByLink: vi.fn(), addAppointmentPhoto: vi.fn() },
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
    problemPhotos: 0,
    canAddPhotos: false,
    cancelReturnsDeposit: false,
    ...overrides,
  };
}

function renderPage(url: string, queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })) {
  return render(
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

/** A cache like the app's: what it read is fresh for 30 seconds. */
function appCache() {
  return new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 30_000 } } });
}

describe('AppointmentTrackingPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    resetLinkUploads();
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

    await waitFor(() => expect(api.cancelAppointmentByLink).toHaveBeenCalledWith(LINK, undefined));
    expect(await screen.findByText('La cancelaste')).toBeInTheDocument();
    expect(toast.success).toHaveBeenCalledWith('Cancelaste tu cita.');
    expect(screen.queryByRole('button', { name: /Cancelar la cita/ })).not.toBeInTheDocument();
  });

  it('says the deposit it was booked with, and what cancelling does with it, as the server decided (Fase 31O)', async () => {
    const user = userEvent.setup();
    api.trackAppointment.mockResolvedValue(appointment({
      status: AppointmentStatus.Confirmed, statusLabel: 'Confirmada', depositAmount: 45, depositStatus: AppointmentDepositStatus.Held,
      cancelReturnsDeposit: false,
    }) as never);
    renderPage(`/cita#${LINK}`);

    expect(await screen.findByText('Pagaste un anticipo de $45.00: se descuenta al cobrarte la cita.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Cancelar la cita/ }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Cancelar cita' }));
    expect(dialog.getByText(/Faltan menos de 24 horas: el negocio se queda con tu anticipo de \$45\.00\./)).toBeInTheDocument();
    // It sends what it said, so the server refuses it if that changed meanwhile.
    api.cancelAppointmentByLink.mockResolvedValue(appointment({ status: AppointmentStatus.Cancelled, statusLabel: 'La cancelaste', canCancel: false }) as never);
    await user.click(dialog.getByRole('button', { name: 'Cancelar cita' }));
    await waitFor(() => expect(api.cancelAppointmentByLink).toHaveBeenCalledWith(LINK, false));
  });

  it('says the deposit goes back when cancelling in time', async () => {
    const user = userEvent.setup();
    api.trackAppointment.mockResolvedValue(appointment({
      depositAmount: 45, depositStatus: AppointmentDepositStatus.Held, cancelReturnsDeposit: true,
    }) as never);
    renderPage(`/cita#${LINK}`);

    await user.click(await screen.findByRole('button', { name: /Cancelar la cita/ }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Cancelar cita' }));
    expect(dialog.getByText(/Te devolvemos tu anticipo de \$45\.00\./)).toBeInTheDocument();
  });

  it('offers no cancel once the server says it is too late', async () => {
    api.trackAppointment.mockResolvedValue(appointment({
      status: AppointmentStatus.Confirmed, statusLabel: 'Confirmada', canCancel: false,
    }) as never);
    renderPage(`/cita#${LINK}`);

    expect(await screen.findByText('Confirmada')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Cancelar la cita/ })).not.toBeInTheDocument();
  });

  it('says a visit to quote is at home by its postal code, and offers to ask for another visit (Fase 29B)', async () => {
    api.trackAppointment.mockResolvedValue(appointment({
      serviceName: 'Visita y diagnóstico',
      businessName: 'Plomería Hernández',
      businessSlug: 'plomeria-hernandez',
      address: null,
      visit: { postalCode: '72000', chargePolicy: HomeVisitChargePolicy.Free },
    }) as never);
    renderPage(`/cita#${LINK}`);

    expect(await screen.findByText('A domicilio · CP 72000')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Cómo llegar/ })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Pedir otra visita a Plomería Hernández' })).toHaveAttribute('href', '/m/plomeria-hernandez/visita');
    expect(screen.queryByRole('link', { name: /Agendar otra/ })).not.toBeInTheDocument();
  });

  it('takes photos of what happens from the link while the visit takes them, and says how many it has (Fase 29H)', async () => {
    const user = userEvent.setup();
    vi.spyOn(URL, 'createObjectURL').mockImplementation((file) => `blob:${(file as File).name}`);
    const visit = {
      serviceName: 'Visita y diagnóstico',
      businessName: 'Plomería Hernández',
      address: null,
      visit: { postalCode: '72000', chargePolicy: HomeVisitChargePolicy.Charged },
    };
    api.trackAppointment.mockResolvedValue(appointment({ ...visit, problemPhotos: 3, canAddPhotos: true }) as never);
    api.addAppointmentPhoto
      .mockResolvedValueOnce(appointment({ ...visit, problemPhotos: 4, canAddPhotos: true }) as never)
      .mockResolvedValueOnce(appointment({ ...visit, problemPhotos: 5, canAddPhotos: false }) as never);
    renderPage(`/cita#${LINK}`);

    const photos = within(await screen.findByRole('region', { name: 'Fotos de lo que pasa' }));
    expect(screen.getByText('Mandaste 3 fotos de lo que pasa')).toBeInTheDocument();
    expect(photos.getByText('Ayudan a Plomería Hernández a preparar la visita. Solo las ve el negocio.')).toBeInTheDocument();
    expect(photos.getByText('Caben 2 más.')).toBeInTheDocument();
    await user.upload(photos.getByTestId('problem-photos-input'), [
      new File(['a'], 'fuga.jpg', { type: 'image/jpeg' }), new File(['b'], 'pared.jpg', { type: 'image/jpeg' }),
    ]);

    expect(await photos.findByText('Listo: se mandaron 2 fotos.')).toBeInTheDocument();
    expect(api.addAppointmentPhoto.mock.calls.map(([token, file]) => [token, file.name])).toEqual([[LINK, 'fuga.jpg'], [LINK, 'pared.jpg']]);
    // The card says it with what the server answered; the section stays to say how they went, with nothing more to add.
    expect(screen.getByText('Mandaste 5 fotos de lo que pasa')).toBeInTheDocument();
    expect(photos.queryByRole('button', { name: /Agregar/ })).not.toBeInTheDocument();
  });

  it('shows the visit as the server has it, never an answer to a photo sent before it opened (Fase 29H, por la revisión)', async () => {
    const visit = { serviceName: 'Visita y diagnóstico', address: null, visit: { postalCode: '72000', chargePolicy: HomeVisitChargePolicy.Charged } };
    // Sent from the receipt while the visit waited; meanwhile the business turned it down.
    api.addAppointmentPhoto.mockResolvedValue(appointment({ ...visit, problemPhotos: 1, canAddPhotos: true }) as never);
    const uploads = linkUploads(LINK);
    uploads.enqueue([{ id: 'p1', file: new File(['a'], 'fuga.jpg', { type: 'image/jpeg' }) }]);
    await waitFor(() => expect(uploads.getSnapshot().latest?.problemPhotos).toBe(1));
    api.trackAppointment.mockResolvedValue(appointment({
      ...visit, status: AppointmentStatus.Cancelled, statusLabel: 'El negocio no la aceptó', canCancel: false, problemPhotos: 1,
    }) as never);

    renderPage(`/cita#${LINK}`, appCache());

    expect(await screen.findByText('El negocio no la aceptó')).toBeInTheDocument();
    expect(screen.queryByText('Por confirmar')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Cancelar la cita/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Agregar/ })).not.toBeInTheDocument();
  });

  it('keeps the visit cancelled when the customer comes back after sending a photo (Fase 29H, por la revisión)', async () => {
    const user = userEvent.setup();
    vi.spyOn(URL, 'createObjectURL').mockImplementation((file) => `blob:${(file as File).name}`);
    const visit = { serviceName: 'Visita y diagnóstico', address: null, visit: { postalCode: '72000', chargePolicy: HomeVisitChargePolicy.Charged } };
    const cancelled = appointment({ ...visit, status: AppointmentStatus.Cancelled, statusLabel: 'La cancelaste', canCancel: false, problemPhotos: 1 });
    api.trackAppointment.mockResolvedValueOnce(appointment({ ...visit, canAddPhotos: true }) as never).mockResolvedValue(cancelled as never);
    api.addAppointmentPhoto.mockResolvedValue(appointment({ ...visit, problemPhotos: 1, canAddPhotos: true }) as never);
    api.cancelAppointmentByLink.mockResolvedValue(cancelled as never);
    const queryClient = appCache();
    const page = renderPage(`/cita#${LINK}`, queryClient);
    const photos = within(await screen.findByRole('region', { name: 'Fotos de lo que pasa' }));
    await user.upload(photos.getByTestId('problem-photos-input'), new File(['a'], 'fuga.jpg', { type: 'image/jpeg' }));
    expect(await photos.findByText('Listo: se mandó una foto.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Cancelar la cita/ }));
    await user.click(within(await screen.findByRole('dialog', { name: 'Cancelar cita' })).getByRole('button', { name: 'Cancelar cita' }));
    expect(await screen.findByText('La cancelaste')).toBeInTheDocument();

    // Leaves and comes back while the page lives.
    page.unmount();
    renderPage(`/cita#${LINK}`, queryClient);

    expect(await screen.findByText('La cancelaste')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Cancelar la cita/ })).not.toBeInTheDocument();
  });

  it('offers no photos for a visit that no longer takes them, nor for an appointment at the business', async () => {
    api.trackAppointment.mockResolvedValue(appointment({ problemPhotos: 0, canAddPhotos: false }) as never);
    renderPage(`/cita#${LINK}`);

    expect(await screen.findByRole('heading', { name: 'Corte de cabello' })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Fotos de lo que pasa' })).not.toBeInTheDocument();
    expect(screen.queryByText(/de lo que pasa/)).not.toBeInTheDocument();
  });

  it('books again an appointment the business moved to the customer’s home, not another visit', async () => {
    api.trackAppointment.mockResolvedValue(appointment({ address: null, visit: { postalCode: '72000' } }) as never);
    renderPage(`/cita#${LINK}`);

    expect(await screen.findByText('A domicilio · CP 72000')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Agendar otra cita en Barbería Luis' })).toHaveAttribute('href', '/m/barberia-luis/agendar');
    expect(screen.queryByRole('link', { name: /Pedir otra visita/ })).not.toBeInTheDocument();
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
