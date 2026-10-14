import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { appointmentsApi } from '../../api/appointmentsApi';
import { employeesApi } from '../../api/employeesApi';
import { quoteVisit } from '../../api/homeVisits';
import { productsApi } from '../../api/productsApi';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { AppointmentStatus, TenantCapability, type AppointmentDto } from '../../types';
import AppointmentsList from './AppointmentsList';

vi.mock('../../api/appointmentsApi', () => ({
  appointmentsApi: { getAll: vi.fn(), create: vi.fn(), update: vi.fn(), updateStatus: vi.fn() },
}));
vi.mock('../../api/employeesApi', () => ({ employeesApi: { getAll: vi.fn() } }));
vi.mock('../../api/productsApi', () => ({ productsApi: { getAll: vi.fn() } }));
vi.mock('../../api/homeVisits', () => ({ quoteVisit: vi.fn(), useVisitRoute: vi.fn() }));
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

function renderAgenda(items: AppointmentDto[]) {
  vi.mocked(appointmentsApi.getAll).mockResolvedValue({ data: page(items) } as never);
  return render(
    <MemoryRouter initialEntries={['/appointments']}>
      <Routes>
        <Route path="/appointments" element={<AppointmentsList />} />
        <Route path="/sell" element={<Sell />} />
      </Routes>
    </MemoryRouter>,
  );
}

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
});
