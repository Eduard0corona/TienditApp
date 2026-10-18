import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { publicMenuApi, type PublicBooking, type PublicBookingSlots } from '../../api/publicMenuApi';
import { AuthContext, type AuthContextType } from '../../contexts/AuthContextDefinition';
import PublicBookingPage from './PublicBookingPage';

vi.mock('../../api/publicMenuApi', () => ({
  publicMenuApi: { getBooking: vi.fn(), getBookingSlots: vi.fn(), requestAppointment: vi.fn() },
}));

const api = vi.mocked(publicMenuApi);
const LINK = 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE';

beforeAll(() => {
  // jsdom has no matchMedia, and the sheet (Offcanvas) asks it for the screen size.
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }) as MediaQueryList,
  });
});

function booking(overrides: Partial<PublicBooking> = {}): PublicBooking {
  return {
    businessName: 'Barbería Luis',
    slug: 'barberia-luis',
    theme: 0,
    pricesIncludeTax: true,
    daysAhead: 30,
    address: 'Av. Juárez 123, Centro',
    services: [
      { id: 's1', name: 'Corte de cabello', description: 'Con lavado', price: 150, durationMinutes: 45 },
      { id: 's2', name: 'Barba', price: 90, durationMinutes: 90 },
    ],
    ...overrides,
  } as PublicBooking;
}

const MONDAY_TEN = '2026-10-05T16:00:00+00:00';
const MONDAY_ELEVEN = '2026-10-05T17:00:00+00:00';

function week(overrides: Partial<PublicBookingSlots> = {}): PublicBookingSlots {
  return {
    days: [
      { date: '2026-10-04', label: 'domingo 4 de octubre', slots: [] },
      {
        date: '2026-10-05',
        label: 'lunes 5 de octubre',
        slots: [{ startAt: MONDAY_TEN, label: '10:00 a. m.' }, { startAt: MONDAY_ELEVEN, label: '11:00 a. m.' }],
      },
      { date: '2026-10-06', label: 'martes 6 de octubre', slots: [{ startAt: '2026-10-06T15:00:00+00:00', label: '9:00 a. m.' }] },
    ],
    nextFrom: '2026-10-11',
    ...overrides,
  };
}

function receipt(overrides: Record<string, unknown> = {}) {
  return {
    serviceName: 'Corte de cabello',
    startAt: MONDAY_TEN,
    endAt: '2026-10-05T16:45:00+00:00',
    dateLabel: 'lunes 5 de octubre',
    timeLabel: '10:00 a. m.',
    businessName: 'Barbería Luis',
    contactPhone: null,
    address: 'Av. Juárez 123, Centro',
    linkedToAccount: false,
    trackingToken: LINK,
    ...overrides,
  };
}

/** Where a link of the receipt took the customer. */
function Elsewhere() {
  const location = useLocation();
  return <p>En {location.pathname}{location.hash}</p>;
}

function renderPage(auth?: Partial<AuthContextType>) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const page = (
    <MemoryRouter initialEntries={['/m/barberia-luis/agendar']}>
      <Routes>
        <Route path="/m/:slug/agendar" element={<PublicBookingPage />} />
        <Route path="*" element={<Elsewhere />} />
      </Routes>
    </MemoryRouter>
  );
  return render(
    <QueryClientProvider client={queryClient}>
      {auth ? <AuthContext.Provider value={auth as AuthContextType}>{page}</AuthContext.Provider> : page}
    </QueryClientProvider>,
  );
}

async function pickMondayAtTen(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: /Corte de cabello/ }));
  await user.click(await screen.findByRole('button', { name: '10:00 a. m.' }));
  await user.click(screen.getByRole('button', { name: 'Continuar · Lun 5 · 10:00 a. m.' }));
  return within(await screen.findByRole('dialog', { name: 'Tu cita' }));
}

