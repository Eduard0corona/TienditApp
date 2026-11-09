import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { accountApi } from '../../api/accountApi';
import { HomeVisitChargePolicy, QuoteTime, ResponseTime } from '../../api/generated/enums';
import { publicMenuApi, type PublicBookingSlots, type PublicHomeVisit } from '../../api/publicMenuApi';
import { AuthContext, type AuthContextType } from '../../contexts/AuthContextDefinition';
import { resetLinkUploads } from '../../hooks/useProblemPhotoUploads';
import PublicHomeVisitPage from './PublicHomeVisitPage';

vi.mock('../../api/publicMenuApi', () => ({
  publicMenuApi: {
    getHomeVisit: vi.fn(), getHomeVisitCoverage: vi.fn(), getHomeVisitSlots: vi.fn(), requestHomeVisit: vi.fn(), addAppointmentPhoto: vi.fn(),
  },
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
    resetLinkUploads();
    window.localStorage.clear();
    api.getHomeVisit.mockResolvedValue(visit() as never);
    api.getHomeVisitCoverage.mockImplementation(async (_slug, postalCode) =>
      (postalCode === '72000' ? { postalCode, covered: true, price: 400 } : { postalCode, covered: false, price: null }) as never);
    api.getHomeVisitSlots.mockResolvedValue(week() as never);
  });

  it('says how soon the business answers, as its card in Explorar does (Fase 29K)', async () => {
    api.getHomeVisit.mockResolvedValue(visit({ responseTime: ResponseTime.WithinTwelveHours }) as never);
    renderPage();

    expect(await screen.findByText('Suele responder en menos de 12 horas')).toBeInTheDocument();
  });

  it('says nothing of how soon it answers without it', async () => {
    renderPage();

    expect(await screen.findByText('Visita y diagnóstico')).toBeInTheDocument();
    expect(screen.queryByText(/Suele responder/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Suele mandar la cotización/)).not.toBeInTheDocument();
  });

  it('says how soon the business sends the quote after the visit, as its card does (Fase 29S)', async () => {
    api.getHomeVisit.mockResolvedValue(visit({ quoteTime: QuoteTime.WithinADay }) as never);
    renderPage();

    expect(await screen.findByText('Suele mandar la cotización en menos de un día')).toBeInTheDocument();
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
      // What the page said, so a visit charged otherwise since is asked again.
      expectedChargePolicy: HomeVisitChargePolicy.CreditedIfHired,
      expectedPrice: 400,
      idempotencyKey: expect.any(String),
    });
    expect(await screen.findByRole('heading', { name: '¡Solicitud enviada!' })).toBeInTheDocument();
    expect(screen.getByText('A domicilio · CP 72000')).toBeInTheDocument();
    expect(screen.getByText(/Plomería Hernández te confirma por WhatsApp/)).toBeInTheDocument();
    await user.click(screen.getByRole('link', { name: 'Ver tu visita' }));
    expect(await screen.findByText(`En /cita#${LINK}`)).toBeInTheDocument();
  });

  it('keeps the sheet open while the request is on its way', async () => {
    const user = userEvent.setup();
    let answered: (value: unknown) => void = () => undefined;
    api.requestHomeVisit.mockReturnValue(new Promise((resolve) => { answered = resolve; }) as never);
    renderPage();

    await user.type(await screen.findByLabelText('Código postal'), '72000');
    await user.click(await screen.findByRole('button', { name: '10:00 a. m.' }));
    await user.click(screen.getByRole('button', { name: 'Continuar · Lun 5 · 10:00 a. m.' }));
    const sheet = await screen.findByRole('dialog', { name: 'Tu visita' });
    await user.type(within(sheet).getByLabelText('¿Qué pasa?'), 'Gotea el calentador');
    await user.type(within(sheet).getByLabelText('Dirección'), 'Av. Juárez 123, Col. Centro');
    await user.type(within(sheet).getByLabelText('Tu nombre'), 'Valeria Ruiz');
    await user.type(within(sheet).getByLabelText('Teléfono (WhatsApp)'), '5522107765');
    await user.click(within(sheet).getByRole('button', { name: 'Solicitar visita' }));

    expect(within(sheet).getByRole('button', { name: 'Enviando…' })).toBeDisabled();
    expect(within(sheet).queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(screen.getByRole('dialog', { name: 'Tu visita' })).toHaveClass('show');

    answered(receipt());
    expect(await screen.findByRole('heading', { name: '¡Solicitud enviada!' })).toBeInTheDocument();
  });

  it('can close while the times are read again after a refusal, and sends nothing else until they are', async () => {
    const user = userEvent.setup();
    const refused = { isAxiosError: true, response: { status: 400, data: { detail: 'Escribe un teléfono de 10 dígitos.' } } };
    api.requestHomeVisit.mockRejectedValue(refused);
    let reads = 0;
    let reread: (value: unknown) => void = () => undefined;
    api.getHomeVisitSlots.mockImplementation((() => (++reads === 1
      ? Promise.resolve(week())
      : new Promise((resolve) => { reread = resolve; }))) as never);
    renderPage();

    const sheet = await pickMondayAtTen(user);
    await user.type(sheet.getByLabelText('¿Qué pasa?'), 'Gotea el calentador');
    await user.type(sheet.getByLabelText('Dirección'), 'Av. Juárez 123');
    await user.type(sheet.getByLabelText('Tu nombre'), 'Valeria Ruiz');
    await user.type(sheet.getByLabelText('Teléfono (WhatsApp)'), '5522107765');
    await user.click(sheet.getByRole('button', { name: 'Solicitar visita' }));

    // Refused, nothing is on its way: the sheet can close while the times are read again, and does not send it again.
    await waitFor(() => expect(reads).toBe(2));
    expect(sheet.getByRole('button', { name: 'Solicitar visita' })).toBeDisabled();
    await user.click(sheet.getByRole('button', { name: 'Close' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Tu visita' })).not.toBeInTheDocument());
    expect(api.requestHomeVisit).toHaveBeenCalledTimes(1);

    // Still free: the sheet opens again ready to send it again.
    reread(week());
    await waitFor(() => expect(screen.getByRole('button', { name: 'Continuar · Lun 5 · 10:00 a. m.' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'Continuar · Lun 5 · 10:00 a. m.' }));
    const again = within(await screen.findByRole('dialog', { name: 'Tu visita' }));
    await waitFor(() => expect(again.getByRole('button', { name: 'Solicitar visita' })).toBeEnabled());
  });

  it('takes photos of what happens with the request, and sends them with its link once it is saved (Fase 29H)', async () => {
    const user = userEvent.setup();
    vi.spyOn(URL, 'createObjectURL').mockImplementation((file) => `blob:${(file as File).name}`);
    api.requestHomeVisit.mockResolvedValue(receipt() as never);
    api.addAppointmentPhoto
      .mockResolvedValueOnce({ problemPhotos: 1, canAddPhotos: true } as never)
      .mockResolvedValueOnce({ problemPhotos: 2, canAddPhotos: true } as never);
    const fuga = new File(['f'], 'fuga.jpg', { type: 'image/jpeg' });
    const pared = new File(['p'], 'pared.png', { type: 'image/png' });
    const otra = new File(['o'], 'otra.jpg', { type: 'image/jpeg' });
    renderPage();

    const sheet = await pickMondayAtTen(user);
    expect(sheet.getByText('Hasta 5 fotos de hasta 10 MB. Solo las ve Plomería Hernández.')).toBeInTheDocument();
    await user.upload(sheet.getByTestId('problem-photos-input'), [fuga, otra, pared]);
    expect(sheet.getAllByRole('img', { name: /de lo que pasa/ })).toHaveLength(3);
    await user.click(sheet.getByRole('button', { name: 'Quitar la foto 2' }));
    expect(sheet.getAllByRole('img', { name: /de lo que pasa/ }).map((img) => img.getAttribute('src'))).toEqual(['blob:fuga.jpg', 'blob:pared.png']);
    expect(sheet.getByRole('button', { name: 'Agregar más fotos' })).toBeEnabled();
    await user.type(sheet.getByLabelText('¿Qué pasa?'), 'Gotea el calentador');
    await user.type(sheet.getByLabelText('Dirección'), 'Av. Juárez 123, Col. Centro');
    await user.type(sheet.getByLabelText('Tu nombre'), 'Valeria Ruiz');
    await user.type(sheet.getByLabelText('Teléfono (WhatsApp)'), '5522107765');
    await user.click(sheet.getByRole('button', { name: 'Solicitar visita' }));

    const photos = within(await screen.findByRole('region', { name: 'Tus fotos de lo que pasa' }));
    expect(await photos.findByText('Listo: se mandaron 2 fotos.')).toBeInTheDocument();
    // One at a time, in the order picked, with the link of the request; never with the request itself.
    expect(api.addAppointmentPhoto.mock.calls).toEqual([[LINK, fuga], [LINK, pared]]);
    expect(api.requestHomeVisit.mock.calls[0][1]).not.toHaveProperty('photos');
    expect(photos.getAllByText('Enviada')).toHaveLength(2);
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
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Tu visita' })).not.toBeInTheDocument());
    expect(screen.queryByRole('button', { name: '10:00 a. m.' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Continuar/ })).not.toBeInTheDocument();
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
      { id: 'oficina', label: 'Oficina', postalCode: '72009', addressLine: 'Reforma 400', references: null },
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

    // The last one used comes first.
    expect(await screen.findByRole('radio', { name: /Casa/ })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByRole('radio', { name: /Oficina/ })).toHaveAttribute('aria-checked', 'false');
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

  it('starts at the account’s address once its session arrives after the page opened, unless something was typed', async () => {
    account.getAddresses.mockResolvedValue([
      { id: 'casa', label: 'Casa', postalCode: '72000', addressLine: 'Calle 5 de Mayo 8', references: null },
    ] as never);
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const tree = (auth: Partial<AuthContextType>) => (
      <QueryClientProvider client={queryClient}>
        <AuthContext.Provider value={auth as AuthContextType}>
          <MemoryRouter initialEntries={['/m/plomeria-hernandez/visita']}>
            <Routes><Route path="/m/:slug/visita" element={<PublicHomeVisitPage />} /></Routes>
          </MemoryRouter>
        </AuthContext.Provider>
      </QueryClientProvider>
    );
    const { rerender } = render(tree({ isAuthenticated: false, isLoading: true, user: null }));
    expect(await screen.findByLabelText('Código postal')).toBeInTheDocument();

    rerender(tree({
      isAuthenticated: true,
      isLoading: false,
      user: {
        id: 'u1', email: 'valeria@correo.mx', firstName: 'Valeria', lastName: 'Ruiz', fullName: 'Valeria Ruiz',
        phoneNumber: '+525522107765', phoneNumberConfirmed: true, emailConfirmed: true, role: 'Customer', permissions: [],
      } as unknown as AuthContextType['user'],
    }));

    expect(await screen.findByRole('radio', { name: /Casa/ })).toHaveAttribute('aria-checked', 'true');
    expect(screen.queryByLabelText('Código postal')).not.toBeInTheDocument();
  });

  it('asks again when the business changed what the visit costs since the page showed it', async () => {
    const user = userEvent.setup();
    api.requestHomeVisit.mockRejectedValue({ isAxiosError: true, response: { status: 400, data: {
      detail: 'El negocio cambió el costo de la visita o cómo la cobra. Revísalo antes de enviar tu solicitud.',
    } } });
    renderPage();

    const sheet = await pickMondayAtTen(user);
    await user.type(sheet.getByLabelText('¿Qué pasa?'), 'Gotea el calentador');
    await user.type(sheet.getByLabelText('Dirección'), 'Av. Juárez 123');
    await user.type(sheet.getByLabelText('Tu nombre'), 'Valeria Ruiz');
    await user.type(sheet.getByLabelText('Teléfono (WhatsApp)'), '5522107765');
    api.getHomeVisit.mockResolvedValue(visit({ chargePolicy: HomeVisitChargePolicy.Charged }) as never);
    await user.click(sheet.getByRole('button', { name: 'Solicitar visita' }));

    expect(await screen.findByText(/El negocio cambió el costo de la visita/)).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Tu visita' })).not.toBeInTheDocument());
    // The page reads the visit and its price again: the customer sees what it costs now.
    expect(await screen.findByText('Pagas la visita aunque no contrates el trabajo.')).toBeInTheDocument();
    expect(api.getHomeVisitCoverage.mock.calls.filter(([, code]) => code === '72000').length).toBeGreaterThan(1);
  });

  it('never says the visit is paid while it costs nothing', async () => {
    api.getHomeVisit.mockResolvedValue(visit({ chargePolicy: HomeVisitChargePolicy.Charged, price: { amount: 0, isStarting: false } }) as never);
    renderPage();

    expect(await screen.findByText(/Visita sin costo/)).toBeInTheDocument();
    expect(screen.getByText('Revisamos y te decimos cuánto cuesta, sin costo.')).toBeInTheDocument();
    expect(screen.queryByText('Pagas la visita aunque no contrates el trabajo.')).not.toBeInTheDocument();
  });

  it('says so when the business does not receive visits online', async () => {
    api.getHomeVisit.mockRejectedValue({ isAxiosError: true, response: { status: 404, data: {} } });
    renderPage();

    expect(await screen.findByRole('heading', { name: 'Este negocio no recibe visitas en línea' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Ver la tienda' })).toHaveAttribute('href', '/m/plomeria-hernandez');
  });
});
