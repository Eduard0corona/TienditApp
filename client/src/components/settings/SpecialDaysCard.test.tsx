import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest, type Schemas } from '../../api/typedClient';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import {
  DayOfWeek,
  OnlineMenuMode,
  SpecialDayKind,
  TenantCapability,
  type OpeningIntervalDto,
  type TenantProfileDto,
} from '../../types';
import SpecialDaysCard from './SpecialDaysCard';

vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: vi.fn() }));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

type SpecialDay = Schemas['SpecialDayDto'];
type Method = 'get' | 'post' | 'put' | 'delete';

const toast = { success: vi.fn(), warning: vi.fn(), error: vi.fn(), info: vi.fn() };
const confirm = vi.fn();
const PATH = '/api/v1/auth/my-tenant/special-days';

// Monday with a break, Friday into the night, closed on Sunday.
const weekly: OpeningIntervalDto[] = [
  { day: DayOfWeek.Monday, opens: '09:00', closes: '14:00' },
  { day: DayOfWeek.Monday, opens: '16:00', closes: '20:00' },
  ...[DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Saturday].map((day) => ({ day, opens: '09:00', closes: '20:00' })),
  { day: DayOfWeek.Friday, opens: '18:00', closes: '02:00' },
];

const christmas: SpecialDay = {
  id: 'navidad', from: '2026-12-25', to: '2026-12-25', kind: SpecialDayKind.Closed, openings: [], reason: 'Navidad', revision: 1, inProgress: false,
};
const christmasEve: SpecialDay = {
  id: 'nochebuena', from: '2026-12-24', to: '2026-12-24', kind: SpecialDayKind.SpecialHours,
  openings: [{ opens: '09:00', closes: '18:00' }], reason: 'Nochebuena', revision: 1, inProgress: false,
};
const vacations: SpecialDay = {
  id: 'vacaciones', from: '2026-12-21', to: '2027-01-04', kind: SpecialDayKind.Closed, openings: [], reason: 'Vacaciones', revision: 2, inProgress: false,
};
// Started on Friday the 9th: today (Sunday the 11th) is one of its days.
const inventory: SpecialDay = {
  id: 'inventario', from: '2026-10-09', to: '2026-10-15', kind: SpecialDayKind.Closed, openings: [], reason: 'Inventario', revision: 3, inProgress: true,
};

const suggestions: Schemas['HolidaySuggestionDto'][] = [
  { date: '2026-11-02', name: 'Día de Muertos', statutory: false, kind: SpecialDayKind.Closed, openings: [] },
  { date: '2026-11-16', name: 'Día de la Revolución', statutory: true, kind: SpecialDayKind.Closed, openings: [] },
  { date: '2026-12-12', name: 'Día de la Virgen de Guadalupe', statutory: false, kind: SpecialDayKind.Closed, openings: [] },
  { date: '2026-12-31', name: 'Fin de año', statutory: false, kind: SpecialDayKind.SpecialHours, openings: [{ opens: '09:00', closes: '18:00' }] },
];

function specialDays(overrides: Partial<Schemas['SpecialDaysDto']> = {}): Schemas['SpecialDaysDto'] {
  return {
    today: '2026-10-11',
    hasWeeklyHours: true,
    upcoming: [],
    past: [],
    suggestions: [],
    upcomingDates: 0,
    maxUpcomingDates: 120,
    maxDaysPerPeriod: 92,
    maxDaysAhead: 366,
    maxReasonLength: 40,
    ...overrides,
  };
}

function withTenant(onlineMenuMode: OnlineMenuMode = OnlineMenuMode.Orders, openingHours: OpeningIntervalDto[] | null = weekly) {
  vi.mocked(useTenant).mockReturnValue({
    tenant: { id: 't1', name: 'Taquería Lupita', slug: 'lupita', onlineMenuMode, timeZone: 'America/Mexico_City', openingHours } as TenantProfileDto,
    capabilities: TenantCapability.Food,
    isLoading: false,
    refreshTenant: vi.fn(),
  });
}

