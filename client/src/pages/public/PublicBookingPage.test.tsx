import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { ResponseTime } from '../../api/generated/enums';
import { publicMenuApi, type PublicBooking, type PublicBookingSlots } from '../../api/publicMenuApi';
import { AuthContext, type AuthContextType } from '../../contexts/AuthContextDefinition';
import PublicBookingPage from './PublicBookingPage';

vi.mock('../../api/publicMenuApi', () => ({
  publicMenuApi: { getBooking: vi.fn(), getBookingSlots: vi.fn(), requestAppointment: vi.fn(), startAppointmentDeposit: vi.fn() },
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

const MONDAY = { date: '2026-10-05', dateLabel: 'lunes 5 de octubre' };
const TUESDAY = { date: '2026-10-06', dateLabel: 'martes 6 de octubre' };

function week(overrides: Partial<PublicBookingSlots> = {}): PublicBookingSlots {
  return {
    days: [
      { date: '2026-10-04', label: 'domingo 4 de octubre', slots: [] },
      {
        date: '2026-10-05',
        label: 'lunes 5 de octubre',
        slots: [{ startAt: MONDAY_TEN, label: '10:00 a. m.', ...MONDAY }, { startAt: MONDAY_ELEVEN, label: '11:00 a. m.', ...MONDAY }],
      },
      { date: '2026-10-06', label: 'martes 6 de octubre', slots: [{ startAt: '2026-10-06T15:00:00+00:00', label: '9:00 a. m.', ...TUESDAY }] },
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

function renderPage(auth?: Partial<AuthContextType>, entry = '/m/barberia-luis/agendar') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const page = (
    <MemoryRouter initialEntries={[entry]}>
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

  it('says how soon the business answers, as its card in Explorar does (Fase 29K)', async () => {
    api.getBooking.mockResolvedValue(booking({ responseTime: ResponseTime.WithinAnHour }) as never);
    renderPage();

    expect(await screen.findByText('Suele responder en menos de una hora')).toBeInTheDocument();
  });

  it('says nothing of how soon it answers without it', async () => {
    renderPage();

    expect(await screen.findByRole('button', { name: /Corte de cabello/ })).toBeInTheDocument();
    expect(screen.queryByText(/Suele responder/)).not.toBeInTheDocument();
  });

  it('books a service at a free time as a guest and gives the link of the appointment', async () => {
    const user = userEvent.setup();
    api.requestAppointment.mockResolvedValue(receipt() as never);
    renderPage();

    expect(await screen.findByRole('button', { name: /Corte de cabello.*45 min.*\$150\.00/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Barba.*1 h 30 min/ })).toBeInTheDocument();
    // The title the server writes for search engines (Fase 24D-2).
    expect(document.title).toBe('Agenda tu cita en Barbería Luis · TienditApp');
    await user.click(screen.getByRole('button', { name: /Corte de cabello/ }));

    // A day without free times cannot be picked; the first one with them opens.
    expect(await screen.findByRole('button', { name: /Dom 4.*Sin horarios.*domingo 4 de octubre/ })).toBeDisabled();
    expect(screen.getByRole('button', { name: /Lun 5.*2 horarios.*lunes 5 de octubre/ })).toHaveAttribute('aria-pressed', 'true');
    expect(api.getBookingSlots).toHaveBeenCalledWith('barberia-luis', 's1', undefined, undefined, expect.anything());

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
      staffId: null,
      idempotencyKey: expect.any(String),
    }));
    expect(await screen.findByRole('heading', { name: '¡Solicitud enviada!' })).toBeInTheDocument();
    expect(screen.getByText(/Barbería Luis te confirma por WhatsApp/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Copiar enlace de tu cita' })).toBeInTheDocument();
    await user.click(screen.getByRole('link', { name: 'Ver tu cita' }));
    expect(await screen.findByText(`En /cita#${LINK}`)).toBeInTheDocument();
  });

  it('books a service that asks a deposit by paying it in Mercado Pago, keeping the return key on this device (Fase 31O)', async () => {
    const user = userEvent.setup();
    const assign = vi.fn();
    const location = window.location;
    Object.defineProperty(window, 'location', { configurable: true, value: { ...location, assign } });
    try {
      api.getBooking.mockResolvedValue(booking({
        depositCancelHours: 24,
        services: [{ id: 's1', name: 'Corte de cabello', price: 150, durationMinutes: 45, staffIds: [], depositAmount: 45 }],
      }) as never);
      api.startAppointmentDeposit.mockResolvedValue({
        checkoutId: 'c1', checkoutUrl: 'https://www.mercadopago.com.mx/checkout/v1/redirect?pref_id=1', amount: 45,
        expiresAt: '2026-10-04T17:30:00Z', returnKey: 'llave-1',
      } as never);
      renderPage();

      expect(await screen.findByRole('button', { name: /Corte de cabello.*Anticipo para reservar: \$45\.00/ })).toBeInTheDocument();
      const sheet = await pickMondayAtTen(user);
      expect(sheet.getByText(/Lo pagas en línea con Mercado Pago y se descuenta al cobrarte la cita/)).toBeInTheDocument();
      expect(sheet.getByText(/si la cancelas con al menos 24 horas/)).toBeInTheDocument();
      await user.type(sheet.getByLabelText('Tu nombre'), 'Valeria Ruiz');
      await user.type(sheet.getByLabelText('Teléfono (WhatsApp)'), '5522107765');
      await user.click(sheet.getByRole('button', { name: 'Pagar anticipo de $45.00' }));

      await waitFor(() => expect(assign).toHaveBeenCalledWith('https://www.mercadopago.com.mx/checkout/v1/redirect?pref_id=1'));
      expect(api.startAppointmentDeposit).toHaveBeenCalledWith('barberia-luis', expect.objectContaining({
        serviceProductId: 's1', startAt: MONDAY_TEN, expectedDeposit: 45, idempotencyKey: expect.any(String),
      }));
      expect(api.requestAppointment).not.toHaveBeenCalled();
      expect(JSON.parse(window.localStorage.getItem('tienditapp.onlineCheckouts') ?? '{}').c1.returnKey).toBe('llave-1');
      // While the browser leaves for Mercado Pago, nothing else is sent.
      expect(sheet.getByRole('button', { name: 'Abriendo Mercado Pago…' })).toBeDisabled();
    } finally {
      Object.defineProperty(window, 'location', { configurable: true, value: location });
      window.localStorage.clear();
    }
  });

  it('reads the deposit again when the server says it changed, and sends nothing to pay', async () => {
    const user = userEvent.setup();
    api.getBooking
      .mockResolvedValueOnce(booking({ services: [{ id: 's1', name: 'Corte de cabello', price: 150, durationMinutes: 45, staffIds: [], depositAmount: 45 }] }) as never)
      .mockResolvedValue(booking({ services: [{ id: 's1', name: 'Corte de cabello', price: 150, durationMinutes: 45, staffIds: [], depositAmount: 60 }] }) as never);
    api.startAppointmentDeposit.mockRejectedValue({
      isAxiosError: true,
      response: { status: 400, data: { detail: 'El anticipo de este servicio ahora es de $60.00. Revísalo antes de pagar.' } },
    });
    renderPage();

    const sheet = await pickMondayAtTen(user);
    await user.type(sheet.getByLabelText('Tu nombre'), 'Valeria Ruiz');
    await user.type(sheet.getByLabelText('Teléfono (WhatsApp)'), '5522107765');
    await user.click(sheet.getByRole('button', { name: 'Pagar anticipo de $45.00' }));

    expect(await sheet.findByText(/ahora es de \$60\.00/)).toBeInTheDocument();
    expect(await sheet.findByRole('button', { name: 'Pagar anticipo de $60.00' })).toBeInTheDocument();
    expect(api.getBooking).toHaveBeenCalledTimes(2);
  });

  it('keeps the sheet open while the request is on its way', async () => {
    const user = userEvent.setup();
    let answered: (value: unknown) => void = () => undefined;
    api.requestAppointment.mockReturnValue(new Promise((resolve) => { answered = resolve; }) as never);
    renderPage();

    await user.click(await screen.findByRole('button', { name: /Corte de cabello/ }));
    await user.click(await screen.findByRole('button', { name: '10:00 a. m.' }));
    await user.click(screen.getByRole('button', { name: 'Continuar · Lun 5 · 10:00 a. m.' }));
    const sheet = await screen.findByRole('dialog', { name: 'Tu cita' });
    await user.type(within(sheet).getByLabelText('Tu nombre'), 'Valeria Ruiz');
    await user.type(within(sheet).getByLabelText('Teléfono (WhatsApp)'), '5522107765');
    await user.click(within(sheet).getByRole('button', { name: 'Pedir cita' }));

    expect(within(sheet).getByRole('button', { name: 'Enviando…' })).toBeDisabled();
    expect(within(sheet).queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(screen.getByRole('dialog', { name: 'Tu cita' })).toHaveClass('show');

    answered(receipt());
    expect(await screen.findByRole('heading', { name: '¡Solicitud enviada!' })).toBeInTheDocument();
  });

  it('can close while the times are read again after a refusal, and sends nothing else until they are', async () => {
    const user = userEvent.setup();
    const refused = { isAxiosError: true, response: { status: 400, data: { detail: 'Escribe un teléfono de 10 dígitos.' } } };
    api.requestAppointment.mockRejectedValue(refused);
    let reads = 0;
    let reread: (value: unknown) => void = () => undefined;
    api.getBookingSlots.mockImplementation((() => (++reads === 1
      ? Promise.resolve(week())
      : new Promise((resolve) => { reread = resolve; }))) as never);
    renderPage();

    const sheet = await pickMondayAtTen(user);
    await user.type(sheet.getByLabelText('Tu nombre'), 'Valeria Ruiz');
    await user.type(sheet.getByLabelText('Teléfono (WhatsApp)'), '5522107765');
    await user.click(sheet.getByRole('button', { name: 'Pedir cita' }));

    // Refused, nothing is on its way: the sheet can close while the times are read again, and does not send it again.
    await waitFor(() => expect(reads).toBe(2));
    expect(sheet.getByRole('button', { name: 'Pedir cita' })).toBeDisabled();
    await user.click(sheet.getByRole('button', { name: 'Close' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(api.requestAppointment).toHaveBeenCalledTimes(1);

    // Still free: the sheet opens again with why it was refused, ready to send it again.
    reread(week());
    await user.click(await screen.findByRole('button', { name: 'Continuar · Lun 5 · 10:00 a. m.' }));
    const again = within(await screen.findByRole('dialog', { name: 'Tu cita' }));
    expect(again.getByRole('alert')).toHaveTextContent('Escribe un teléfono de 10 dígitos.');
    expect(again.getByRole('button', { name: 'Pedir cita' })).toBeEnabled();
  });

  it('goes to the next week the server offers and back', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(await screen.findByRole('button', { name: /Corte de cabello/ }));
    expect(await screen.findByRole('button', { name: 'Semana anterior' })).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Semana siguiente' }));
    await waitFor(() => expect(api.getBookingSlots).toHaveBeenLastCalledWith('barberia-luis', 's1', '2026-10-11', undefined, expect.anything()));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Semana anterior' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Semana anterior' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Semana anterior' })).toBeDisabled());
  });

  it('sends the same request again with its key when the answer was lost, and asks for another time once it was taken', async () => {
    const user = userEvent.setup();
    const lost = { isAxiosError: true, response: { status: 503, data: {} } };
    const taken = { isAxiosError: true, response: { status: 400, data: { detail: 'Esa hora ya no está libre. Elige otra.' } } };
    api.requestAppointment.mockRejectedValueOnce(lost).mockRejectedValueOnce(taken);
    renderPage();

    const sheet = await pickMondayAtTen(user);
    await user.type(sheet.getByLabelText('Tu nombre'), 'Valeria Ruiz');
    await user.type(sheet.getByLabelText('Teléfono (WhatsApp)'), '5522107765');
    // The server stored it and took the time, but its answer is lost: the times would no longer offer it.
    api.getBookingSlots.mockResolvedValue(week({
      days: [{ date: '2026-10-05', label: 'lunes 5 de octubre', slots: [{ startAt: MONDAY_ELEVEN, label: '11:00 a. m.', ...MONDAY }] }],
    }) as never);
    await user.click(sheet.getByRole('button', { name: 'Pedir cita' }));
    // The sheet stays with the same request, which goes again with its key instead of becoming another appointment.
    expect(await sheet.findByRole('alert')).toHaveTextContent('No se pudo enviar tu solicitud. Intenta de nuevo. Si ya se había enviado, no se repite.');
    expect(api.getBookingSlots).toHaveBeenCalledTimes(1);

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

  it('says the day of a time after midnight, in the opening of the day before', async () => {
    const user = userEvent.setup();
    api.getBookingSlots.mockResolvedValue(week({
      days: [{
        date: '2026-10-05',
        label: 'lunes 5 de octubre',
        slots: [
          { startAt: '2026-10-06T05:30:00+00:00', label: '11:30 p. m.', ...MONDAY },
          { startAt: '2026-10-06T06:30:00+00:00', label: '12:30 a. m.', ...TUESDAY },
        ],
      }],
    }) as never);
    renderPage();

    await user.click(await screen.findByRole('button', { name: /Corte de cabello/ }));
    expect(await screen.findByRole('button', { name: '11:30 p. m.' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: '12:30 a. m. · Mar 6' }));
    await user.click(screen.getByRole('button', { name: 'Continuar · Mar 6 · 12:30 a. m.' }));

    expect(within(await screen.findByRole('dialog', { name: 'Tu cita' })).getByText('Martes 6 de octubre · 12:30 a. m.')).toBeInTheDocument();
  });

  it('lets the customer choose who attends, with that person’s times, and says so in the receipt', async () => {
    const user = userEvent.setup();
    api.getBooking.mockResolvedValue(booking({
      staff: [{ id: 'p1', name: 'Ana L.' }, { id: 'p2', name: 'Luis M.' }],
      services: [
        { id: 's1', name: 'Corte de cabello', price: 150, durationMinutes: 45, staffIds: ['p1', 'p2'] },
        { id: 's2', name: 'Barba', price: 90, durationMinutes: 90, staffIds: ['p2'] },
      ],
    }) as never);
    api.requestAppointment.mockResolvedValue(receipt({ staffName: 'Ana L.' }) as never);
    renderPage();

    await user.click(await screen.findByRole('button', { name: /Corte de cabello/ }));
    const people = within(await screen.findByRole('group', { name: 'Quién te atiende' }));
    // No preference first: whoever is free.
    expect(people.getByRole('button', { name: /Sin preferencia/ })).toHaveAttribute('aria-pressed', 'true');
    expect(api.getBookingSlots).toHaveBeenLastCalledWith('barberia-luis', 's1', undefined, undefined, expect.anything());
    await user.click(people.getByRole('button', { name: /Ana L\./ }));
    await waitFor(() => expect(api.getBookingSlots).toHaveBeenLastCalledWith('barberia-luis', 's1', undefined, 'p1', expect.anything()));
    expect(people.getByRole('button', { name: /Ana L\./ })).toHaveAttribute('aria-pressed', 'true');

    await user.click(await screen.findByRole('button', { name: '10:00 a. m.' }));
    await user.click(screen.getByRole('button', { name: 'Continuar · Lun 5 · 10:00 a. m.' }));
    const sheet = within(await screen.findByRole('dialog', { name: 'Tu cita' }));
    expect(sheet.getByText('Con Ana L.')).toBeInTheDocument();
    await user.type(sheet.getByLabelText('Tu nombre'), 'Valeria Ruiz');
    await user.type(sheet.getByLabelText('Teléfono (WhatsApp)'), '5522107765');
    await user.click(sheet.getByRole('button', { name: 'Pedir cita' }));

    await waitFor(() => expect(api.requestAppointment).toHaveBeenCalledWith('barberia-luis', expect.objectContaining({ staffId: 'p1' })));
    expect(await screen.findByText('Te atiende Ana L.')).toBeInTheDocument();
  });

  it('offers to book with whoever is free when the person chosen stops taking the service', async () => {
    const user = userEvent.setup();
    api.getBooking.mockResolvedValue(booking({
      staff: [{ id: 'p1', name: 'Ana L.' }],
      services: [{ id: 's1', name: 'Corte de cabello', price: 150, durationMinutes: 45, staffIds: ['p1'] }],
    }) as never);
    api.getBookingSlots.mockImplementation(((_slug: string, _service: string, _from?: string, staffId?: string) => staffId
      ? Promise.reject({ isAxiosError: true, response: { status: 404, data: { detail: 'Ese servicio no se agenda en línea.' } } })
      : Promise.resolve(week())) as never);
    renderPage();

    await user.click(await screen.findByRole('button', { name: /Corte de cabello/ }));
    await user.click(await screen.findByRole('button', { name: /Ana L\./ }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Ya no se agenda este servicio con Ana L. en línea.');
    await user.click(screen.getByRole('button', { name: 'Ver con quien esté libre' }));

    expect(await screen.findByRole('button', { name: '10:00 a. m.' })).toBeInTheDocument();
    // The button went away with the person: the choice it made keeps the focus.
    expect(screen.getByRole('button', { name: /Sin preferencia/ })).toHaveFocus();
  });

  it('keeps the server’s message and turns to no preference when the person chosen no longer takes it on sending', async () => {
    const user = userEvent.setup();
    const staff = [{ id: 'p1', name: 'Ana L.' }, { id: 'p2', name: 'Luis M.' }];
    api.getBooking
      .mockResolvedValueOnce(booking({ staff, services: [{ id: 's1', name: 'Corte de cabello', price: 150, durationMinutes: 45, staffIds: ['p1', 'p2'] }] }) as never)
      .mockResolvedValue(booking({ staff: [staff[1]], services: [{ id: 's1', name: 'Corte de cabello', price: 150, durationMinutes: 45, staffIds: ['p2'] }] }) as never);
    api.requestAppointment.mockRejectedValue({
      isAxiosError: true, response: { status: 400, data: { detail: 'Esa persona ya no agenda ese servicio en línea. Elige otra.' } },
    });
    renderPage();

    await user.click(await screen.findByRole('button', { name: /Corte de cabello/ }));
    await user.click(await screen.findByRole('button', { name: /Ana L\./ }));
    await user.click(await screen.findByRole('button', { name: '10:00 a. m.' }));
    await user.click(screen.getByRole('button', { name: 'Continuar · Lun 5 · 10:00 a. m.' }));
    const sheet = within(await screen.findByRole('dialog', { name: 'Tu cita' }));
    await user.type(sheet.getByLabelText('Tu nombre'), 'Valeria Ruiz');
    await user.type(sheet.getByLabelText('Teléfono (WhatsApp)'), '5522107765');
    await user.click(sheet.getByRole('button', { name: 'Pedir cita' }));

    expect(await screen.findByRole('status')).toHaveTextContent('Esa persona ya no agenda ese servicio en línea. Elige otra.');
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Tu cita' })).not.toBeInTheDocument());
    expect(screen.getByRole('button', { name: /Sin preferencia/ })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.queryByRole('button', { name: /Ana L\./ })).not.toBeInTheDocument();
  });

  it('turns back to the services with the server’s message when nobody takes the service any more', async () => {
    const user = userEvent.setup();
    api.getBooking
      .mockResolvedValueOnce(booking({
        staff: [{ id: 'p1', name: 'Ana L.' }],
        services: [{ id: 's1', name: 'Corte de cabello', price: 150, durationMinutes: 45, staffIds: ['p1'] }],
      }) as never)
      .mockResolvedValue(booking({ staff: [], services: [] }) as never);
    api.requestAppointment.mockRejectedValue({
      isAxiosError: true, response: { status: 400, data: { detail: 'Ese servicio no se agenda en línea.' } },
    });
    renderPage();

    await user.click(await screen.findByRole('button', { name: /Corte de cabello/ }));
    await user.click(await screen.findByRole('button', { name: /Ana L\./ }));
    await user.click(await screen.findByRole('button', { name: '10:00 a. m.' }));
    await user.click(screen.getByRole('button', { name: 'Continuar · Lun 5 · 10:00 a. m.' }));
    const sheet = within(await screen.findByRole('dialog', { name: 'Tu cita' }));
    await user.type(sheet.getByLabelText('Tu nombre'), 'Valeria Ruiz');
    await user.type(sheet.getByLabelText('Teléfono (WhatsApp)'), '5522107765');
    await user.click(sheet.getByRole('button', { name: 'Pedir cita' }));

    expect(await screen.findByRole('status')).toHaveTextContent('Ese servicio no se agenda en línea.');
    expect(screen.getByRole('heading', { name: 'Elige el servicio' })).toBeInTheDocument();
  });

  it('opens with the person of the link, or no preference when the link names nobody who does the service', async () => {
    const withPeople = booking({
      staff: [{ id: 'p1', name: 'Ana L.' }, { id: 'p2', name: 'Luis M.' }],
      services: [{ id: 's1', name: 'Corte de cabello', price: 150, durationMinutes: 45, staffIds: ['p1', 'p2'] }],
    });
    api.getBooking.mockResolvedValue(withPeople as never);
    const { unmount } = renderPage(undefined, '/m/barberia-luis/agendar?servicio=s1&con=p2');

    expect(await screen.findByRole('button', { name: /Luis M\./ })).toHaveAttribute('aria-pressed', 'true');
    await waitFor(() => expect(api.getBookingSlots).toHaveBeenLastCalledWith('barberia-luis', 's1', undefined, 'p2', expect.anything()));
    unmount();

    renderPage(undefined, '/m/barberia-luis/agendar?servicio=s1&con=nadie');
    expect(await screen.findByRole('button', { name: /Sin preferencia/ })).toHaveAttribute('aria-pressed', 'true');
    await waitFor(() => expect(api.getBookingSlots).toHaveBeenLastCalledWith('barberia-luis', 's1', undefined, undefined, expect.anything()));
  });

  it('says when the service a link chose is no longer booked, and offers the others (Fase 29I)', async () => {
    const user = userEvent.setup();
    api.getBooking.mockResolvedValue(booking() as never);
    renderPage(undefined, '/m/barberia-luis/agendar?servicio=ya-no');

    expect(await screen.findByText('Ese servicio ya no se agenda en línea: elige otro.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /^Corte de cabello/ }));

    expect(screen.queryByText(/ya no se agenda/)).not.toBeInTheDocument();
  });

  it('says so when the business does not book online', async () => {
    api.getBooking.mockRejectedValue({ isAxiosError: true, response: { status: 404, data: {} } });
    renderPage();

    expect(await screen.findByRole('heading', { name: 'Este negocio no agenda citas en línea' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver el menú' })).toHaveAttribute('href', '/m/barberia-luis');
  });
});
