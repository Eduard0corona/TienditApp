import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { platformApi, type PlatformTenant } from '../../api/platformApi';
import PlatformConsole from './PlatformConsole';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../components/platform/FakeRequestsWatch', () => ({ default: () => null }));
vi.mock('../../components/platform/TenantBillingModal', () => ({
  default: ({ tenant, onClose }: { tenant: PlatformTenant; onClose: () => void }) => (
    <div role="dialog" aria-label={`Cobranza de ${tenant.name}`}>
      <p>{tenant.planName ?? 'Sin plan'}</p>
      <button type="button" onClick={onClose}>Cerrar</button>
    </div>
  ),
}));
vi.mock('../../api/platformApi', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/platformApi')>();
  return { ...actual, platformApi: { getOverview: vi.fn(), getPlans: vi.fn(), getTenants: vi.fn() } };
});

const api = vi.mocked(platformApi);

const refused = (message: string, status = 400) => ({ isAxiosError: true, response: { status, data: { error: message } } });

function deferred() {
  let resolve: (value: unknown) => void = () => undefined;
  const promise = new Promise((res) => {
    resolve = res;
  });
  return { promise, resolve };
}

const tenantsPage = (items: PlatformTenant[], pageNumber = 1, totalPages = 1) => ({
  items, pageNumber, totalPages, totalCount: totalPages > 1 ? 25 : items.length, hasPreviousPage: pageNumber > 1,
  hasNextPage: pageNumber < totalPages,
}) as never;

const tenant = (over: Partial<PlatformTenant>): PlatformTenant => ({
  tenantId: 't-luna', name: 'Café Luna', businessType: 0, slug: 'cafe-luna', contactEmail: 'hola@cafeluna.mx', planId: 'p-pro',
  planName: 'Pro', subscriptionStatus: 1, tenantStatus: 0, paidThrough: '2026-11-30T18:00:00Z', trialEndsAt: null,
  branchesCount: 2, usersCount: 5, lastOrderAt: '2026-11-09T18:00:00Z', ...over,
}) as PlatformTenant;

/** A business paying Pro, one in its trial, one suspended without a subscription and one without limits. */
const LUNA = tenant({});
const TALLER = tenant({
  tenantId: 't-taller', name: 'Taller Pech', businessType: 2, slug: 'taller-pech', contactEmail: null, planName: 'Básico',
  subscriptionStatus: 0, paidThrough: null, trialEndsAt: '2026-11-20T18:00:00Z', branchesCount: 1, usersCount: 1, lastOrderAt: null,
});
const CERRADO = tenant({
  tenantId: 't-cerrado', name: 'Abarrotes Cerrado', businessType: 1, slug: 'cerrado', planName: null, subscriptionStatus: undefined,
  tenantStatus: 1,
});
const LIBRE = tenant({ tenantId: 't-libre', name: 'Fonda Libre', slug: 'fonda-libre', planName: null, subscriptionStatus: undefined });

const OVERVIEW = {
  totalTenants: 12, activeTenants: 10, inTrial: 3, trialsEndingIn7Days: 1, paying: 6, monthlyRecurringRevenue: 2394, pastDue: 1,
  tenantsWithoutAccess: 2, suspended: 1, cancelled: 1, paymentsLast30Days: 3590, withoutPlan: 2,
};

const lastQuery = () => api.getTenants.mock.calls.at(-1)?.[0];

/** Waits past the pause after typing in the search. */
const pause = () => act(() => new Promise((resolve) => setTimeout(resolve, 350)));

function renderConsole() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<QueryClientProvider client={client}><PlatformConsole /></QueryClientProvider>);
  return { client };
}

