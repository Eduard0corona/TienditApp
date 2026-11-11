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
import {
  MAX_ABSENCES,
  absenceDraft,
  absencesProblem,
  absencesToSave,
  describeStaff,
  nameLength,
  staffDraftProblem,
  type StaffDraft,
} from './onlineBookingStaff';

vi.mock('../../api/appointmentsApi', () => ({
  appointmentsApi: {
    getOnlineBookingStaff: vi.fn(),
    updateOnlineBookingStaff: vi.fn(),
    uploadOnlineBookingStaffPhoto: vi.fn(),
    deleteOnlineBookingStaffPhoto: vi.fn(),
  },
}));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));

const api = vi.mocked(appointmentsApi);
const toast = { success: vi.fn(), warning: vi.fn(), error: vi.fn(), info: vi.fn() };
const WEEKDAYS = [1, 2, 3, 4, 5].map((day) => ({ day, opens: '09:00', closes: '18:00' }));

function member(overrides: Partial<OnlineBookingStaffMember> = {}): OnlineBookingStaffMember {
  return {
    userId: 'u1', fullName: 'Ana López', name: 'Ana L.', configured: false, enabled: false, allServices: true, serviceIds: [],
    weeklyHours: null, absences: [], photoUrl: null, ...overrides,
  } as OnlineBookingStaffMember;
}

const SERVICES = [{ id: 's1', name: 'Corte de cabello', bookableNow: true }, { id: 's2', name: 'Barba', bookableNow: true }];

function settings(members: OnlineBookingStaffMember[], overrides: Partial<OnlineBookingStaffSettings> = {}): OnlineBookingStaffSettings {
  return { members, services: SERVICES, maxEnabled: 30, ...overrides };
}

function withCapabilities(capabilities: number) {
  vi.mocked(useTenant).mockReturnValue({
    tenant: { id: 't1', name: 'Barbería Luis', slug: 'barberia-luis', openingHours: WEEKDAYS } as unknown as TenantProfileDto,
    capabilities,
    isLoading: false,
    refreshTenant: vi.fn(),
  });
}

