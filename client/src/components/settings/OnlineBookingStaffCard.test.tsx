import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { appointmentsApi, type OnlineBookingStaffMember, type OnlineBookingStaffSettings } from '../../api/appointmentsApi';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { TenantCapability, type TenantProfileDto } from '../../types';
import OnlineBookingStaffCard from './OnlineBookingStaffCard';
import { toDraft } from '../../utils/openingHours';
import { describeStaff, staffDraftProblem, type StaffDraft } from './onlineBookingStaff';

vi.mock('../../api/appointmentsApi', () => ({
  appointmentsApi: { getOnlineBookingStaff: vi.fn(), updateOnlineBookingStaff: vi.fn() },
}));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));

const api = vi.mocked(appointmentsApi);
const toast = { success: vi.fn(), warning: vi.fn(), error: vi.fn(), info: vi.fn() };
const WEEKDAYS = [1, 2, 3, 4, 5].map((day) => ({ day, opens: '09:00', closes: '18:00' }));

function member(overrides: Partial<OnlineBookingStaffMember> = {}): OnlineBookingStaffMember {
  return {
    userId: 'u1', fullName: 'Ana López', name: 'Ana L.', configured: false, enabled: false, allServices: true, serviceIds: [],
    weeklyHours: null, ...overrides,
  } as OnlineBookingStaffMember;
}

function settings(members: OnlineBookingStaffMember[]): OnlineBookingStaffSettings {
  return {
    members,
    services: [{ id: 's1', name: 'Corte de cabello' }, { id: 's2', name: 'Barba' }],
    maxEnabled: 30,
  };
}

function withCapabilities(capabilities: number) {
  vi.mocked(useTenant).mockReturnValue({
    tenant: { id: 't1', name: 'Barbería Luis', slug: 'barberia-luis', openingHours: WEEKDAYS } as unknown as TenantProfileDto,
    capabilities,
    isLoading: false,
    refreshTenant: vi.fn(),
  });
}

function renderCard() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <OnlineBookingStaffCard />
    </QueryClientProvider>,
  );
}

