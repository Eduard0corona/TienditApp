import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { appointmentsApi } from '../../api/appointmentsApi';
import { filesApi } from '../../api/filesApi';
import { HomeVisitChargePolicy } from '../../api/generated/enums';
import ConfirmProvider from '../../components/feedback/ConfirmProvider';
import { employeesApi } from '../../api/employeesApi';
import { quoteVisit } from '../../api/homeVisits';
import { quotesApi } from '../../api/quotesApi';
import { productsApi } from '../../api/productsApi';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { AppointmentChannel, AppointmentStatus, TenantCapability, type AppointmentDto } from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import AppointmentsList from './AppointmentsList';
import { APPOINTMENT_REQUESTS_CHANGED } from '../../hooks/useAppointmentRequests';

vi.mock('../../api/appointmentsApi', () => ({
  appointmentsApi: {
    getAll: vi.fn(), create: vi.fn(), update: vi.fn(), updateStatus: vi.fn(), accept: vi.fn(), decline: vi.fn(),
  },
}));
vi.mock('../../api/employeesApi', () => ({ employeesApi: { getAll: vi.fn() } }));
vi.mock('../../api/productsApi', () => ({ productsApi: { getAll: vi.fn() } }));
vi.mock('../../api/homeVisits', () => ({ quoteVisit: vi.fn(), useVisitRoute: vi.fn() }));
vi.mock('../../api/quotesApi', () => ({ quotesApi: { create: vi.fn() } }));
vi.mock('../../api/filesApi', () => ({ filesApi: { get: vi.fn() } }));
vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
const toast = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

const page = (items: AppointmentDto[]) => ({
  items, pageNumber: 1, totalPages: 1, totalCount: items.length, hasPreviousPage: false, hasNextPage: false,
});

const appointment = (overrides: Partial<AppointmentDto>): AppointmentDto => ({
  id: 'a1',
  customerName: 'Ana Ruiz',
  customerPhone: '5512345678',
  serviceName: 'Corte a domicilio',
  staffName: 'Luis Paz',
  startAt: '2026-10-14T16:00:00Z',
  endAt: '2026-10-14T17:00:00Z',
  status: AppointmentStatus.Confirmed,
  ...overrides,
});

const AT_HOME = appointment({
  visit: { postalCode: '72000', addressLine: 'Av. Juárez 123', references: 'Portón negro', zoneName: 'Centro', fee: 80 },
});

function apiError(detail: string): AxiosError {
  const headers = new AxiosHeaders();
  const response = { status: 400, statusText: '', headers, config: { headers }, data: { code: 'business_rule', detail } } as AxiosResponse;
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, response);
}

/** Where «Cobrar» took the person. */
function Sell() {
  const location = useLocation();
  return <p>Vender {location.search}</p>;
}

/** The count of requests booked online waiting from now on (the notice above the list): the one call for one row of them. */
const countsRequests = (params?: { status?: AppointmentStatus; pageSize?: number }) =>
  params?.status === AppointmentStatus.Requested && params.pageSize === 1;

function renderAgenda(items: AppointmentDto[], { requests = 0, path = '/appointments' } = {}) {
  vi.mocked(appointmentsApi.getAll).mockImplementation(async (params) => ({
    data: countsRequests(params) ? { ...page([]), totalCount: requests } : page(items),
  }) as never);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <ConfirmProvider>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route path="/appointments" element={<AppointmentsList />} />
            <Route path="/sell" element={<Sell />} />
            <Route path="/quotes/:id" element={<p>Cotización abierta</p>} />
          </Routes>
        </MemoryRouter>
      </ConfirmProvider>
    </QueryClientProvider>,
  );
}

/** The calls that read the list, not the count of requests. */
const listCalls = () => vi.mocked(appointmentsApi.getAll).mock.calls.map(([params]) => params).filter((params) => !countsRequests(params));