describe('PlatformConsole', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    api.getOverview.mockResolvedValue(OVERVIEW as never);
    api.getPlans.mockResolvedValue([{ id: 'p-basic', name: 'Básico' }, { id: 'p-pro', name: 'Pro' }] as never);
    api.getTenants.mockResolvedValue(tenantsPage([LUNA, TALLER, CERRADO, LIBRE]));
  });

  it('sums up the businesses and lists each one with its plan, its state and when it is due', async () => {
    renderConsole();

    const cards = await screen.findByText('Negocios', { selector: '.text-muted' });
    expect(cards.parentElement).toHaveTextContent('Negocios1210 con acceso');
    expect(screen.getByText('Pago vencido', { selector: '.text-muted' }).closest('.card')).toHaveClass('border-warning');
    expect(screen.getByText('Sin acceso').parentElement).toHaveTextContent('Sin acceso21 suspendidos · 1 cancelados');

    const luna = (await screen.findByText('Café Luna')).closest('tr') as HTMLElement;
    expect(luna).toHaveTextContent('Alimentos · cafe-luna · hola@cafeluna.mx');
    expect(within(luna).getByText('Al corriente')).toBeInTheDocument();
    expect(within(luna).getByText(/^Pagado al /)).toBeInTheDocument();
    const taller = screen.getByText('Taller Pech').closest('tr') as HTMLElement;
    expect(taller).toHaveTextContent('Servicios · taller-pech');
    expect(within(taller).getByText('En prueba')).toBeInTheDocument();
    expect(within(taller).getByText(/^Prueba al /)).toBeInTheDocument();
    const cerrado = screen.getByText('Abarrotes Cerrado').closest('tr') as HTMLElement;
    expect(within(cerrado).getByText('Sin plan')).toBeInTheDocument();
    expect(within(cerrado).getByText('Suspendido')).toBeInTheDocument();
    expect(within(cerrado).getByText('—')).toBeInTheDocument();
    expect(within(screen.getByText('Fonda Libre').closest('tr') as HTMLElement).getByText('Sin límites')).toBeInTheDocument();
    expect(lastQuery()).toEqual({ search: undefined, withoutPlan: undefined, subscriptionStatus: undefined, planId: undefined, pageNumber: 1, pageSize: 20 });
  });

  it('opens the billing of a business with the row as the list has it now', async () => {
    const user = userEvent.setup();
    const { client } = renderConsole();
    const luna = (await screen.findByText('Café Luna')).closest('tr') as HTMLElement;

    await user.click(within(luna).getByRole('button', { name: 'Cobranza' }));
    const billing = screen.getByRole('dialog', { name: 'Cobranza de Café Luna' });
    expect(billing).toHaveTextContent('Pro');

    // An action in the billing reads the list again: the dialog follows the business as it comes back.
    api.getTenants.mockResolvedValue(tenantsPage([{ ...LUNA, planName: 'Empresa' }, TALLER]));
    await act(async () => client.invalidateQueries({ queryKey: ['platform', 'tenants'] }));
    await waitFor(() => expect(screen.getByRole('dialog', { name: 'Cobranza de Café Luna' })).toHaveTextContent('Empresa'));

    await user.click(screen.getByRole('button', { name: 'Cerrar' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('asks for the search when typing stops, from the first page', async () => {
    const user = userEvent.setup();
    api.getTenants.mockImplementation((async (query: { search?: string; pageNumber?: number }) => (
      query.search ? tenantsPage([LUNA], query.pageNumber ?? 1, 2) : tenantsPage([LUNA, TALLER], query.pageNumber ?? 1, 2)
    )) as never);
    renderConsole();
    await screen.findByText('Taller Pech');
    await user.click(screen.getByRole('button', { name: 'Siguiente' }));
    await waitFor(() => expect(lastQuery()?.pageNumber).toBe(2));
    const reads = api.getTenants.mock.calls.length;

    // The API takes up to 100 characters.
    expect(screen.getByLabelText('Buscar')).toHaveAttribute('maxLength', '100');
    await user.type(screen.getByLabelText('Buscar'), '  luna ');
    // Nothing is asked for with each key.
    expect(api.getTenants.mock.calls.length).toBe(reads);
    await pause();
    expect(api.getTenants.mock.calls.length).toBe(reads + 1);
    expect(lastQuery()).toEqual(expect.objectContaining({ search: 'luna', pageNumber: 1 }));
    await waitFor(() => expect(screen.queryByText('Taller Pech')).not.toBeInTheDocument());

    // Typing and erasing before the pause asks for nothing, and keeps the page.
    await user.click(screen.getByRole('button', { name: 'Siguiente' }));
    await waitFor(() => expect(lastQuery()).toEqual(expect.objectContaining({ search: 'luna', pageNumber: 2 })));
    const searched = api.getTenants.mock.calls.length;
    await user.type(screen.getByLabelText('Buscar'), 'x');
    await user.type(screen.getByLabelText('Buscar'), '{Backspace}');
    await pause();
    expect(api.getTenants.mock.calls.length).toBe(searched);
    expect(screen.getByText('Página 2 de 2 · 25 negocios')).toBeInTheDocument();
  });

  it('filters by state, without a plan or by plan, from the first page', async () => {
    const user = userEvent.setup();
    api.getTenants.mockImplementation((async (query: { pageNumber?: number }) => tenantsPage([LUNA], query.pageNumber ?? 1, 2)) as never);
    renderConsole();
    await screen.findByText('Página 1 de 2 · 25 negocios');
    await user.click(screen.getByRole('button', { name: 'Siguiente' }));
    await screen.findByText('Página 2 de 2 · 25 negocios');

    await user.selectOptions(screen.getByLabelText('Estado'), 'Pago vencido');
    await waitFor(() => expect(lastQuery()).toEqual(expect.objectContaining({ subscriptionStatus: 2, withoutPlan: undefined, pageNumber: 1 })));
    await screen.findByText('Página 1 de 2 · 25 negocios');
    await user.click(screen.getByRole('button', { name: 'Siguiente' }));
    await screen.findByText('Página 2 de 2 · 25 negocios');
    await user.selectOptions(screen.getByLabelText('Estado'), 'Sin plan');
    await waitFor(() => expect(lastQuery()).toEqual(expect.objectContaining({ subscriptionStatus: undefined, withoutPlan: true, pageNumber: 1 })));
    await screen.findByText('Página 1 de 2 · 25 negocios');
    await user.click(screen.getByRole('button', { name: 'Siguiente' }));
    await screen.findByText('Página 2 de 2 · 25 negocios');
    await user.selectOptions(screen.getByLabelText('Plan'), 'Pro');
    await waitFor(() => expect(lastQuery()).toEqual(expect.objectContaining({ planId: 'p-pro', withoutPlan: true, pageNumber: 1 })));
  });

  it('marks the rows of the filter before as busy while the new one is read', async () => {
    const user = userEvent.setup();
    const reading = deferred();
    api.getTenants.mockResolvedValueOnce(tenantsPage([LUNA, TALLER])).mockReturnValueOnce(reading.promise as never);
    renderConsole();
    await screen.findByText('Taller Pech');
    expect(screen.getByRole('table')).not.toHaveAttribute('aria-busy');

    await user.selectOptions(screen.getByLabelText('Estado'), 'En prueba');
    expect(screen.getByRole('table')).toHaveAttribute('aria-busy', 'true');
    expect(screen.getByText('Café Luna')).toBeInTheDocument();

    await act(async () => reading.resolve(tenantsPage([TALLER])));
    await waitFor(() => expect(screen.queryByText('Café Luna')).not.toBeInTheDocument());
    expect(screen.getByRole('table')).not.toHaveAttribute('aria-busy');
  });

  it('does not go past the last page with a second click while the next one is read', async () => {
    const user = userEvent.setup();
    const second = deferred();
    api.getTenants.mockImplementation(((query: { pageNumber?: number }) => {
      const pageNumber = query.pageNumber ?? 1;
      if (pageNumber === 2) return second.promise;
      return Promise.resolve(pageNumber > 2 ? tenantsPage([], pageNumber, 2) : tenantsPage([LUNA], 1, 2));
    }) as never);
    renderConsole();
    await screen.findByText('Página 1 de 2 · 25 negocios');

    await user.click(screen.getByRole('button', { name: 'Siguiente' }));
    expect(screen.getByRole('button', { name: 'Siguiente' })).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Siguiente' }));
    await act(async () => second.resolve(tenantsPage([TALLER], 2, 2)));
    expect(await screen.findByText('Página 2 de 2 · 25 negocios')).toBeInTheDocument();
    expect(api.getTenants.mock.calls.map(([query]) => query.pageNumber)).toEqual([1, 2]);
  });

  it('reads the page before when the one in view is left empty, instead of saying there are none', async () => {
    const user = userEvent.setup();
    let emptied = false;
    api.getTenants.mockImplementation((async (query: { pageNumber?: number }) => {
      const pageNumber = query.pageNumber ?? 1;
      if (emptied) return pageNumber === 2 ? tenantsPage([], 2, 1) : tenantsPage([TALLER], 1, 1);
      return tenantsPage(pageNumber === 2 ? [LUNA] : [TALLER], pageNumber, 2);
    }) as never);
    const { client } = renderConsole();
    await screen.findByText('Página 1 de 2 · 25 negocios');
    await user.click(screen.getByRole('button', { name: 'Siguiente' }));
    await screen.findByText('Café Luna');

    // A billing action takes the only business of the last page out of the filter.
    emptied = true;
    await act(async () => client.invalidateQueries({ queryKey: ['platform', 'tenants'] }));
    expect(await screen.findByText('Taller Pech')).toBeInTheDocument();
    expect(lastQuery()).toEqual(expect.objectContaining({ pageNumber: 1 }));
    expect(screen.queryByText('No hay negocios con esos filtros.')).not.toBeInTheDocument();
    expect(screen.queryByText('Todavía no hay negocios.')).not.toBeInTheDocument();
  });

  it('does not say there are none for a filter before its answer comes', async () => {
    const user = userEvent.setup();
    const reading = deferred();
    api.getTenants.mockResolvedValueOnce(tenantsPage([])).mockReturnValueOnce(reading.promise as never);
    renderConsole();
    await screen.findByText('Todavía no hay negocios.');

    await user.selectOptions(screen.getByLabelText('Estado'), 'Cancelado');
    expect(screen.queryByText('No hay negocios con esos filtros.')).not.toBeInTheDocument();
    expect(screen.queryByText('Todavía no hay negocios.')).not.toBeInTheDocument();
    await act(async () => reading.resolve(tenantsPage([])));
    expect(await screen.findByText('No hay negocios con esos filtros.')).toBeInTheDocument();
  });

  it('says there are no businesses yet, or none with the filters', async () => {
    const user = userEvent.setup();
    api.getTenants.mockResolvedValue(tenantsPage([]));
    renderConsole();

    expect(await screen.findByText('Todavía no hay negocios.')).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText('Estado'), 'Cancelado');
    expect(await screen.findByText('No hay negocios con esos filtros.')).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText('Estado'), 'Todos');
    expect(await screen.findByText('Todavía no hay negocios.')).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText('Plan'), 'Pro');
    expect(await screen.findByText('No hay negocios con esos filtros.')).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText('Plan'), 'Todos');
    expect(await screen.findByText('Todavía no hay negocios.')).toBeInTheDocument();
    await user.type(screen.getByLabelText('Buscar'), 'zz');
    expect(await screen.findByText('No hay negocios con esos filtros.')).toBeInTheDocument();
  });

  it('keeps what was read when reading it again fails, and waits for the retry', async () => {
    const user = userEvent.setup();
    const retry = deferred();
    api.getTenants.mockResolvedValueOnce(tenantsPage([LUNA]))
      .mockRejectedValueOnce(refused('Se perdió la conexión.'))
      .mockReturnValueOnce(retry.promise as never);
    const { client } = renderConsole();
    await screen.findByText('Café Luna');

    await act(async () => client.invalidateQueries({ queryKey: ['platform', 'tenants'] }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Se perdió la conexión.');
    expect(screen.getByText('Café Luna')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Reintentar' }));
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeDisabled();
    await act(async () => retry.resolve(tenantsPage([LUNA])));
    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument());
  });

  it('keeps the summary read when reading it again fails, and waits for the retry', async () => {
    const user = userEvent.setup();
    const retry = deferred();
    api.getOverview.mockResolvedValueOnce(OVERVIEW as never)
      .mockRejectedValueOnce(refused('Se perdió la conexión.'))
      .mockReturnValueOnce(retry.promise as never);
    const { client } = renderConsole();
    await screen.findByText('10 con acceso');

    await act(async () => client.invalidateQueries({ queryKey: ['platform', 'overview'] }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Se perdió la conexión.');
    expect(screen.getByText('10 con acceso')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Reintentar' }));
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeDisabled();
    await act(async () => retry.resolve(OVERVIEW));
    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument());
  });

  it('announces the loading of the businesses', async () => {
    api.getTenants.mockReturnValue(new Promise(() => undefined) as never);
    renderConsole();

    expect(await screen.findByText('Cargando los negocios…')).toBeInTheDocument();
    expect(screen.getByText('Cargando los negocios…').closest('[role="status"]')).not.toBeNull();
  });

  it('says what could not be read, and reads it again', async () => {
    const user = userEvent.setup();
    api.getOverview.mockRejectedValueOnce(refused('No se pudo leer el resumen de la plataforma.')).mockResolvedValue(OVERVIEW as never);
    api.getTenants.mockRejectedValueOnce(refused('No se pudieron leer los negocios de la plataforma.'))
      .mockResolvedValue(tenantsPage([LUNA]));
    renderConsole();

    await waitFor(() => expect(screen.getAllByRole('alert')).toHaveLength(2));
    const [overviewAlert, tenantsAlert] = screen.getAllByRole('alert');
    expect(overviewAlert).toHaveTextContent('No se pudo leer el resumen de la plataforma.');
    expect(tenantsAlert).toHaveTextContent('No se pudieron leer los negocios de la plataforma.');
    expect(screen.queryByText('Todavía no hay negocios.')).not.toBeInTheDocument();

    await user.click(within(overviewAlert).getByRole('button', { name: 'Reintentar' }));
    expect(await screen.findByText('10 con acceso')).toBeInTheDocument();
    await user.click(within(screen.getByRole('alert')).getByRole('button', { name: 'Reintentar' }));
    expect(await screen.findByText('Café Luna')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
