import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { appointmentsApi, type OnlineBookingSettings } from '../../api/appointmentsApi';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { OnlineMenuMode, TenantCapability, type TenantProfileDto } from '../../types';
import OnlineBookingCard from './OnlineBookingCard';

vi.mock('../../api/appointmentsApi', () => ({
  appointmentsApi: { getOnlineBooking: vi.fn(), updateOnlineBooking: vi.fn() },
}));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));

const api = vi.mocked(appointmentsApi);
const toast = { success: vi.fn(), warning: vi.fn(), error: vi.fn(), info: vi.fn() };
const NO_HOURS = 'Pon el horario de tu negocio: los horarios libres salen de él.';

function settings(overrides: Partial<OnlineBookingSettings> = {}): OnlineBookingSettings {
  return {
    enabled: false, capacity: 1, maxCapacity: 20, booksOnline: false, missing: [], slug: 'barberia-luis', staffCount: 0,
    answerReminderMinutes: 60, answerReminderChoices: [30, 60, 120, 180], ...overrides,
  };
}

function withCapabilities(capabilities: number) {
  vi.mocked(useTenant).mockReturnValue({
    tenant: { id: 't1', name: 'Barbería Luis', slug: 'barberia-luis', onlineMenuMode: OnlineMenuMode.MenuOnly, openingHours: [] } as unknown as TenantProfileDto,
    capabilities,
    isLoading: false,
    refreshTenant: vi.fn(),
  });
}

function renderCard() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <OnlineBookingCard />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('OnlineBookingCard', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    withCapabilities(TenantCapability.Service | TenantCapability.Appointments);
  });

  it('turns online booking on with how many at once, and says what is still missing', async () => {
    const user = userEvent.setup();
    api.getOnlineBooking.mockResolvedValue(settings({ missing: [NO_HOURS] }) as never);
    api.updateOnlineBooking.mockResolvedValue(settings({ enabled: true, capacity: 2, missing: [NO_HOURS] }) as never);
    renderCard();

    expect(await screen.findByText(NO_HOURS)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Guardar' })).toBeDisabled();
    await user.click(screen.getByRole('switch', { name: 'Recibir citas en línea' }));
    await user.clear(screen.getByLabelText('Citas a la vez'));
    await user.type(screen.getByLabelText('Citas a la vez'), '2');
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(api.updateOnlineBooking).toHaveBeenCalledWith(true, 2, 60));
    expect(toast.success).toHaveBeenCalledWith('Guardado.');
    expect(await screen.findByText(/Tus clientes todavía no pueden agendar en línea/)).toBeInTheDocument();
  });

  it('picks how long before its limit a request still waiting is recalled, or none (Fase 30B)', async () => {
    const user = userEvent.setup();
    api.getOnlineBooking.mockResolvedValue(settings({ enabled: true }) as never);
    api.updateOnlineBooking.mockResolvedValue(settings({ enabled: true, answerReminderMinutes: 0 }) as never);
    renderCard();

    const reminder = await screen.findByRole('combobox', { name: 'Recordatorio de solicitudes sin responder' });
    expect(reminder).toHaveValue('60');
    expect(Array.from((reminder as HTMLSelectElement).options).map((option) => option.text)).toEqual([
      'Media hora antes del límite', 'Una hora antes del límite', '2 horas antes del límite', '3 horas antes del límite', 'No recordarme',
    ]);
    expect(reminder).toHaveAccessibleDescription(/si una solicitud sigue sin respuesta, para que la respondas a tiempo/);

    await user.selectOptions(reminder, 'No recordarme');
    expect(reminder).toHaveAccessibleDescription('Solo te llega el correo de cada solicitud cuando llega.');
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(api.updateOnlineBooking).toHaveBeenCalledWith(true, 1, 0));
    expect(await screen.findByRole('combobox', { name: 'Recordatorio de solicitudes sin responder' })).toHaveValue('0');
  });

  it('keeps the hour of always when the API answering is one of before (Fase 30B)', async () => {
    const before: Partial<OnlineBookingSettings> = settings({ enabled: true });
    delete before.answerReminderMinutes;
    delete before.answerReminderChoices;
    api.getOnlineBooking.mockResolvedValue(before as never);
    renderCard();

    const reminder = await screen.findByRole('combobox', { name: 'Recordatorio de solicitudes sin responder' });
    expect(reminder).toHaveValue('60');
    expect(screen.getByRole('button', { name: 'Guardar' })).toBeDisabled();
  });

  it('links the booking page while customers book', async () => {
    api.getOnlineBooking.mockResolvedValue(settings({ enabled: true, booksOnline: true }) as never);
    renderCard();

    expect(await screen.findByText(/Tus clientes ya agendan desde tu menú en línea/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver tu página de citas' })).toHaveAttribute('href', '/m/barberia-luis/agendar');
  });

  it('keeps the number of appointments at once in range', async () => {
    const user = userEvent.setup();
    api.getOnlineBooking.mockResolvedValue(settings({ enabled: true, booksOnline: true }) as never);
    renderCard();

    const capacity = await screen.findByLabelText('Citas a la vez');
    await user.clear(capacity);
    await user.type(capacity, '21');

    expect(screen.getByText('Las citas a la vez van de 1 a 20.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Guardar' })).toBeDisabled();
  });

  it('says the times come from who attends while someone does, without the appointments at once', async () => {
    api.getOnlineBooking.mockResolvedValue(settings({ enabled: true, booksOnline: true, staffCount: 2 }) as never);
    renderCard();

    expect(await screen.findByText(/2 personas agendan en línea/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Citas a la vez')).not.toBeInTheDocument();
  });

  it('is not there for a business without the agenda', () => {
    withCapabilities(TenantCapability.Retail);
    renderCard();

    expect(screen.queryByRole('heading', { name: 'Citas en línea' })).not.toBeInTheDocument();
    expect(api.getOnlineBooking).not.toHaveBeenCalled();
  });
});