describe('AppointmentsList', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useAuth).mockReturnValue({
      user: { id: 'u1', firstName: 'Mariana', role: 'STAFF', permissions: ['ManageAppointments', 'ManageOrders'] },
    } as unknown as ReturnType<typeof useAuth>);
    vi.mocked(useTenant).mockReturnValue({
      tenant: { pricesIncludeTax: true },
      capabilities: TenantCapability.Service | TenantCapability.Appointments,
    } as unknown as ReturnType<typeof useTenant>);
    vi.mocked(productsApi.getAll).mockResolvedValue({ data: page([]) } as never);
    vi.mocked(employeesApi.getAll).mockResolvedValue({ data: page([]) } as never);
  });

  it('reads the list and the requests again, without a spinner, when a request arrives (Fase 29N)', async () => {
    renderAgenda([appointment({ id: 'a1' })], { requests: 0 });
    await screen.findByText('Ana Ruiz');
    const before = vi.mocked(appointmentsApi.getAll).mock.calls.length;
    vi.mocked(appointmentsApi.getAll).mockImplementation(async (params) => ({
      data: countsRequests(params)
        ? { ...page([]), totalCount: 1 }
        : page([appointment({ id: 'a1' }), appointment({ id: 'a2', customerName: 'Luz Mora', status: AppointmentStatus.Requested })]),
    }) as never);

    act(() => {
      window.dispatchEvent(new Event(APPOINTMENT_REQUESTS_CHANGED));
    });
    // The list stays while it reads again: no spinner in its place.
    expect(screen.getByText('Ana Ruiz')).toBeInTheDocument();

    expect(await screen.findByText('Luz Mora')).toBeInTheDocument();
    expect(screen.getByText('Ana Ruiz')).toBeInTheDocument();
    expect(vi.mocked(appointmentsApi.getAll).mock.calls.slice(before).some(([params]) => countsRequests(params))).toBe(true);
  });

  it('shows only the latest of two quiet reads that cross, and says nothing when one fails (Fase 29N)', async () => {
    renderAgenda([appointment({ id: 'a1' })]);
    await screen.findByText('Ana Ruiz');
    let releaseOld = () => {};
    const old = new Promise<void>((resolve) => {
      releaseOld = resolve;
    });
    let reads = 0;
    vi.mocked(appointmentsApi.getAll).mockImplementation(async (params) => {
      if (countsRequests(params)) return { data: { ...page([]), totalCount: 0 } } as never;
      reads += 1;
      if (reads === 1) {
        await old;
        return { data: page([appointment({ id: 'old', customerName: 'Lectura Vieja' })]) } as never;
      }
      if (reads === 2) return { data: page([appointment({ id: 'new', customerName: 'Lectura Nueva' })]) } as never;
      throw apiError('Sin conexión.');
    });

    act(() => {
      window.dispatchEvent(new Event(APPOINTMENT_REQUESTS_CHANGED));
    });
    act(() => {
      window.dispatchEvent(new Event(APPOINTMENT_REQUESTS_CHANGED));
    });
    expect(await screen.findByText('Lectura Nueva')).toBeInTheDocument();
    await act(async () => {
      releaseOld();
      await old;
    });

    expect(screen.queryByText('Lectura Vieja')).not.toBeInTheDocument();
    expect(screen.getByText('Lectura Nueva')).toBeInTheDocument();

    // A third quiet read fails: the list stays, without an error.
    act(() => {
      window.dispatchEvent(new Event(APPOINTMENT_REQUESTS_CHANGED));
    });
    await waitFor(() => expect(reads).toBe(3));
    expect(screen.getByText('Lectura Nueva')).toBeInTheDocument();
    expect(screen.queryByText('Sin conexión.')).not.toBeInTheDocument();
  });

  it('says why the list did not change when a quiet read that took the place of a filtered one fails (Fase 29N)', async () => {
    const user = userEvent.setup();
    renderAgenda([appointment({ id: 'a1' })]);
    await screen.findByText('Ana Ruiz');
    let releaseFiltered = () => {};
    const filtered = new Promise<void>((resolve) => {
      releaseFiltered = resolve;
    });
    vi.mocked(appointmentsApi.getAll).mockImplementation(async (params) => {
      if (countsRequests(params)) return { data: { ...page([]), totalCount: 0 } } as never;
      if (params?.homeVisit === true && vi.mocked(appointmentsApi.getAll).mock.calls.filter(([p]) => p?.homeVisit === true).length === 1) {
        await filtered;
        return { data: page([appointment({ id: 'home', customerName: 'Visita Filtrada' })]) } as never;
      }
      throw apiError('Sin conexión.');
    });

    // The person filters (a read with the spinner), and a request arrives before it ends: its quiet read fails.
    await user.selectOptions(screen.getByRole('combobox', { name: 'Dónde' }), 'A domicilio');
    act(() => {
      window.dispatchEvent(new Event(APPOINTMENT_REQUESTS_CHANGED));
    });

    expect(await screen.findByText('Sin conexión.')).toBeInTheDocument();
    await act(async () => {
      releaseFiltered();
      await filtered;
    });
    // The filtered read was replaced: what shows is the error, not a list of another filter without a word.
    expect(screen.queryByText('Visita Filtrada')).not.toBeInTheDocument();
  });

  it('says where each visit is, filters by where they happen and charges them in Vender', async () => {
    const user = userEvent.setup();
    renderAgenda([
      AT_HOME,
      appointment({ id: 'a2', customerName: 'Mara', status: AppointmentStatus.Completed, saleOrderId: 'o1' }),
      appointment({ id: 'a3', customerName: 'Iván', status: AppointmentStatus.Completed, saleOrderId: 'o2', saleCancelled: true }),
    ]);

    const visit = (await screen.findByText('Ana Ruiz')).closest('tr')!;
    expect(within(visit).getByText('A domicilio · CP 72000 · Centro')).toBeInTheDocument();
    // With its sale it is not charged again; once that sale was cancelled, it is.
    expect(within(screen.getByText('Mara').closest('tr')!).getByText('Con venta')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cobrar la cita de Mara' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cobrar la cita de Iván' })).toBeInTheDocument();

    await user.selectOptions(screen.getByRole('combobox', { name: 'Dónde' }), 'A domicilio');
    await waitFor(() => expect(appointmentsApi.getAll).toHaveBeenLastCalledWith(expect.objectContaining({ homeVisit: true })));

    await user.click(screen.getByRole('button', { name: 'Cobrar la cita de Ana Ruiz' }));
    expect(await screen.findByText('Vender ?appointment=a1')).toBeInTheDocument();
  });

  it('quotes a visit, open or done, for whoever takes orders: a cancelled one and an appointment at the business no', async () => {
    const user = userEvent.setup();
    vi.mocked(quotesApi.create).mockResolvedValue({ id: 'q1' } as never);
    renderAgenda([
      AT_HOME,
      appointment({ ...AT_HOME, id: 'a2', customerName: 'Mara', status: AppointmentStatus.Completed, saleOrderId: 'o1' }),
      appointment({ ...AT_HOME, id: 'a3', customerName: 'Iván', status: AppointmentStatus.Cancelled }),
      appointment({ id: 'a4', customerName: 'Rosa' }),
    ]);

    expect(await screen.findByRole('button', { name: 'Cotizar la visita de Mara' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cotizar la visita de Iván' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cotizar la visita de Rosa' })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Cotizar la visita de Ana Ruiz' }));

    expect(quotesApi.create).toHaveBeenCalledWith({ appointmentId: 'a1' });
    expect(await screen.findByText('Cotización abierta')).toBeInTheDocument();
  });

  it('opens the order of the job of a quote instead of charging or quoting it (Fase 29F)', async () => {
    renderAgenda([
      appointment({
        ...AT_HOME, id: 'a5', customerName: 'Valeria', serviceName: 'Trabajo de la cotización COT-000012',
        visit: { ...AT_HOME.visit!, fee: 0 }, workOrderId: 'o9', workOrderNumber: 'ORD-000031',
      }),
    ]);

    const job = (await screen.findByText('Valeria')).closest('tr')!;
    expect(within(job).getByText('Trabajo de la cotización COT-000012')).toBeInTheDocument();
    expect(within(job).getByRole('link', { name: 'ORD-000031' })).toHaveAttribute('href', '/orders?search=ORD-000031');
    expect(job).toHaveTextContent('Se cobra en la orden ORD-000031');
    expect(screen.queryByRole('button', { name: 'Cobrar la cita de Valeria' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cotizar la visita de Valeria' })).not.toBeInTheDocument();
  });

  it('says the job of an order charges nothing to go elsewhere, and names its order without a link to whoever sees no orders', async () => {
    const user = userEvent.setup();
    vi.mocked(useAuth).mockReturnValue({
      user: { id: 'u1', firstName: 'Mariana', role: 'STAFF', permissions: ['ManageAppointments'] },
    } as unknown as ReturnType<typeof useAuth>);
    vi.mocked(quoteVisit).mockImplementation(((postalCode: string) => Promise.resolve({ postalCode, zoneName: 'Norte', fee: 150 })) as never);
    renderAgenda([
      appointment({
        ...AT_HOME, id: 'a5', customerName: 'Valeria', serviceName: 'Trabajo de la cotización COT-000012',
        visit: { ...AT_HOME.visit!, fee: 0 }, workOrderId: 'o9', workOrderNumber: 'ORD-000031',
      }),
    ]);

    const job = (await screen.findByText('Valeria')).closest('tr')!;
    expect(job).toHaveTextContent('Se cobra en la orden ORD-000031');
    expect(within(job).queryByRole('link', { name: 'ORD-000031' })).not.toBeInTheDocument();
    await user.click(within(job).getByRole('button', { name: 'Editar o reprogramar la cita de Valeria' }));
    const form = within(await screen.findByRole('dialog', { name: /cita/i }));
    await user.clear(form.getByLabelText('Código postal *'));
    await user.type(form.getByLabelText('Código postal *'), '72100');

    expect(await form.findByText('Zona Norte · Visita sin cargo')).toBeInTheDocument();
  });

  it('offers no quote to whoever does not take orders', async () => {
    vi.mocked(useAuth).mockReturnValue({
      user: { id: 'u1', firstName: 'Mariana', role: 'STAFF', permissions: ['ManageAppointments'] },
    } as unknown as ReturnType<typeof useAuth>);
    renderAgenda([AT_HOME]);

    expect(await screen.findByText('Ana Ruiz')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cotizar la visita de Ana Ruiz' })).not.toBeInTheDocument();
  });

  it('offers no charge to whoever does not sell', async () => {
    vi.mocked(useAuth).mockReturnValue({
      user: { id: 'u2', firstName: 'Luis', role: 'STAFF', permissions: ['ManageAppointments'] },
    } as unknown as ReturnType<typeof useAuth>);
    renderAgenda([AT_HOME]);

    await screen.findByText('Ana Ruiz');
    expect(screen.queryByRole('button', { name: /Cobrar/ })).not.toBeInTheDocument();
  });

  it('quotes the visit with five digits, says why the server does not go there, and books it with the address', async () => {
    const user = userEvent.setup();
    vi.mocked(quoteVisit).mockImplementation((postalCode: string) => (postalCode === '72000'
      ? Promise.resolve({ postalCode, zoneName: 'Centro', fee: 80 })
      : Promise.reject(apiError(`Por ahora no hacemos visitas en el ${postalCode}.`))) as never);
    vi.mocked(appointmentsApi.create).mockResolvedValue({ data: AT_HOME } as never);
    renderAgenda([]);

    await user.click(await screen.findByRole('button', { name: 'Nueva cita' }));
    const form = within(await screen.findByRole('dialog', { name: 'Nueva cita' }));
    await user.click(form.getByLabelText('A domicilio'));
    await user.type(form.getByLabelText('Código postal *'), '7200');
    expect(quoteVisit).not.toHaveBeenCalled();
    await user.type(form.getByLabelText('Código postal *'), '0');
    expect(await form.findByText('Zona Centro · Cargo por visita $80.00')).toBeInTheDocument();

    await user.clear(form.getByLabelText('Código postal *'));
    await user.type(form.getByLabelText('Código postal *'), '72999');
    expect(await form.findByText('Por ahora no hacemos visitas en el 72999.')).toBeInTheDocument();

    await user.clear(form.getByLabelText('Código postal *'));
    await user.type(form.getByLabelText('Código postal *'), '72000');
    await user.type(form.getByLabelText('Dirección *'), 'Av. Juárez 123');
    await user.type(form.getByLabelText('Cliente *'), 'Ana Ruiz');
    await user.type(form.getByLabelText('Teléfono *'), '5512345678');
    await user.selectOptions(form.getByLabelText('Servicio *'), 'Otro (escribirlo)');
    await user.type(form.getByRole('textbox', { name: 'Nombre del servicio' }), 'Corte a domicilio');
    fireEvent.change(form.getByLabelText('Inicio *'), { target: { value: '2026-10-14T10:00' } });
    await user.click(form.getByRole('button', { name: 'Registrar cita' }));

    await waitFor(() => expect(appointmentsApi.create).toHaveBeenCalledTimes(1));
    expect(vi.mocked(appointmentsApi.create).mock.calls[0][0].visit)
      .toEqual({ postalCode: '72000', addressLine: 'Av. Juárez 123', references: undefined });
    // The form closes with a transition: the test ends once it has.
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('keeps what was agreed at the same address, and says the customer hears of a new place', async () => {
    const user = userEvent.setup();
    vi.mocked(quoteVisit).mockResolvedValue({ postalCode: '72005', zoneName: 'Sur', fee: 95 } as never);
    renderAgenda([AT_HOME]);

    await user.click(await screen.findByRole('button', { name: 'Editar o reprogramar la cita de Ana Ruiz' }));
    const form = within(await screen.findByRole('dialog', { name: 'Editar o reprogramar la cita' }));
    expect(form.getByText(/Zona Centro · Cargo por visita \$80\.00/)).toHaveTextContent('(lo acordado con el cliente)');
    expect(quoteVisit).not.toHaveBeenCalled();
    expect(form.queryByText(/el nuevo lugar/)).not.toBeInTheDocument();

    await user.clear(form.getByLabelText('Código postal *'));
    await user.type(form.getByLabelText('Código postal *'), '72005');
    expect(await form.findByText('Zona Sur · Cargo por visita $95.00')).toBeInTheDocument();
    expect(form.getByText('Al guardar, el cliente recibe por WhatsApp el nuevo lugar.')).toBeInTheDocument();
  });

  it('decides a request booked online: accepted, or declined after asking, and never charged before', async () => {
    const user = userEvent.setup();
    const request = appointment({ id: 'r1', customerName: 'Valeria', status: AppointmentStatus.Requested, channel: AppointmentChannel.Online });
    vi.mocked(appointmentsApi.accept).mockResolvedValue({ ...request, status: AppointmentStatus.Scheduled } as never);
    vi.mocked(appointmentsApi.decline).mockResolvedValue({ ...request, status: AppointmentStatus.Cancelled } as never);
    renderAgenda([request]);

    const row = (await screen.findByText('Valeria')).closest('tr')!;
    expect(within(row).getByText('En línea')).toBeInTheDocument();
    expect(within(row).getByText('Por confirmar')).toBeInTheDocument();
    // A request is decided first: no status to pick and nothing to charge yet.
    expect(within(row).queryByRole('combobox')).not.toBeInTheDocument();
    expect(within(row).queryByRole('button', { name: /Cobrar/ })).not.toBeInTheDocument();

    await user.click(within(row).getByRole('button', { name: 'Aceptar la cita de Valeria' }));
    await waitFor(() => expect(appointmentsApi.accept).toHaveBeenCalledWith('r1'));
    expect(toast.success).toHaveBeenCalledWith('Cita aceptada: el cliente recibe el aviso.');

    await user.click(within(row).getByRole('button', { name: 'Rechazar la cita de Valeria' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Rechazar la solicitud' }));
    expect(dialog.getByText(/el horario queda libre/)).toBeInTheDocument();
    expect(appointmentsApi.decline).not.toHaveBeenCalled();
    await user.click(dialog.getByRole('button', { name: 'Rechazar' }));
    await waitFor(() => expect(appointmentsApi.decline).toHaveBeenCalledWith('r1'));
    expect(toast.success).toHaveBeenCalledWith('Solicitud rechazada: el cliente recibe el aviso.');
  });

  it('says by when each request is answered in time, or that its limit passed (Fase 29L)', async () => {
    renderAgenda([
      appointment({ id: 'r1', customerName: 'Valeria', status: AppointmentStatus.Requested, channel: AppointmentChannel.Online, answerBy: '2099-03-02T16:00:00Z' }),
      appointment({ id: 'r2', customerName: 'Rocío', status: AppointmentStatus.Requested, channel: AppointmentChannel.Online, answerBy: '2000-03-02T16:00:00Z' }),
      appointment({ id: 'a1', customerName: 'Mario', status: AppointmentStatus.Scheduled, channel: AppointmentChannel.Online }),
    ]);

    const waiting = (await screen.findByText('Valeria')).closest('tr')!;
    expect(within(waiting).getByText(/^Responde antes del .+ para que cuente a tiempo$/)).toBeInTheDocument();
    expect(within(screen.getByText('Rocío').closest('tr')!).getByText('Ya pasó el límite para responder a tiempo')).toBeInTheDocument();
    expect(within(screen.getByText('Mario').closest('tr')!).queryByText(/a tiempo/)).not.toBeInTheDocument();
  });

  it('says a request of the team does not count in Explorar, and gives it no limit (Fase 29M)', async () => {
    renderAgenda([
      appointment({ id: 'r1', customerName: 'Valeria', status: AppointmentStatus.Requested, channel: AppointmentChannel.Online, fromTeam: true, answerBy: null }),
      appointment({ id: 'r2', customerName: 'Rocío', status: AppointmentStatus.Requested, channel: AppointmentChannel.Online, answerBy: '2099-03-02T16:00:00Z' }),
      appointment({ id: 'a1', customerName: 'Mario', status: AppointmentStatus.Scheduled, channel: AppointmentChannel.Online, fromTeam: true }),
    ]);

    const ours = (await screen.findByText('Valeria')).closest('tr')!;
    expect(within(ours).getByText('De tu equipo: no cuenta en Explorar')).toBeInTheDocument();
    expect(within(ours).queryByText(/a tiempo/)).not.toBeInTheDocument();
    expect(within(screen.getByText('Rocío').closest('tr')!).queryByText(/De tu equipo/)).not.toBeInTheDocument();
    // Once accepted there is nothing left to answer.
    expect(within(screen.getByText('Mario').closest('tr')!).queryByText(/De tu equipo/)).not.toBeInTheDocument();
  });

  it('says the limit passed while the agenda stays open (Fase 29L)', async () => {
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] });
    vi.setSystemTime(new Date('2026-10-14T16:00:00Z'));
    try {
      renderAgenda([appointment({
        id: 'r1', customerName: 'Valeria', status: AppointmentStatus.Requested, channel: AppointmentChannel.Online, answerBy: '2026-10-14T16:00:30Z',
      })]);

      const row = (await screen.findByText('Valeria')).closest('tr')!;
      expect(within(row).getByText(/^Responde antes de las .+ para que cuente a tiempo$/)).toBeInTheDocument();
      act(() => vi.advanceTimersByTime(60_000));
      expect(within(row).getByText('Ya pasó el límite para responder a tiempo')).toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });

  it('says a visit to quote requested online is one, how it is charged and where its quote goes (Fase 29B)', async () => {
    renderAgenda([appointment({
      id: 'v1',
      customerName: 'Valeria',
      customerEmail: 'valeria@correo.mx',
      status: AppointmentStatus.Requested,
      channel: AppointmentChannel.Online,
      visit: { postalCode: '72000', addressLine: 'Av. Juárez 123', zoneName: 'Centro', fee: 0, chargePolicy: HomeVisitChargePolicy.Free },
    }), AT_HOME]);

    const row = (await screen.findByText('Valeria')).closest('tr')!;
    expect(within(row).getByText('valeria@correo.mx')).toBeInTheDocument();
    expect(within(row).getByText('A domicilio · CP 72000 · Centro')).toBeInTheDocument();
    expect(within(row).getByText('Visita para cotizar · sin costo')).toBeInTheDocument();
    // A visit the team arranged says only where it is.
    const teams = screen.getByText('Ana Ruiz').closest('tr')!;
    expect(within(teams).getByText('A domicilio · CP 72000 · Centro')).toBeInTheDocument();
    expect(within(teams).queryByText(/Visita para cotizar/)).not.toBeInTheDocument();
  });

  it('shows what the customer said happens and their photos, only for a visit requested online (Fase 29H)', async () => {
    const user = userEvent.setup();
    const photo = (n: number) => `/files/orders/0b6f1c7e-2d1a-4c3b-9e8f-1a2b3c4d5e6f/visit-problem/202611/9f8e7d6c-5b4a-4321-8765-0fedcba9876${n}.webp`;
    vi.mocked(filesApi.get).mockResolvedValue({ data: new Blob(['x'], { type: 'image/webp' }) } as never);
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:foto');
    renderAgenda([
      appointment({
        id: 'v1',
        customerName: 'Valeria',
        status: AppointmentStatus.Requested,
        channel: AppointmentChannel.Online,
        notes: 'Gotea el calentador desde ayer',
        visit: { postalCode: '72000', addressLine: 'Av. Juárez 123', zoneName: 'Centro', fee: 50, chargePolicy: HomeVisitChargePolicy.Charged },
        problemPhotos: [photo(1), photo(2)],
      }),
      appointment({
        id: 'v2',
        customerName: 'Rosa',
        status: AppointmentStatus.Requested,
        channel: AppointmentChannel.Online,
        notes: 'No enciende el boiler',
        visit: { postalCode: '72000', addressLine: 'Av. Reforma 5', zoneName: 'Centro', fee: 50, chargePolicy: HomeVisitChargePolicy.Charged },
      }),
      { ...AT_HOME, notes: 'Llevar escalera' },
    ]);

    const row = (await screen.findByText('Valeria')).closest('tr')!;
    await user.click(within(row).getByRole('button', { name: 'Ver lo que pasa · 2 fotos, en la visita de Valeria' }));

    const dialog = within(await screen.findByRole('dialog', { name: 'Lo que pasa' }));
    expect(dialog.getByText('Gotea el calentador desde ayer')).toBeInTheDocument();
    expect(dialog.getByText(/^Valeria · /)).toBeInTheDocument();
    await waitFor(() => expect(dialog.getAllByRole('link', { name: /Abrir foto \d de lo que pasa/ })).toHaveLength(2));
    expect(filesApi.get).toHaveBeenCalledWith(photo(1));
    await user.click(dialog.getByRole('button', { name: 'Cerrar' }));
    // Only written: no photos. A visit the team arranged keeps the team's notes, which are not what the customer said.
    const rosa = screen.getByText('Rosa').closest('tr')!;
    expect(within(rosa).getByRole('button', { name: 'Ver lo que pasa, en la visita de Rosa' })).toBeInTheDocument();
    expect(within(screen.getByText('Ana Ruiz').closest('tr')!).queryByRole('button', { name: /lo que pasa/ })).not.toBeInTheDocument();
  });

  it('says how many requests wait and shows them, and opens on them from the notice', async () => {
    const user = userEvent.setup();
    renderAgenda([AT_HOME], { requests: 2 });

    expect(await screen.findByText(/Tienes 2 solicitudes de cita por confirmar/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Ver solicitudes' }));
    await waitFor(() => expect(listCalls().at(-1)).toEqual(expect.objectContaining({ status: AppointmentStatus.Requested })));
    expect(screen.queryByText(/solicitudes de cita por confirmar/)).not.toBeInTheDocument();
  });

  it('opens on the requests from the notice of the bell', async () => {
    renderAgenda([], { requests: 1, path: '/appointments?estado=por-confirmar' });

    await waitFor(() => expect(listCalls()[0]).toEqual(expect.objectContaining({ status: AppointmentStatus.Requested })));
    expect(screen.getByRole('combobox', { name: 'Estado' })).toHaveValue(String(AppointmentStatus.Requested));
  });

  it('says the requests read by when each is answered, only on them (Fase 29P)', async () => {
    const user = userEvent.setup();
    renderAgenda([AT_HOME], { requests: 1 });
    const note = 'Primero las que hay que responder antes; al final, las de tu equipo.';

    await screen.findByText(/Tienes 1 solicitud de cita por confirmar/);
    expect(screen.queryByText(note)).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Ver solicitudes' }));

    expect(await screen.findByText(note)).toBeInTheDocument();
  });

  it('turns the sound of the requests on and off on this device (Fase 29P)', async () => {
    const user = userEvent.setup();
    window.localStorage.removeItem('tienditapp:appointments-sound');
    renderAgenda([AT_HOME]);

    const sound = await screen.findByRole('button', { name: 'Sonido para solicitudes en línea' });
    expect(sound).toHaveAttribute('aria-pressed', 'false');
    await user.click(sound);

    // Its name stays; whether it is on says aria-pressed.
    expect(sound).toHaveAttribute('aria-pressed', 'true');
    expect(window.localStorage.getItem('tienditapp:appointments-sound')).toBe('on');
    // The one action of the screen is still «Nueva cita».
    expect(screen.getByRole('button', { name: 'Nueva cita' })).toHaveClass('btn-primary');
    expect(sound).not.toHaveClass('btn-primary');
    await user.click(sound);
    expect(sound).toHaveAttribute('aria-pressed', 'false');
    expect(window.localStorage.getItem('tienditapp:appointments-sound')).toBe('off');
    window.localStorage.removeItem('tienditapp:appointments-sound');
  });

  it('asks for a touch when the sound is on but the browser has not been touched yet (Fase 29P)', async () => {
    window.localStorage.setItem('tienditapp:appointments-sound', 'on');
    renderAgenda([AT_HOME]);

    expect(await screen.findByText('Toca la pantalla o presiona una tecla para que suenen las solicitudes.')).toBeInTheDocument();
    window.localStorage.removeItem('tienditapp:appointments-sound');
  });

  it('says when the customer cancelled the appointment', async () => {
    renderAgenda([appointment({ status: AppointmentStatus.Cancelled, channel: AppointmentChannel.Online, cancelledByCustomer: true })]);

    const row = (await screen.findByText('Ana Ruiz')).closest('tr')!;
    expect(within(row).getByText('La canceló el cliente')).toBeInTheDocument();
  });

  describe('with the business in Tijuana and the device on Mexico City time', () => {
    // Tijuana is UTC-7 in October; Mexico City, UTC-6.
    beforeEach(() => {
      vi.stubEnv('TZ', 'America/Mexico_City');
      setBusinessTimeZone('America/Tijuana');
    });

    afterEach(() => {
      setBusinessTimeZone(null);
      vi.unstubAllEnvs();
      vi.useRealTimers();
    });

    it('opens the upcoming ones at the start of the business day, not the device day', async () => {
      // 06:30 UTC on the 14th: 00:30 of the 14th on the device, still 23:30 of the 13th in Tijuana.
      vi.useFakeTimers({ toFake: ['Date'] });
      vi.setSystemTime(new Date('2026-10-14T06:30:00.000Z'));
      renderAgenda([]);

      await waitFor(() => expect(listCalls()[0]).toEqual(expect.objectContaining({ fromDate: '2026-10-13T07:00:00.000Z' })));
    });

    it('shows the day an appointment ends when it ends after the midnight of the business', async () => {
      // 23:00 of the 13th to 00:30 of the 14th in Tijuana (the same day, 00:00 to 01:30, on the device); the other
      // one is 9:00 to 10:00 of the 14th.
      renderAgenda([
        appointment({ id: 'n1', customerName: 'Noche', startAt: '2026-10-14T06:00:00Z', endAt: '2026-10-14T07:30:00Z' }),
        appointment({ id: 'd1', customerName: 'Día' }),
      ]);

      const late = (await screen.findByText('Noche')).closest('tr')!;
      expect(late).toHaveTextContent(/13 de oct · 11:00 p\.\s?m\. a 14\/10\/26/);
      const day = screen.getByText('Día').closest('tr')!;
      expect(day).toHaveTextContent(/14 de oct · 09:00 a\.\s?m\. a 10:00 a\.\s?m\./);
      expect(day).not.toHaveTextContent(/14\/10\/26/);
    });

    it('keeps an appointment in the hour the clock repeats where it was when it is saved without moving it', async () => {
      const user = userEvent.setup();
      vi.mocked(appointmentsApi.update).mockResolvedValue({ data: appointment({}) } as never);
      // 1 November: 01:30 PST to 02:30 PST, the second time 01:30 comes (the first is 08:30 UTC, still PDT).
      renderAgenda([appointment({ startAt: '2026-11-01T09:30:00Z', endAt: '2026-11-01T10:30:00Z' })]);

      await user.click(await screen.findByRole('button', { name: 'Editar o reprogramar la cita de Ana Ruiz' }));
      const form = within(await screen.findByRole('dialog', { name: 'Editar o reprogramar la cita' }));
      expect(form.getByLabelText('Inicio *')).toHaveValue('2026-11-01T01:30');
      expect(form.queryByText(/el nuevo horario/)).not.toBeInTheDocument();
      await user.click(form.getByRole('button', { name: 'Guardar cambios' }));

      await waitFor(() => expect(appointmentsApi.update).toHaveBeenCalledTimes(1));
      expect(vi.mocked(appointmentsApi.update).mock.calls[0][1]).toEqual(expect.objectContaining({
        startAt: '2026-11-01T09:30:00.000Z',
        endAt: '2026-11-01T10:30:00.000Z',
      }));
      expect(toast.success).toHaveBeenCalledWith('Cita actualizada.');
    });

    it('books the start typed as the clock of the business, and the end after its duration', async () => {
      const user = userEvent.setup();
      vi.mocked(appointmentsApi.create).mockResolvedValue({ data: appointment({}) } as never);
      renderAgenda([]);

      await user.click(await screen.findByRole('button', { name: 'Nueva cita' }));
      const form = within(await screen.findByRole('dialog', { name: 'Nueva cita' }));
      await user.type(form.getByLabelText('Cliente *'), 'Ana Ruiz');
      await user.type(form.getByLabelText('Teléfono *'), '5512345678');
      await user.selectOptions(form.getByLabelText('Servicio *'), 'Otro (escribirlo)');
      await user.type(form.getByRole('textbox', { name: 'Nombre del servicio' }), 'Corte');
      fireEvent.change(form.getByLabelText('Inicio *'), { target: { value: '2026-10-14T10:00' } });
      expect(form.getByLabelText('Fin *')).toHaveValue('2026-10-14T11:00');
      await user.click(form.getByRole('button', { name: 'Registrar cita' }));

      await waitFor(() => expect(appointmentsApi.create).toHaveBeenCalledTimes(1));
      expect(vi.mocked(appointmentsApi.create).mock.calls[0][0]).toEqual(expect.objectContaining({
        startAt: '2026-10-14T17:00:00.000Z',
        endAt: '2026-10-14T18:00:00.000Z',
      }));
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    });

    it('shows the appointment at the hour of the business and moves it from there', async () => {
      const user = userEvent.setup();
      vi.mocked(appointmentsApi.update).mockResolvedValue({ data: appointment({}) } as never);
      // 16:00 to 17:00 UTC: 9:00 to 10:00 in Tijuana (10:00 to 11:00 on the device).
      renderAgenda([appointment({})]);

      await user.click(await screen.findByRole('button', { name: 'Editar o reprogramar la cita de Ana Ruiz' }));
      const form = within(await screen.findByRole('dialog', { name: 'Editar o reprogramar la cita' }));
      expect(form.getByLabelText('Inicio *')).toHaveValue('2026-10-14T09:00');
      expect(form.getByLabelText('Fin *')).toHaveValue('2026-10-14T10:00');
      // The same hour is not a new one for the customer.
      expect(form.queryByText(/el nuevo horario/)).not.toBeInTheDocument();

      fireEvent.change(form.getByLabelText('Inicio *'), { target: { value: '2026-10-14T12:30' } });
      expect(form.getByLabelText('Fin *')).toHaveValue('2026-10-14T13:30');
      expect(form.getByText('Al guardar, el cliente recibe por WhatsApp el nuevo horario.')).toBeInTheDocument();
      await user.click(form.getByRole('button', { name: 'Guardar cambios' }));

      await waitFor(() => expect(appointmentsApi.update).toHaveBeenCalledTimes(1));
      expect(vi.mocked(appointmentsApi.update).mock.calls[0][1]).toEqual(expect.objectContaining({
        startAt: '2026-10-14T19:30:00.000Z',
        endAt: '2026-10-14T20:30:00.000Z',
      }));
      expect(toast.success).toHaveBeenCalledWith('Cita reprogramada: el cliente recibe el aviso.');
    });
  });
});
