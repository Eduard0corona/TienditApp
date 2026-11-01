import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { accountApi } from '../../api/accountApi';
import { HomeVisitChargePolicy } from '../../api/generated/enums';
import { publicMenuApi, type PublicBookingSlots, type PublicHomeVisit } from '../../api/publicMenuApi';
import { AuthContext, type AuthContextType } from '../../contexts/AuthContextDefinition';
import PublicHomeVisitPage from './PublicHomeVisitPage';

vi.mock('../../api/publicMenuApi', () => ({
  publicMenuApi: { getHomeVisit: vi.fn(), getHomeVisitCoverage: vi.fn(), getHomeVisitSlots: vi.fn(), requestHomeVisit: vi.fn() },
}));
vi.mock('../../api/accountApi', () => ({ accountApi: { getAddresses: vi.fn() } }));

const api = vi.mocked(publicMenuApi);
const account = vi.mocked(accountApi);
const LINK = 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE';
const MONDAY_TEN = '2026-10-05T16:00:00+00:00';
const MONDAY_ELEVEN = '2026-10-05T17:00:00+00:00';
const MONDAY = { date: '2026-10-05', dateLabel: 'lunes 5 de octubre' };

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

function visit(overrides: Partial<PublicHomeVisit> = {}): PublicHomeVisit {
  return {
    businessName: 'Plomería Hernández',
    slug: 'plomeria-hernandez',
    theme: 0,
    pricesIncludeTax: true,
    daysAhead: 30,
    visitName: 'Visita y diagnóstico',
    durationMinutes: 60,
    chargePolicy: HomeVisitChargePolicy.CreditedIfHired,
    price: { amount: 350, isStarting: true },
    staff: [],
    ...overrides,
  } as PublicHomeVisit;
}

function week(overrides: Partial<PublicBookingSlots> = {}): PublicBookingSlots {
  return {
    days: [
      { date: '2026-10-04', label: 'domingo 4 de octubre', slots: [] },
      {
        date: '2026-10-05',
        label: 'lunes 5 de octubre',
        slots: [{ startAt: MONDAY_TEN, label: '10:00 a. m.', ...MONDAY }, { startAt: MONDAY_ELEVEN, label: '11:00 a. m.', ...MONDAY }],
      },
    ],
    nextFrom: '2026-10-11',
    ...overrides,
  };
}