function renderCard(queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })) {
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

  it('keeps a paused service with the person, saying it is not booked now', async () => {
    const user = userEvent.setup();
    api.getOnlineBookingStaff.mockResolvedValue(settings(
      [member({ configured: true, enabled: true, allServices: false, serviceIds: ['s2'] })],
      { services: [SERVICES[0], { ...SERVICES[1], bookableNow: false }] },
    ) as never);
    api.updateOnlineBookingStaff.mockResolvedValue(member({ configured: true, enabled: true, allServices: false, serviceIds: ['s2'] }) as never);
    renderCard();

    expect(await screen.findByText('Tus clientes ven «Ana L.» · 1 servicio · Horario del negocio')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Editar a Ana López' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Ana López' }));
    expect(dialog.getByLabelText(/Barba/)).toBeChecked();
    expect(dialog.getByText('· No se agenda ahora')).toBeInTheDocument();
    await user.click(dialog.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(api.updateOnlineBookingStaff).toHaveBeenCalledTimes(1));
    expect(api.updateOnlineBookingStaff.mock.calls[0][1]).toMatchObject({ allServices: false, serviceIds: ['s2'] });
  });

  it('says only «Guardado» when editing who already books online, and refreshes what depends on the team', async () => {
    const user = userEvent.setup();
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    api.getOnlineBookingStaff.mockResolvedValue(settings([member({ configured: true, enabled: true })]) as never);
    api.updateOnlineBookingStaff.mockResolvedValue(member({ configured: true, enabled: true, name: 'Anita L.' }) as never);
    renderCard(queryClient);

    await user.click(await screen.findByRole('button', { name: 'Editar a Ana López' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Ana López' }));
    await user.clear(dialog.getByLabelText('Nombre que ven tus clientes'));
    await user.type(dialog.getByLabelText('Nombre que ven tus clientes'), 'Anita L.');
    await user.click(dialog.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Guardado.'));
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['online-booking-settings'] });
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['public-booking'] });
    expect(await screen.findByText(/Tus clientes ven «Anita L\.»/)).toBeInTheDocument();
  });

  it('starts the switch off for someone new when as many people as allowed already book online', async () => {
    const user = userEvent.setup();
    api.getOnlineBookingStaff.mockResolvedValue(settings(
      [member({ userId: 'u2', fullName: 'Luis Méndez', configured: true, enabled: true }), member()],
      { maxEnabled: 1 },
    ) as never);
    renderCard();

    expect(await screen.findByText('Ya agendan 1 personas, el máximo: apaga a alguien para agregar a otra.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Configurar a Ana López' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Ana López' }));
    expect(dialog.getByRole('switch', { name: 'Agenda citas en línea' })).not.toBeChecked();
  });

  // Fase 31I (ADR 0089): the days a person does not attend, and their photo.

  it('says the next days a person does not attend in the list, and saves the ones the owner changes', async () => {
    const user = userEvent.setup();
    const absences = [{ from: '2026-11-16', to: '2026-11-18' }, { from: '2026-12-24', to: '2026-12-24' }];
    api.getOnlineBookingStaff.mockResolvedValue(settings([member({ configured: true, enabled: true, absences })]) as never);
    api.updateOnlineBookingStaff.mockResolvedValue(member({ configured: true, enabled: true, absences: [absences[0]] }) as never);
    renderCard();

    expect(await screen.findByText('Tus clientes ven «Ana L.» · Todos los servicios · Horario del negocio · No atiende: Lun 16 nov – Mié 18 nov y 1 más'))
      .toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Editar a Ana López' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Ana López' }));
    expect(dialog.getAllByLabelText('Desde').map((input) => (input as HTMLInputElement).value)).toEqual(['2026-11-16', '2026-12-24']);
    await user.click(dialog.getByRole('button', { name: 'Quitar la ausencia 2' }));
    await user.click(dialog.getByRole('button', { name: 'Agregar días' }));
    // A single day is one choice: «Hasta» follows «Desde».
    const from = dialog.getAllByLabelText('Desde')[1];
    await user.type(from, '2027-01-04');
    expect(dialog.getAllByLabelText('Hasta')[1]).toHaveValue('2027-01-04');
    await user.click(dialog.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(api.updateOnlineBookingStaff).toHaveBeenCalledTimes(1));
    expect(api.updateOnlineBookingStaff.mock.calls[0][1].absences).toEqual([
      { from: '2026-11-16', to: '2026-11-18' },
      { from: '2027-01-04', to: '2027-01-04' },
    ]);
    expect(await screen.findByText(/No atiende: Lun 16 nov – Mié 18 nov$/)).toBeInTheDocument();
  });

  it('says two absences that cross before saving', async () => {
    const user = userEvent.setup();
    api.getOnlineBookingStaff.mockResolvedValue(settings([member({
      configured: true, enabled: true, absences: [{ from: '2026-11-16', to: '2026-11-18' }, { from: '2026-11-18', to: '2026-11-20' }],
    })]) as never);
    renderCard();

    await user.click(await screen.findByRole('button', { name: 'Editar a Ana López' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Ana López' }));
    await user.click(dialog.getByRole('button', { name: 'Guardar' }));

    expect(dialog.getByRole('alert')).toHaveTextContent('Dos ausencias se cruzan: júntalas en una.');
    expect(api.updateOnlineBookingStaff).not.toHaveBeenCalled();
  });

  it('puts and takes away the photo of someone saved at once, and asks to save someone new first', async () => {
    const user = userEvent.setup();
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');
    api.getOnlineBookingStaff.mockResolvedValue(settings([
      member({ configured: true, enabled: true }),
      member({ userId: 'u2', fullName: 'Luis Méndez', name: 'Luis M.' }),
    ]) as never);
    api.uploadOnlineBookingStaffPhoto.mockResolvedValue({ photoUrl: '/uploads/staff/t1/ana_xs.webp' });
    api.deleteOnlineBookingStaffPhoto.mockResolvedValue({ photoUrl: null } as never);
    const { container } = renderCard(queryClient);

    await user.click(await screen.findByRole('button', { name: 'Editar a Ana López' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Ana López' }));
    const photo = new File(['photo'], 'ana.png', { type: 'image/png' });
    await user.upload(dialog.getByLabelText('Foto que ven tus clientes'), photo);

    await waitFor(() => expect(api.uploadOnlineBookingStaffPhoto).toHaveBeenCalledWith('u1', photo));
    expect(toast.success).toHaveBeenCalledWith('Foto guardada: tus clientes ya la ven junto a «Ana L.».');
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['public-booking'] });
    expect(await dialog.findByRole('button', { name: /Cambiar foto/ })).toBeInTheDocument();
    expect(document.querySelectorAll('img[src="/uploads/staff/t1/ana_xs.webp"]')).toHaveLength(2);

    await user.click(dialog.getByRole('button', { name: /Quitar foto/ }));
    await waitFor(() => expect(api.deleteOnlineBookingStaffPhoto).toHaveBeenCalledWith('u1'));
    expect(await dialog.findByRole('button', { name: /Subir foto/ })).toBeInTheDocument();
    expect(container.ownerDocument.querySelector('img')).toBeNull();
    await user.click(dialog.getByRole('button', { name: 'Cancelar' }));

    await user.click(screen.getByRole('button', { name: 'Configurar a Luis Méndez' }));
    const newcomer = within(await screen.findByRole('dialog', { name: 'Luis Méndez' }));
    expect(newcomer.getByText('Guárdala primero; luego le pones su foto.')).toBeInTheDocument();
    expect(newcomer.queryByRole('button', { name: /Subir foto/ })).not.toBeInTheDocument();
  });

  it('says what the server refused about a photo, and keeps the one there was', async () => {
    const user = userEvent.setup();
    api.getOnlineBookingStaff.mockResolvedValue(settings([member({ configured: true, enabled: true, photoUrl: '/uploads/staff/t1/old_xs.webp' })]) as never);
    api.uploadOnlineBookingStaffPhoto.mockRejectedValue({
      isAxiosError: true, response: { status: 400, data: { detail: 'El archivo no puede exceder 5 MB.' } },
    });
    renderCard();

    await user.click(await screen.findByRole('button', { name: 'Editar a Ana López' }));
    const dialog = within(await screen.findByRole('dialog', { name: 'Ana López' }));
    await user.upload(dialog.getByLabelText('Foto que ven tus clientes'), new File(['x'], 'big.png', { type: 'image/png' }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('El archivo no puede exceder 5 MB.'));
    expect(document.querySelectorAll('img[src="/uploads/staff/t1/old_xs.webp"]')).toHaveLength(2);
    expect(dialog.getByRole('button', { name: /Cambiar foto/ })).toBeEnabled();
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
    week: toDraft([]), absences: [],
  };

  it('asks for a name customers may see, a service and the hours of a day turned on', () => {
    expect(staffDraftProblem(draft, SERVICES)).toBeNull();
    expect(staffDraftProblem({ ...draft, name: '  ' }, SERVICES)).toBe('Escribe el nombre que verán tus clientes.');
    expect(staffDraftProblem({ ...draft, name: 'a'.repeat(41) }, SERVICES)).toBe('El nombre que ven tus clientes puede tener hasta 40 caracteres.');
    expect(staffDraftProblem({ ...draft, allServices: false }, [])).toBe('Aún no tienes servicios para agendar en línea: elige «Todos los servicios».');
    // A service no longer in the catalog does not count.
    expect(staffDraftProblem({ ...draft, allServices: false, serviceIds: ['gone'] }, SERVICES)).toBe('Elige al menos un servicio, o todos.');
    expect(staffDraftProblem({ ...draft, ownHours: true }, SERVICES)).toBe('Marca al menos un día que atiende, o usa el horario del negocio.');
    const monday = draft.week.map((day) => (day.day === 1 ? { ...day, open: true, ranges: [{ opens: '10:00', closes: '' }] } : day));
    expect(staffDraftProblem({ ...draft, ownHours: true, week: monday }, SERVICES)).toBe('Escribe a qué hora entra y sale el lunes.');
  });

  it('counts an emoji with its skin tone, or a flag, as one character, as the server does', () => {
    expect(nameLength('Ana 👍🏽')).toBe(5);
    expect(nameLength('🇲🇽')).toBe(1);
    expect(staffDraftProblem({ ...draft, name: '💈🏽'.repeat(40) }, SERVICES)).toBeNull();
  });

  it('says every service when the list has them all, and counts only the ones it has', () => {
    expect(describeStaff(member({ allServices: false, serviceIds: ['s1', 's2'] }), SERVICES)).toBe('Todos los servicios · Horario del negocio');
    expect(describeStaff(member({ allServices: false, serviceIds: ['s1', 's2'] }), [...SERVICES, { id: 's3', name: 'Tinte', bookableNow: true }]))
      .toBe('2 servicios · Horario del negocio');
    expect(describeStaff(member({ allServices: false, serviceIds: ['s1', 'gone'] }), SERVICES)).toBe('1 servicio · Horario del negocio');
  });

  it('asks for both days of each absence, in order, up to a year, up to the most, and none crossing another', () => {
    const away = (from: string, to: string) => absenceDraft(from, to);
    expect(absencesProblem([away('2026-11-16', '2026-11-18'), away('2026-11-19', '2026-11-19')])).toBeNull();
    // A year: 366 days, both included.
    expect(absencesProblem([away('2026-01-01', '2027-01-01')])).toBeNull();
    expect(absencesProblem([away('2026-11-16', '')])).toBe('Escribe desde y hasta qué día de cada ausencia.');
    expect(absencesProblem([away('2026-11-18', '2026-11-16')])).toBe('Una ausencia no puede terminar antes de empezar.');
    expect(absencesProblem([away('2026-01-01', '2027-01-02')])).toBe('Una ausencia puede durar hasta un año.');
    expect(absencesProblem([away('2026-12-01', '2026-12-10'), away('2026-11-20', '2026-12-01')])).toBe('Dos ausencias se cruzan: júntalas en una.');
    const many = Array.from({ length: MAX_ABSENCES + 1 }, (_, n) => away(`2027-01-${String(n + 1).padStart(2, '0')}`, `2027-01-${String(n + 1).padStart(2, '0')}`));
    expect(absencesProblem(many)).toBe('Una persona puede tener hasta 20 ausencias por venir.');
    expect(staffDraftProblem({ ...draft, absences: [away('2026-11-18', '2026-11-16')] }, SERVICES)).toBe('Una ausencia no puede terminar antes de empezar.');
    expect(absencesToSave([away('2026-12-01', '2026-12-02'), away('2026-11-01', '2026-11-01')]))
      .toEqual([{ from: '2026-11-01', to: '2026-11-01' }, { from: '2026-12-01', to: '2026-12-02' }]);
  });

  it('says the next absence of a person, a single day by itself', () => {
    expect(describeStaff(member({ absences: [{ from: '2026-12-25', to: '2026-12-25' }] }), SERVICES))
      .toBe('Todos los servicios · Horario del negocio · No atiende: Vie 25 dic');
  });
});