describe('PublicBookingPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    api.getBooking.mockResolvedValue(booking() as never);
    api.getBookingSlots.mockResolvedValue(week() as never);
  });

  it('books a service at a free time as a guest and gives the link of the appointment', async () => {
    const user = userEvent.setup();
    api.requestAppointment.mockResolvedValue(receipt() as never);
    renderPage();

    expect(await screen.findByRole('button', { name: /Corte de cabello.*45 min.*\$150\.00/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Barba.*1 h 30 min/ })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Corte de cabello/ }));

    // A day without free times cannot be picked; the first one with them opens.
    expect(await screen.findByRole('button', { name: 'Domingo 4 de octubre: sin horarios libres' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Lunes 5 de octubre: 2 horarios libres' })).toHaveAttribute('aria-pressed', 'true');
    expect(api.getBookingSlots).toHaveBeenCalledWith('barberia-luis', 's1', undefined, expect.anything());

    await user.click(screen.getByRole('button', { name: '10:00 a. m.' }));
    await user.click(screen.getByRole('button', { name: 'Continuar · Lun 5 · 10:00 a. m.' }));
    const sheet = within(await screen.findByRole('dialog', { name: 'Tu cita' }));
    expect(sheet.getByText('Lunes 5 de octubre · 10:00 a. m.')).toBeInTheDocument();
    await user.type(sheet.getByLabelText('Tu nombre'), 'Valeria Ruiz');
    await user.type(sheet.getByLabelText('Teléfono (WhatsApp)'), '5522107765');
    await user.click(sheet.getByRole('button', { name: 'Pedir cita' }));

    await waitFor(() => expect(api.requestAppointment).toHaveBeenCalledTimes(1));
    expect(api.requestAppointment).toHaveBeenCalledWith('barberia-luis', expect.objectContaining({
      serviceProductId: 's1',
      startAt: MONDAY_TEN,
      customerName: 'Valeria Ruiz',
      customerPhone: '5522107765',
      notes: null,
      idempotencyKey: expect.any(String),
    }));
    expect(await screen.findByRole('heading', { name: '¡Solicitud enviada!' })).toBeInTheDocument();
    expect(screen.getByText(/Barbería Luis te confirma por WhatsApp/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Copiar enlace de tu cita' })).toBeInTheDocument();
    await user.click(screen.getByRole('link', { name: 'Ver tu cita' }));
    expect(await screen.findByText(`En /cita#${LINK}`)).toBeInTheDocument();
  });

  it('goes to the next week the server offers and back', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(await screen.findByRole('button', { name: /Corte de cabello/ }));
    expect(await screen.findByRole('button', { name: 'Semana anterior' })).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Semana siguiente' }));
    await waitFor(() => expect(api.getBookingSlots).toHaveBeenLastCalledWith('barberia-luis', 's1', '2026-10-11', expect.anything()));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Semana anterior' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Semana anterior' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Semana anterior' })).toBeDisabled());
  });

  it('sends the same request again with its key, and asks for another time once it was taken', async () => {
    const user = userEvent.setup();
    const lost = { isAxiosError: true, response: { status: 503, data: {} } };
    const taken = { isAxiosError: true, response: { status: 400, data: { detail: 'Esa hora ya no está libre. Elige otra.' } } };
    api.requestAppointment.mockRejectedValueOnce(lost).mockRejectedValueOnce(taken);
    renderPage();

    const sheet = await pickMondayAtTen(user);
    await user.type(sheet.getByLabelText('Tu nombre'), 'Valeria Ruiz');
    await user.type(sheet.getByLabelText('Teléfono (WhatsApp)'), '5522107765');
    await user.click(sheet.getByRole('button', { name: 'Pedir cita' }));
    // The time is still free: the answer was lost, and the same request goes again with the same key.
    expect(await sheet.findByText('No se pudo enviar tu solicitud. Intenta de nuevo.')).toBeInTheDocument();

    api.getBookingSlots.mockResolvedValue(week({
      days: [{ date: '2026-10-05', label: 'lunes 5 de octubre', slots: [{ startAt: MONDAY_ELEVEN, label: '11:00 a. m.' }] }],
    }) as never);
    await user.click(sheet.getByRole('button', { name: 'Pedir cita' }));

    await waitFor(() => expect(api.requestAppointment).toHaveBeenCalledTimes(2));
    const [first, second] = api.requestAppointment.mock.calls.map(([, body]) => body.idempotencyKey);
    expect(second).toBe(first);
    // Taken meanwhile: the sheet closes, the times are read again and the one taken is gone.
    expect(await screen.findByText('Esa hora ya no está libre. Elige otra.')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.queryByRole('button', { name: '10:00 a. m.' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Continuar/ })).not.toBeInTheDocument();
  });

  it('writes the account’s name and phone, and the appointment is in Mis citas', async () => {
    const user = userEvent.setup();
    api.requestAppointment.mockResolvedValue(receipt({ linkedToAccount: true }) as never);
    renderPage({
      isAuthenticated: true,
      isLoading: false,
      user: {
        id: 'u1', email: 'mariana@demo.com', firstName: 'Mariana', lastName: 'Ortiz', fullName: 'Mariana Ortiz',
        phoneNumber: '+5212221234567', phoneNumberConfirmed: false, emailConfirmed: true, role: 'Customer', permissions: [],
      } as unknown as AuthContextType['user'],
    });

    const sheet = await pickMondayAtTen(user);
    expect(sheet.getByLabelText('Tu nombre')).toHaveValue('Mariana Ortiz');
    expect(sheet.getByLabelText('Teléfono (WhatsApp)')).toHaveValue('2221234567');
    await user.click(sheet.getByRole('button', { name: 'Pedir cita' }));

    expect(await screen.findByText('La encuentras en tus citas.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver mis citas' })).toHaveAttribute('href', '/mis-citas');
    expect(screen.queryByRole('button', { name: 'Copiar enlace de tu cita' })).not.toBeInTheDocument();
  });

  it('says so when the business does not book online', async () => {
    api.getBooking.mockRejectedValue({ isAxiosError: true, response: { status: 404, data: {} } });
    renderPage();

    expect(await screen.findByRole('heading', { name: 'Este negocio no agenda citas en línea' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver el menú' })).toHaveAttribute('href', '/m/barberia-luis');
  });
});