function receipt(overrides: Record<string, unknown> = {}) {
  return {
    serviceName: 'Visita y diagnóstico',
    startAt: MONDAY_TEN,
    endAt: '2026-10-05T17:00:00+00:00',
    dateLabel: 'lunes 5 de octubre',
    timeLabel: '10:00 a. m.',
    businessName: 'Plomería Hernández',
    contactPhone: null,
    address: null,
    visit: { postalCode: '72000', chargePolicy: HomeVisitChargePolicy.CreditedIfHired },
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

function renderPage(auth?: Partial<AuthContextType>, entry = '/m/plomeria-hernandez/visita') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const page = (
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="/m/:slug/visita" element={<PublicHomeVisitPage />} />
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

/** The postal code typed, Monday at 10:00 picked and the sheet open. */
async function pickMondayAtTen(user: ReturnType<typeof userEvent.setup>) {
  await user.type(await screen.findByLabelText('Código postal'), '72000');
  await user.click(await screen.findByRole('button', { name: '10:00 a. m.' }));
  await user.click(screen.getByRole('button', { name: 'Continuar · Lun 5 · 10:00 a. m.' }));
  return within(await screen.findByRole('dialog', { name: 'Tu visita' }));
}

describe('PublicHomeVisitPage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    window.localStorage.clear();
    api.getHomeVisit.mockResolvedValue(visit() as never);
    api.getHomeVisitCoverage.mockImplementation(async (_slug, postalCode) =>
      (postalCode === '72000' ? { postalCode, covered: true, price: 400 } : { postalCode, covered: false, price: null }) as never);
    api.getHomeVisitSlots.mockResolvedValue(week() as never);
  });

  it('asks for a visit at a free time as a guest, where the business goes, and gives its link', async () => {
    const user = userEvent.setup();
    api.requestHomeVisit.mockResolvedValue(receipt() as never);
    renderPage();

    expect(await screen.findByText('Visita y diagnóstico')).toBeInTheDocument();
    expect(screen.getByText('Visita desde $350.00, se descuenta si contratas')).toBeInTheDocument();
    expect(screen.getByText('Si contratas el trabajo, lo que pagaste de la visita se descuenta.')).toBeInTheDocument();
    expect(document.title).toBe('Solicita una visita de Plomería Hernández · TienditApp');
    // Nothing to pick until the business goes there.
    expect(screen.queryByRole('heading', { name: 'Elige el día' })).not.toBeInTheDocument();
    expect(api.getHomeVisitSlots).not.toHaveBeenCalled();

    await user.type(screen.getByLabelText('Código postal'), '72000');
    expect(await screen.findByText('Vamos al 72000 · Visita $400.00, se descuenta si contratas')).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: /Lun 5.*2 horarios.*lunes 5 de octubre/ })).toHaveAttribute('aria-pressed', 'true');
    expect(api.getHomeVisitSlots).toHaveBeenCalledWith('plomeria-hernandez', undefined, undefined, expect.anything());
    await user.click(screen.getByRole('button', { name: '10:00 a. m.' }));
    await user.click(screen.getByRole('button', { name: 'Continuar · Lun 5 · 10:00 a. m.' }));

    const sheet = within(await screen.findByRole('dialog', { name: 'Tu visita' }));
    expect(sheet.getByText('A domicilio · CP 72000 · Visita $400.00, se descuenta si contratas')).toBeInTheDocument();
    await user.type(sheet.getByLabelText('¿Qué pasa?'), 'Gotea el calentador');
    await user.type(sheet.getByLabelText('Dirección'), 'Av. Juárez 123, Col. Centro');
    await user.type(sheet.getByLabelText('Tu nombre'), 'Valeria Ruiz');
    await user.type(sheet.getByLabelText('Teléfono (WhatsApp)'), '5522107765');
    await user.click(sheet.getByRole('button', { name: 'Solicitar visita' }));

    await waitFor(() => expect(api.requestHomeVisit).toHaveBeenCalledTimes(1));
    expect(api.requestHomeVisit).toHaveBeenCalledWith('plomeria-hernandez', {
      startAt: MONDAY_TEN,
      postalCode: '72000',
      addressLine: 'Av. Juárez 123, Col. Centro',
      references: null,
      problem: 'Gotea el calentador',
      customerName: 'Valeria Ruiz',
      customerPhone: '5522107765',
      customerEmail: null,
      staffId: null,
      idempotencyKey: expect.any(String),
    });
    expect(await screen.findByRole('heading', { name: '¡Solicitud enviada!' })).toBeInTheDocument();
    expect(screen.getByText('A domicilio · CP 72000')).toBeInTheDocument();
    expect(screen.getByText(/Plomería Hernández te confirma por WhatsApp/)).toBeInTheDocument();
    await user.click(screen.getByRole('link', { name: 'Ver tu visita' }));
    expect(await screen.findByText(`En /cita#${LINK}`)).toBeInTheDocument();
  });

  it('says so when the business does not go to the postal code, and offers no times', async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(await screen.findByLabelText('Código postal'), '72009');

    expect(await screen.findByText('Por ahora no vamos al 72009.')).toBeInTheDocument();
    expect(screen.getByLabelText('Código postal')).toHaveClass('is-invalid');
    expect(screen.queryByRole('heading', { name: 'Elige el día' })).not.toBeInTheDocument();
    expect(api.getHomeVisitSlots).not.toHaveBeenCalled();
  });

  it('sends the same request again with its key when the answer was lost, and asks for another time once it was taken', async () => {
    const user = userEvent.setup();
    const lost = { isAxiosError: true, response: { status: 503, data: {} } };
    const taken = { isAxiosError: true, response: { status: 400, data: { detail: 'Esa hora ya no está libre. Elige otra.' } } };
    api.requestHomeVisit.mockRejectedValueOnce(lost).mockRejectedValueOnce(taken);
    renderPage();

    const sheet = await pickMondayAtTen(user);
    await user.type(sheet.getByLabelText('¿Qué pasa?'), 'Gotea el calentador');
    await user.type(sheet.getByLabelText('Dirección'), 'Av. Juárez 123');
    await user.type(sheet.getByLabelText('Tu nombre'), 'Valeria Ruiz');
    await user.type(sheet.getByLabelText('Teléfono (WhatsApp)'), '5522107765');
    api.getHomeVisitSlots.mockResolvedValue(week({
      days: [{ date: '2026-10-05', label: 'lunes 5 de octubre', slots: [{ startAt: MONDAY_ELEVEN, label: '11:00 a. m.', ...MONDAY }] }],
    }) as never);
    await user.click(sheet.getByRole('button', { name: 'Solicitar visita' }));
    expect(await sheet.findByRole('alert')).toHaveTextContent('No se pudo enviar tu solicitud. Intenta de nuevo. Si ya se había enviado, no se repite.');

    await user.click(sheet.getByRole('button', { name: 'Solicitar visita' }));

    await waitFor(() => expect(api.requestHomeVisit).toHaveBeenCalledTimes(2));
    const [first, second] = api.requestHomeVisit.mock.calls.map((call) => call[1]);
    expect(second.idempotencyKey).toBe(first.idempotencyKey);
    // Taken meanwhile: the sheet closes and the times say it.
    expect(await screen.findByText('Esa hora ya no está libre. Elige otra.')).toBeInTheDocument();
    expect(screen.queryByRole('dialog', { name: 'Tu visita' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '10:00 a. m.' })).not.toBeInTheDocument();
  });

  it('lets the customer choose who goes, with that person’s times', async () => {
    const user = userEvent.setup();
    api.getHomeVisit.mockResolvedValue(visit({ staff: [{ id: 'ana', name: 'Ana L.' }] }) as never);
    api.requestHomeVisit.mockResolvedValue(receipt({ staffName: 'Ana L.' }) as never);
    renderPage();

    await user.type(await screen.findByLabelText('Código postal'), '72000');
    const people = await screen.findByRole('group', { name: 'Quién va' });
    await user.click(within(people).getByRole('button', { name: /Ana L\./ }));
    await waitFor(() => expect(api.getHomeVisitSlots).toHaveBeenLastCalledWith('plomeria-hernandez', undefined, 'ana', expect.anything()));
    await user.click(await screen.findByRole('button', { name: '10:00 a. m.' }));
    await user.click(screen.getByRole('button', { name: 'Continuar · Lun 5 · 10:00 a. m.' }));
    const sheet = within(await screen.findByRole('dialog', { name: 'Tu visita' }));

    expect(sheet.getByText('Va Ana L.')).toBeInTheDocument();
  });

  it('starts at the account’s last address, with its name, phone and email', async () => {
    const user = userEvent.setup();
    account.getAddresses.mockResolvedValue([
      { id: 'casa', label: 'Casa', postalCode: '72000', addressLine: 'Calle 5 de Mayo 8', references: 'Portón verde' },
    ] as never);
    api.requestHomeVisit.mockResolvedValue(receipt({ linkedToAccount: true }) as never);
    renderPage({
      isAuthenticated: true,
      isLoading: false,
      user: {
        id: 'u1', email: 'valeria@correo.mx', firstName: 'Valeria', lastName: 'Ruiz', fullName: 'Valeria Ruiz',
        phoneNumber: '+525522107765', phoneNumberConfirmed: true, emailConfirmed: true, role: 'Customer', permissions: [],
      } as unknown as AuthContextType['user'],
    });

    expect(await screen.findByRole('radio', { name: /Casa/ })).toHaveAttribute('aria-checked', 'true');
    expect(screen.queryByLabelText('Código postal')).not.toBeInTheDocument();
    await user.click(await screen.findByRole('button', { name: '10:00 a. m.' }));
    await user.click(screen.getByRole('button', { name: 'Continuar · Lun 5 · 10:00 a. m.' }));
    const sheet = within(await screen.findByRole('dialog', { name: 'Tu visita' }));
    expect(sheet.getByLabelText('Dirección')).toHaveValue('Calle 5 de Mayo 8');
    expect(sheet.getByLabelText('Correo (opcional)')).toHaveValue('valeria@correo.mx');
    await user.type(sheet.getByLabelText('¿Qué pasa?'), 'No prende el boiler');
    await user.click(sheet.getByRole('button', { name: 'Solicitar visita' }));

    await waitFor(() => expect(api.requestHomeVisit).toHaveBeenCalledWith('plomeria-hernandez', expect.objectContaining({
      postalCode: '72000', addressLine: 'Calle 5 de Mayo 8', references: 'Portón verde', customerEmail: 'valeria@correo.mx',
    })));
    expect(await screen.findByText('La encuentras en tus citas.')).toBeInTheDocument();
  });

  it('says so when the business does not receive visits online', async () => {
    api.getHomeVisit.mockRejectedValue({ isAxiosError: true, response: { status: 404, data: {} } });
    renderPage();

    expect(await screen.findByRole('heading', { name: 'Este negocio no recibe visitas en línea' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver la tienda' })).toHaveAttribute('href', '/m/plomeria-hernandez');
  });
});