describe('OnlineBookingStaffCard', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    withCapabilities(TenantCapability.Service | TenantCapability.Appointments);
  });

  it('lists the team with who books online, what customers see and their services and hours', async () => {
    api.getOnlineBookingStaff.mockResolvedValue(settings([
      member(),
      member({
        userId: 'u2', fullName: 'Luis Méndez', name: 'Luis M.', configured: true, enabled: true, allServices: false,
        serviceIds: ['s2'], weeklyHours: [{ day: 1, opens: '10:00', closes: '14:00' }],
      }),
    ]) as never);
    renderCard();

    expect(await screen.findByText('Luis Méndez')).toBeInTheDocument();
    expect(screen.getByText('Agenda en línea')).toBeInTheDocument();
    expect(screen.getByText('Tus clientes ven «Luis M.» · 1 servicio · Horario propio')).toBeInTheDocument();
    expect(screen.getByText('No agenda en línea')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Configurar a Ana López' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Editar a Luis Méndez' })).toBeInTheDocument();
  });

  it('sets a person up with some services and their own hours, starting from the business’s', async () => {
    const user = userEvent.setup();
    api.getOnlineBookingStaff.mockResolvedValue(settings([member()]) as never);
    api.updateOnlineBookingStaff.mockResolvedValue(member({ configured: true, enabled: true }) as never);
    renderCard();

    await user.click(await screen.findByRole('button', { name: 'Configurar a Ana López' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Ana López' }));
    expect(dialog.getByRole('switch', { name: 'Agenda citas en línea' })).toBeChecked();
    expect(dialog.getByLabelText('Nombre que ven tus clientes')).toHaveValue('Ana L.');
    await user.click(dialog.getByLabelText('Solo algunos'));
    await user.click(dialog.getByLabelText('Barba'));
    await user.click(dialog.getByLabelText('Su propio horario'));
    // The business's hours to start from; Monday shortened.
    expect(dialog.getByLabelText('Entra el lunes')).toHaveValue('09:00');
    await user.clear(dialog.getByLabelText('Sale el lunes'));
    await user.type(dialog.getByLabelText('Sale el lunes'), '13:00');
    await user.click(dialog.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(api.updateOnlineBookingStaff).toHaveBeenCalledTimes(1));
    const [userId, body] = api.updateOnlineBookingStaff.mock.calls[0];
    expect(userId).toBe('u1');
    expect(body).toMatchObject({ enabled: true, name: 'Ana L.', allServices: false, serviceIds: ['s2'] });
    expect(body.weeklyHours).toContainEqual({ day: 1, opens: '09:00', closes: '13:00' });
    expect(body.weeklyHours).toHaveLength(5);
    expect(toast.success).toHaveBeenCalledWith('Guardado: Ana L. ya agenda citas en línea.');
  });

  it('says what is missing before saving, and what the server refused', async () => {
    const user = userEvent.setup();
    api.getOnlineBookingStaff.mockResolvedValue(settings([member()]) as never);
    api.updateOnlineBookingStaff.mockRejectedValue({
      isAxiosError: true, response: { status: 400, data: { detail: 'Puedes ofrecer hasta 30 personas en tus citas en línea.' } },
    });
    renderCard();

    await user.click(await screen.findByRole('button', { name: 'Configurar a Ana López' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Ana López' }));
    await user.click(dialog.getByLabelText('Solo algunos'));
    await user.click(dialog.getByRole('button', { name: 'Guardar' }));
    expect(dialog.getByRole('alert')).toHaveTextContent('Elige al menos un servicio, o todos.');
    expect(api.updateOnlineBookingStaff).not.toHaveBeenCalled();

    await user.click(dialog.getByLabelText('Todos los servicios, también los que agregues'));
    await user.click(dialog.getByRole('button', { name: 'Guardar' }));
    expect(await dialog.findByText('Puedes ofrecer hasta 30 personas en tus citas en línea.')).toBeInTheDocument();
  });

  it('is not there for a business without the agenda', () => {
    withCapabilities(TenantCapability.Retail);
    renderCard();

    expect(screen.queryByRole('heading', { name: 'Quién atiende' })).not.toBeInTheDocument();
    expect(api.getOnlineBookingStaff).not.toHaveBeenCalled();
  });
});

describe('onlineBookingStaff', () => {
  const draft: StaffDraft = {
    enabled: true, name: 'Ana L.', allServices: true, serviceIds: [], ownHours: false,
    week: toDraft([]),
  };

  it('asks for a name customers may see, a service and the hours of a day turned on', () => {
    expect(staffDraftProblem(draft, 2)).toBeNull();
    expect(staffDraftProblem({ ...draft, name: '  ' }, 2)).toBe('Escribe el nombre que verán tus clientes.');
    expect(staffDraftProblem({ ...draft, name: 'a'.repeat(41) }, 2)).toBe('El nombre que ven tus clientes puede tener hasta 40 caracteres.');
    expect(staffDraftProblem({ ...draft, allServices: false }, 0)).toBe('Aún no tienes servicios para agendar en línea: elige «Todos los servicios».');
    expect(staffDraftProblem({ ...draft, ownHours: true }, 2)).toBe('Marca al menos un día que atiende, o usa el horario del negocio.');
    const monday = draft.week.map((day) => (day.day === 1 ? { ...day, open: true, ranges: [{ opens: '10:00', closes: '' }] } : day));
    expect(staffDraftProblem({ ...draft, ownHours: true, week: monday }, 2)).toBe('Escribe a qué hora entra y sale el lunes.');
  });

  it('says every service when the list has them all', () => {
    expect(describeStaff(member({ allServices: false, serviceIds: ['s1', 's2'] }), 2)).toBe('Todos los servicios · Horario del negocio');
    expect(describeStaff(member({ allServices: false, serviceIds: ['s1', 's2'] }), 3)).toBe('2 servicios · Horario del negocio');
  });
});