/** Answers each method of the API with its function; one without an answer fails the test. */
function serve(responses: Partial<Record<Method, () => Promise<unknown>>>) {
  vi.mocked(apiRequest).mockImplementation(((method: Method) => {
    const respond = responses[method];
    return respond ? respond() : Promise.reject(new Error(`Sin respuesta para ${method}`));
  }) as unknown as typeof apiRequest);
}

function calls(method: Method) {
  return vi.mocked(apiRequest).mock.calls.filter(([called]) => called === method);
}

function apiError(status: number, code: string, detail: string): AxiosError {
  const headers = new AxiosHeaders();
  const response = { status, statusText: '', headers, config: { headers }, data: { code, detail } } as AxiosResponse;
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, response);
}

function renderCard() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <SpecialDaysCard />
    </QueryClientProvider>,
  );
}

async function openNew() {
  await userEvent.click(await screen.findByRole('button', { name: 'Agregar día especial' }));
  return screen.getByRole('dialog', { name: 'Nuevo día especial' });
}

describe('SpecialDaysCard', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    vi.mocked(useConfirm).mockReturnValue({ confirm, prompt: vi.fn() });
    withTenant();
  });

  it('says it is loading, then invites to mark the first special day', async () => {
    let answer: (value: unknown) => void = () => undefined;
    serve({ get: () => new Promise((resolve) => { answer = resolve; }) });
    renderCard();

    expect(screen.getByText('Cargando…')).toBeInTheDocument();
    answer(specialDays());

    expect(await screen.findByText(/Aún no tienes días especiales/)).toHaveTextContent(
      'Aún no tienes días especiales. Marca los días que cierras (16 de noviembre, Navidad, vacaciones) o abres con otro horario.',
    );
    expect(screen.getByText(/Tu menú en línea y tus respuestas automáticas de WhatsApp los toman en cuenta\./)).toHaveTextContent(
      'en la hora de tu negocio: Centro (CDMX, Guadalajara, Monterrey, Mérida).',
    );
    expect(screen.getByText('En los días que cierras, tu menú no recibe pedidos, ni siquiera desde el QR de una mesa.')).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Próximos días especiales' })).not.toBeInTheDocument();
    expect(screen.queryByText('Próximos festivos')).not.toBeInTheDocument();
    expect(screen.queryByText('Pasados (últimos 90 días)')).not.toBeInTheDocument();
    expect(screen.queryByText(/Sin horario de la semana/)).not.toBeInTheDocument();
  });

  it('offers to try again when the special days cannot be read', async () => {
    serve({ get: () => Promise.reject(apiError(500, 'server_error', 'Ocurrió un error inesperado.')) });
    renderCard();

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudieron cargar tus días especiales.');

    serve({ get: () => Promise.resolve(specialDays({ upcoming: [christmas], upcomingDates: 1 })) });
    await userEvent.click(screen.getByRole('button', { name: 'Reintentar' }));

    expect(await screen.findByRole('list', { name: 'Próximos días especiales' })).toHaveTextContent('Vie 25 dic');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('lists the special days ahead with actions that name their dates, and the past ones apart', async () => {
    serve({
      get: () => Promise.resolve(specialDays({
        upcoming: [inventory, christmasEve, christmas],
        past: [{ ...christmas, id: 'independencia', from: '2026-09-16', to: '2026-09-16', reason: 'Día de la Independencia' }],
        upcomingDates: 7,
      })),
    });
    renderCard();

    const list = await screen.findByRole('list', { name: 'Próximos días especiales' });
    const rows = within(list).getAllByRole('listitem');
    expect(rows).toHaveLength(3);
    expect(rows[0]).toHaveTextContent('Vie 9 oct al jue 15 oct');
    expect(within(rows[0]).getByText('En curso')).toHaveClass('ta-chip', 'ta-chip-preparing');
    expect(rows[1]).toHaveTextContent('Jue 24 dic');
    expect(rows[1]).toHaveTextContent('9:00 a 18:00 · Nochebuena');
    expect(rows[2]).toHaveTextContent('Vie 25 dic');
    expect(rows[2]).toHaveTextContent('Cerrado · Navidad');
    expect(within(rows[2]).queryByText('En curso')).not.toBeInTheDocument();
    expect(within(rows[2]).getByRole('button', { name: 'Editar viernes 25 de diciembre' })).toHaveTextContent('Editar');
    expect(within(rows[2]).getByRole('button', { name: 'Quitar viernes 25 de diciembre' })).toHaveTextContent('Quitar');
    expect(within(rows[0]).getByRole('button', { name: 'Quitar del viernes 9 de octubre al jueves 15 de octubre' })).toBeInTheDocument();

    // History is read only: it shows what was, without actions.
    expect(screen.getByText('Pasados (últimos 90 días)').closest('details')).not.toHaveAttribute('open');
    const past = screen.getByRole('list', { name: 'Días especiales pasados', hidden: true });
    expect(past).toHaveTextContent('Mié 16 sep · Cerrado · Día de la Independencia');
    expect(within(past).queryByRole('button', { hidden: true })).not.toBeInTheDocument();
  });

  it('adds a closed day and tells the menu shows it', async () => {
    serve({
      get: () => Promise.resolve(specialDays()),
      post: () => Promise.resolve(specialDays({ upcoming: [christmas], upcomingDates: 1 })),
    });
    renderCard();

    const dialog = await openNew();
    fireEvent.change(within(dialog).getByLabelText('Fecha'), { target: { value: '2026-12-25' } });
    expect(within(dialog).getByLabelText('Cierro todo el día')).toBeChecked();
    expect(within(dialog).getByText('Tus clientes verán: Cerrado')).toBeInTheDocument();
    await userEvent.type(within(dialog).getByLabelText('Motivo (opcional)'), 'Navidad');
    expect(within(dialog).getByLabelText('Motivo (opcional)')).toHaveAttribute('maxLength', '40');
    expect(within(dialog).getByText('Lo ve cualquiera que abra tu menú. Máximo 40 caracteres.')).toBeInTheDocument();
    expect(within(dialog).getByText('Tus clientes verán: Cerrado por Navidad')).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));

    expect(apiRequest).toHaveBeenCalledWith('post', PATH, {
      body: { from: '2026-12-25', to: null, kind: SpecialDayKind.Closed, openings: [], reason: 'Navidad' },
    });
    expect(toast.success).toHaveBeenCalledWith('Día especial guardado: tu menú en línea ya lo muestra.');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    // The answer is the list as it is now: no second read.
    expect(screen.getByRole('list', { name: 'Próximos días especiales' })).toHaveTextContent('Vie 25 dic');
    expect(calls('get')).toHaveLength(1);
  });

  it('opens with other hours, starting from the hours of that weekday', async () => {
    serve({ get: () => Promise.resolve(specialDays()), post: () => Promise.resolve(specialDays()) });
    renderCard();

    const dialog = await openNew();
    // Monday the 21st: the weekly Monday has a break.
    fireEvent.change(within(dialog).getByLabelText('Fecha'), { target: { value: '2026-12-21' } });
    await userEvent.click(within(dialog).getByLabelText('Abro con otro horario'));
    expect(within(dialog).getByLabelText('Abre ese día')).toHaveValue('09:00');
    expect(within(dialog).getByLabelText('Cierra ese día')).toHaveValue('14:00');
    expect(within(dialog).getByLabelText('Abre ese día, segundo horario')).toHaveValue('16:00');
    expect(within(dialog).queryByRole('button', { name: 'Agregar otro horario' })).not.toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText('Cierra ese día, segundo horario'), { target: { value: '18:00' } });
    await userEvent.type(within(dialog).getByLabelText('Motivo (opcional)'), 'Posada');
    expect(within(dialog).getByText('Tus clientes verán: 9:00 a 14:00 y 16:00 a 18:00 · Posada')).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));

    expect(apiRequest).toHaveBeenCalledWith('post', PATH, {
      body: {
        from: '2026-12-21',
        to: null,
        kind: SpecialDayKind.SpecialHours,
        openings: [{ opens: '09:00', closes: '14:00' }, { opens: '16:00', closes: '18:00' }],
        reason: 'Posada',
      },
    });
  });

  it('marks several days and says how long they last', async () => {
    serve({ get: () => Promise.resolve(specialDays()), post: () => Promise.resolve(specialDays({ upcoming: [vacations] })) });
    renderCard();

    const dialog = await openNew();
    fireEvent.change(within(dialog).getByLabelText('Fecha'), { target: { value: '2026-12-21' } });
    expect(within(dialog).queryByLabelText('Hasta')).not.toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('switch', { name: 'Varios días' }));
    expect(within(dialog).getByLabelText('Hasta')).toHaveValue('2026-12-27');
    expect(within(dialog).getByText('7 días: del lunes 21 al domingo 27 de diciembre.')).toBeInTheDocument();
    // At most 92 days, and never past a year from today.
    expect(within(dialog).getByLabelText('Hasta')).toHaveAttribute('max', '2027-03-22');
    fireEvent.change(within(dialog).getByLabelText('Hasta'), { target: { value: '2027-01-04' } });
    expect(within(dialog).getByText('15 días: del lunes 21 de diciembre al lunes 4 de enero.')).toBeInTheDocument();
    await userEvent.type(within(dialog).getByLabelText('Motivo (opcional)'), 'Vacaciones');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));

    expect(apiRequest).toHaveBeenCalledWith('post', PATH, {
      body: { from: '2026-12-21', to: '2027-01-04', kind: SpecialDayKind.Closed, openings: [], reason: 'Vacaciones' },
    });
  });

  it('checks blanks and order before asking the API', async () => {
    serve({ get: () => Promise.resolve(specialDays()) });
    renderCard();

    const dialog = await openNew();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Elige la fecha.');

    fireEvent.change(within(dialog).getByLabelText('Fecha'), { target: { value: '2026-12-21' } });
    await userEvent.click(within(dialog).getByRole('switch', { name: 'Varios días' }));
    fireEvent.change(within(dialog).getByLabelText('Hasta'), { target: { value: '2026-12-20' } });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));
    expect(within(dialog).getByRole('alert')).toHaveTextContent('La fecha final no puede ser antes de la inicial.');

    await userEvent.click(within(dialog).getByRole('switch', { name: 'Varios días' }));
    // Sunday is closed every week: other hours start at 9:00 to 20:00, and a second one starts blank.
    fireEvent.change(within(dialog).getByLabelText('Fecha'), { target: { value: '2026-12-27' } });
    expect(within(dialog).getByText('Ese día ya cierras por tu horario de la semana.')).toBeInTheDocument();
    await userEvent.click(within(dialog).getByLabelText('Abro con otro horario'));
    expect(within(dialog).queryByText('Ese día ya cierras por tu horario de la semana.')).not.toBeInTheDocument();
    expect(within(dialog).getByLabelText('Abre ese día')).toHaveValue('09:00');
    expect(within(dialog).getByLabelText('Cierra ese día')).toHaveValue('20:00');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Agregar otro horario' }));
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Escribe a qué hora abres y cierras ese día.');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Quitar el segundo horario' }));
    expect(within(dialog).queryByLabelText('Abre ese día, segundo horario')).not.toBeInTheDocument();

    expect(calls('post')).toHaveLength(0);
  });

  it('warns that the night before keeps hours that run past midnight', async () => {
    serve({ get: () => Promise.resolve(specialDays()) });
    renderCard();

    const dialog = await openNew();
    // Saturday the 26th: on Friday the business opens from 18:00 to 2:00.
    fireEvent.change(within(dialog).getByLabelText('Fecha'), { target: { value: '2026-12-26' } });
    expect(within(dialog).getByText(
      'El viernes tu horario termina después de medianoche y se respeta. Para cambiarlo, marca también ese día.',
    )).toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText('Fecha'), { target: { value: '2026-12-25' } });
    expect(within(dialog).queryByText(/termina después de medianoche/)).not.toBeInTheDocument();
  });

  it('changes a special day that already started: its first date stays and the revision read goes with it', async () => {
    serve({
      get: () => Promise.resolve(specialDays({ upcoming: [inventory], upcomingDates: 5 })),
      put: () => Promise.resolve(specialDays({ upcoming: [{ ...inventory, to: '2026-10-13', revision: 4 }] })),
    });
    renderCard();

    await userEvent.click(await screen.findByRole('button', { name: 'Editar del viernes 9 de octubre al jueves 15 de octubre' }));
    const dialog = screen.getByRole('dialog', { name: 'Editar día especial' });
    expect(within(dialog).getByLabelText('Fecha')).toBeDisabled();
    expect(within(dialog).getByLabelText('Fecha')).toHaveValue('2026-10-09');
    expect(within(dialog).getByText('Ya empezó: puedes cambiar hasta cuándo dura, el horario y el motivo.')).toBeInTheDocument();
    expect(within(dialog).getByRole('switch', { name: 'Varios días' })).toBeChecked();
    expect(within(dialog).getByLabelText('Motivo (opcional)')).toHaveValue('Inventario');
    fireEvent.change(within(dialog).getByLabelText('Hasta'), { target: { value: '2026-10-13' } });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));

    expect(apiRequest).toHaveBeenCalledWith('put', `${PATH}/{id}`, {
      path: { id: 'inventario' },
      body: { revision: 3, from: '2026-10-09', to: '2026-10-13', kind: SpecialDayKind.Closed, openings: [], reason: 'Inventario' },
    });
    expect(toast.success).toHaveBeenCalled();
  });

  it('shows why the API refuses a special day and keeps the dialog open', async () => {
    serve({
      get: () => Promise.resolve(specialDays({ upcoming: [christmas], upcomingDates: 1 })),
      post: () => Promise.reject(apiError(422, 'domain_rule', 'El viernes 25 de diciembre ya tiene un día especial: cámbialo o quítalo primero.')),
    });
    renderCard();

    const dialog = await openNew();
    fireEvent.change(within(dialog).getByLabelText('Fecha'), { target: { value: '2026-12-25' } });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'El viernes 25 de diciembre ya tiene un día especial: cámbialo o quítalo primero.',
    );
    expect(screen.getByRole('dialog', { name: 'Nuevo día especial' })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Guardar' })).toBeEnabled();
    expect(toast.success).not.toHaveBeenCalled();
    expect(calls('get')).toHaveLength(1);
  });

  it('reads the list again when someone else changed the special day meanwhile', async () => {
    serve({
      get: () => Promise.resolve(specialDays({ upcoming: [christmas], upcomingDates: 1 })),
      put: () => Promise.reject(apiError(409, 'concurrency_conflict', 'Alguien más modificó este registro al mismo tiempo. Recarga e intenta de nuevo.')),
    });
    renderCard();

    await userEvent.click(await screen.findByRole('button', { name: 'Editar viernes 25 de diciembre' }));
    const dialog = screen.getByRole('dialog', { name: 'Editar día especial' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'Alguien más modificó este registro al mismo tiempo. Recarga e intenta de nuevo.',
    );
    await waitFor(() => expect(calls('get')).toHaveLength(2));
  });

  it('opens a suggested holiday filled in, without saving it', async () => {
    withTenant(OnlineMenuMode.Off);
    serve({
      get: () => Promise.resolve(specialDays({ suggestions })),
      post: () => Promise.resolve(specialDays()),
    });
    renderCard();

    const section = await screen.findByRole('region', { name: 'Próximos festivos' });
    expect(within(section).getAllByRole('listitem')).toHaveLength(3);
    expect(within(section).getByText('Lun 16 nov · Día de la Revolución')).toBeInTheDocument();
    expect(within(section).getByText('Cerrado todo el día · descanso obligatorio')).toBeInTheDocument();
    expect(within(section).getByText('Son sugerencias: nada cambia hasta que guardes.')).toBeInTheDocument();
    await userEvent.click(within(section).getByRole('button', { name: 'Ver 1 más' }));
    expect(within(section).getAllByRole('listitem')).toHaveLength(4);
    expect(within(section).getByText('9:00 a 18:00')).toBeInTheDocument();
    await userEvent.click(within(section).getByRole('button', { name: 'Ver menos' }));
    expect(within(section).getAllByRole('listitem')).toHaveLength(3);
    await userEvent.click(within(section).getByRole('button', { name: 'Ver 1 más' }));

    await userEvent.click(within(section).getByRole('button', { name: 'Agregar Fin de año, jueves 31 de diciembre' }));
    const dialog = screen.getByRole('dialog', { name: 'Nuevo día especial' });
    expect(within(dialog).getByLabelText('Fecha')).toHaveValue('2026-12-31');
    expect(within(dialog).getByLabelText('Abro con otro horario')).toBeChecked();
    expect(within(dialog).getByLabelText('Abre ese día')).toHaveValue('09:00');
    expect(within(dialog).getByLabelText('Cierra ese día')).toHaveValue('18:00');
    expect(within(dialog).getByLabelText('Motivo (opcional)')).toHaveValue('Fin de año');
    expect(calls('post')).toHaveLength(0);

    await userEvent.click(within(dialog).getByRole('button', { name: 'Guardar' }));
    // Without an online menu the toast says nothing about it.
    expect(toast.success).toHaveBeenCalledWith('Día especial guardado.');
  });

  it('removes a special day after confirming what comes back', async () => {
    confirm.mockResolvedValue(true);
    serve({
      get: () => Promise.resolve(specialDays({ upcoming: [christmas], upcomingDates: 1 })),
      delete: () => Promise.resolve(specialDays()),
    });
    renderCard();

    await userEvent.click(await screen.findByRole('button', { name: 'Quitar viernes 25 de diciembre' }));

    expect(confirm).toHaveBeenCalledWith({
      title: 'Quitar el día especial',
      message: 'El viernes 25 de diciembre vuelve tu horario de siempre.',
      confirmLabel: 'Quitar día especial',
      danger: true,
    });
    expect(apiRequest).toHaveBeenCalledWith('delete', `${PATH}/{id}`, { path: { id: 'navidad' }, query: { revision: 1 } });
    expect(toast.success).toHaveBeenCalledWith('Día especial quitado.');
    expect(await screen.findByText(/Aún no tienes días especiales/)).toBeInTheDocument();
  });

  it('says what comes back for several days and for one that already started, and keeps it when cancelled', async () => {
    confirm.mockResolvedValue(false);
    serve({ get: () => Promise.resolve(specialDays({ upcoming: [inventory, vacations], upcomingDates: 20 })) });
    renderCard();

    await userEvent.click(await screen.findByRole('button', { name: 'Quitar del lunes 21 de diciembre al lunes 4 de enero' }));
    expect(confirm).toHaveBeenLastCalledWith(expect.objectContaining({
      message: 'Del 21 de diciembre al 4 de enero vuelve tu horario de siempre.',
      danger: true,
    }));
    await userEvent.click(screen.getByRole('button', { name: 'Quitar del viernes 9 de octubre al jueves 15 de octubre' }));
    expect(confirm).toHaveBeenLastCalledWith(expect.objectContaining({
      message: 'Desde hoy vuelve tu horario de siempre; los días que ya pasaron se quedan en Pasados.',
    }));

    expect(calls('delete')).toHaveLength(0);
  });

  it('says that without weekly hours the menu closes only on these days', async () => {
    withTenant(OnlineMenuMode.MenuOnly, null);
    serve({ get: () => Promise.resolve(specialDays({ hasWeeklyHours: false })) });
    renderCard();

    expect(await screen.findByText('Sin horario de la semana, tu menú solo se ve cerrado en estos días.')).toBeInTheDocument();
    // A menu that takes no orders has nothing to say about them.
    expect(screen.queryByText(/no recibe pedidos/)).not.toBeInTheDocument();

    const dialog = await openNew();
    fireEvent.change(within(dialog).getByLabelText('Fecha'), { target: { value: '2026-12-27' } });
    expect(within(dialog).queryByText('Ese día ya cierras por tu horario de la semana.')).not.toBeInTheDocument();
    await userEvent.click(within(dialog).getByLabelText('Abro con otro horario'));
    expect(within(dialog).getByLabelText('Abre ese día')).toHaveValue('09:00');
    expect(within(dialog).getByLabelText('Cierra ese día')).toHaveValue('20:00');
    expect(within(dialog).getByText('Tus clientes verán: 9:00 a 20:00 · horario especial')).toBeInTheDocument();
  });
});
