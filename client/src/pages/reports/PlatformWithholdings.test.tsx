import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { PlatformWithholdingCertificate, PlatformWithholdingPeriod, PlatformWithholdings as Withholdings } from '../../api/platformWithholdings';
import { apiRequest } from '../../api/typedClient';
import { useAuth } from '../../hooks/useAuth';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { OrderChannel, TenantCapability, type TenantProfileDto } from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import PlatformWithholdings from './PlatformWithholdings';

vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: vi.fn() }));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

type Options = { query?: Record<string, unknown>; path?: Record<string, string>; body?: Record<string, unknown> };

const toast = { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() };
const prompt = vi.fn();

function period(month: number | null, patch: Partial<PlatformWithholdingPeriod> = {}): PlatformWithholdingPeriod {
  return { month, orderCount: 0, salesWithoutTax: 0, calculatedIsr: 0, calculatedVat: 0, certificateCount: 0, coveredFromMonth: null, ...patch };
}

function certificate(patch: Partial<PlatformWithholdingCertificate> = {}): PlatformWithholdingCertificate {
  return {
    id: 'c1',
    channel: OrderChannel.UberEats,
    uuid: '7874d580-da5b-558f-b8ec-d92c57197242',
    issuerRfc: 'UBV121024TN8',
    issuerName: 'UBER B.V.',
    year: 2026,
    monthFrom: 9,
    monthTo: 9,
    issuedOn: '2026-10-02T22:28:50',
    operationTotal: 16269.76,
    isrBase: 16269.76,
    isrWithheld: 406.74,
    isrRatePercent: 2.5,
    vatBase: 2603.16,
    vatWithheld: 1301.58,
    vatRatePercent: 50,
    totalWithheld: 1708.32,
    createdAt: '2026-10-03T18:00:00Z',
    removedAt: null,
    removalReason: null,
    ...patch,
  };
}

// September of Uber Eats: 2 orders calculated at $32.33 of ISR and $103.45 of IVA; its CFDI says $406.74 and $1,301.58.
// August had orders and no CFDI; July a removed one.
function withholdings(patch: Partial<Withholdings> = {}): Withholdings {
  const months = Array.from({ length: 12 }, (_, index) => period(index + 1));
  months[7] = period(8, { orderCount: 1, salesWithoutTax: 862.07, calculatedIsr: 21.55, calculatedVat: 68.97 });
  months[8] = period(9, {
    orderCount: 2, salesWithoutTax: 1293.11, calculatedIsr: 32.33, calculatedVat: 103.45,
    certificateCount: 1, certificateOperationTotal: 16269.76, certificateIsr: 406.74, certificateVat: 1301.58,
    isrDifference: 374.41, vatDifference: 1198.13,
  });
  return {
    channel: OrderChannel.UberEats,
    year: 2026,
    isrWithholdingPercent: 2.5,
    vatWithholdingPercent: 50,
    months,
    // The year's difference only over September, the month with a CFDI.
    total: period(null, {
      orderCount: 3, calculatedIsr: 53.88, calculatedVat: 172.42,
      certificateCount: 1, certificateIsr: 406.74, certificateVat: 1301.58, isrDifference: 374.41, vatDifference: 1198.13,
    }),
    certificates: [
      certificate({ id: 'c0', uuid: '22222222-2222-4333-8444-555555555555', monthFrom: 7, monthTo: 7, removedAt: '2026-10-04T18:00:00Z', removalReason: 'Era de Rappi' }),
      certificate(),
    ],
    certificatesTruncated: false,
    ...patch,
  };
}

function serve(answer: (query: Record<string, unknown>) => Withholdings | Promise<Withholdings>) {
  vi.mocked(apiRequest).mockImplementation(((method: string, path: string, options: Options = {}) => {
    if (method === 'get' && path === '/api/v1/platform-withholdings') return Promise.resolve(answer(options.query ?? {}));
    if (method === 'post' && path === '/api/v1/platform-withholdings/{id}/remove') {
      return Promise.resolve(certificate({ removedAt: '2026-10-22T18:00:00Z', removalReason: String(options.body?.reason) }));
    }
    return Promise.reject(new Error(`Sin respuesta para ${method} ${path}`));
  }) as unknown as typeof apiRequest);
}

function calls(method: string, path: string) {
  return vi.mocked(apiRequest).mock.calls.filter(([m, p]) => m === method && p === path).map(([, , options]) => options as Options);
}

function signIn(role: string, permissions: string[] = []) {
  vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role, permissions } } as unknown as ReturnType<typeof useAuth>);
}

function renderPage(url = '/reports/settlements/withholdings?channel=4&year=2026') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <MemoryRouter initialEntries={[url]}>
      <QueryClientProvider client={queryClient}>
        <PlatformWithholdings />
      </QueryClientProvider>
    </MemoryRouter>,
  );
}

function row(table: HTMLElement, name: string) {
  return within(table).getByRole('rowheader', { name: new RegExp(`^${name}`) }).closest('tr') as HTMLElement;
}

describe('PlatformWithholdings', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-22T18:00:00Z'));
    setBusinessTimeZone('America/Mexico_City');
    signIn('TenantAdmin');
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
    vi.mocked(useConfirm).mockReturnValue({ confirm: vi.fn(), prompt });
    vi.mocked(useTenant).mockReturnValue({
      tenant: { id: 't1', name: 'Cocina Lupita', slug: 'lupita', timeZone: 'America/Mexico_City' } as TenantProfileDto,
      capabilities: TenantCapability.Food,
      isLoading: false,
      refreshTenant: vi.fn(),
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    setBusinessTimeZone(null);
  });

  it('compares each month of the year so far with its CFDI, and totals the year', async () => {
    serve(() => withholdings());
    renderPage();

    const table = await screen.findByRole('table', { name: 'Retenciones de Uber Eats por mes de 2026' });
    // Up to October, the month of the business: November and December have not come.
    expect(within(table).getAllByRole('rowheader').map((cell) => cell.textContent)).toEqual([
      'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio', 'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Total',
    ]);
    const september = within(row(table, 'Septiembre')).getAllByRole('cell').map((cell) => cell.textContent);
    expect(september).toEqual(['2', '$32.33', '$406.74', '+$374.41', '$103.45', '$1,301.58', '+$1,198.13']);
    expect(within(table).getAllByRole('columnheader').map((header) => header.textContent)).toEqual([
      'Mes', 'Pedidos', 'ISR calculado', 'ISR del CFDI', 'Diferencia de ISR', 'IVA calculado', 'IVA del CFDI', 'Diferencia de IVA',
    ]);
    // A month with orders and without a CFDI shows dashes, not $0.
    expect(within(row(table, 'Agosto')).getAllByRole('cell').map((cell) => cell.textContent))
      .toEqual(['1', '$21.55', '—', '—', '$68.97', '—', '—']);
    expect(within(row(table, 'Total')).getAllByRole('cell')[0]).toHaveTextContent('3');
    expect(screen.getByText(/calcula los pedidos nuevos con 2\.50 % de ISR y 50\.00 % del IVA/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Configuración › Negocio › Plataformas de reparto' })).toHaveAttribute('href', '/settings/business');
    expect(calls('get', '/api/v1/platform-withholdings')[0].query).toEqual({ channel: OrderChannel.UberEats, year: 2026 });
  });

  it('marks a month that adds up, and one that compares the months of a CFDI of several months', async () => {
    const data = withholdings();
    data.months[8] = { ...data.months[8], isrDifference: 0, vatDifference: 0, coveredFromMonth: 7 };
    serve(() => data);
    renderPage();

    const september = row(await screen.findByRole('table', { name: /Retenciones de Uber Eats/ }), 'Septiembre');
    expect(within(september).getAllByText('Cuadra')).toHaveLength(2);
    expect(within(september).getByText('Compara de julio a septiembre')).toBeInTheDocument();
    // The note sits in the CFDI's cell, not in the row's header that names every cell.
    expect(within(september).getByRole('rowheader')).toHaveTextContent(/^Septiembre$/);
  });

  it('lists the CFDI of the year with their rates, the removed one with its reason', async () => {
    serve(() => withholdings());
    renderPage();

    const removed = (await screen.findByText('Julio de 2026')).closest('tr') as HTMLElement;
    expect(within(removed).getByText('Quitado')).toBeInTheDocument();
    expect(within(removed).getByText('Era de Rappi')).toBeInTheDocument();
    expect(within(removed).queryByRole('button', { name: /^Quitar/ })).not.toBeInTheDocument();
    const inForce = screen.getByText('Septiembre de 2026').closest('tr') as HTMLElement;
    expect(within(inForce).getByText('UBER B.V. · folio 7874D580')).toBeInTheDocument();
    expect(within(inForce).getByText('2.50 %')).toBeInTheDocument();
    expect(within(inForce).getByText('50.00 % del IVA')).toBeInTheDocument();
  });

  it('removes a CFDI with the reason the owner writes', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    serve(() => withholdings());
    prompt.mockResolvedValue('Era de DiDi');
    renderPage();

    await user.click(await screen.findByRole('button', { name: 'Quitar el CFDI de septiembre de 2026, folio 7874D580' }));

    expect(prompt).toHaveBeenCalledWith(expect.objectContaining({ title: 'Quitar CFDI', required: true, maxLength: 500 }));
    await waitFor(() => expect(calls('post', '/api/v1/platform-withholdings/{id}/remove')).toEqual([
      expect.objectContaining({ path: { id: 'c1' }, body: { reason: 'Era de DiDi' } }),
    ]));
    expect(toast.success).toHaveBeenCalledWith('CFDI de septiembre de 2026 quitado.');
    // The reconciliation is read again.
    await waitFor(() => expect(calls('get', '/api/v1/platform-withholdings').length).toBeGreaterThan(1));
  });

  it('reads another platform or year from the filters, without passing the last one for it meanwhile', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    let release: (() => void) | undefined;
    serve((query) => {
      const answer = withholdings({ channel: query.channel as OrderChannel, year: query.year as number });
      if (query.channel !== OrderChannel.DidiFood) return answer;
      const didi = { ...answer, certificates: [] };
      return query.year === 2026 ? new Promise((resolve) => { release = () => resolve(didi); }) : didi;
    });
    renderPage();
    await screen.findByRole('heading', { name: 'Uber Eats en 2026' });

    await user.selectOptions(screen.getByLabelText('Plataforma'), String(OrderChannel.DidiFood));

    // While DiDi arrives, Uber's figures stay named as Uber's, and none of its CFDI can be removed.
    expect(await screen.findByText('Cargando DiDi Food de 2026…')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Uber Eats en 2026' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Quitar el CFDI de septiembre de 2026/ })).toBeDisabled();

    release?.();
    expect(await screen.findByRole('heading', { name: 'DiDi Food en 2026' })).toBeInTheDocument();
    expect(screen.queryByText('Cargando DiDi Food de 2026…')).not.toBeInTheDocument();
    expect(screen.getByText('Todavía no subes CFDI de retenciones de DiDi Food de 2026.')).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('Año'), '2025');
    expect(await screen.findByRole('heading', { name: 'DiDi Food en 2025' })).toBeInTheDocument();
    expect(calls('get', '/api/v1/platform-withholdings').at(-1)?.query).toEqual({ channel: OrderChannel.DidiFood, year: 2025 });
    // The whole of a past year shows.
    expect(screen.getByRole('rowheader', { name: 'Diciembre' })).toBeInTheDocument();
  });

  it('falls back to the first platform and this year for what the link does not say right', async () => {
    serve((query) => withholdings({ channel: query.channel as OrderChannel, year: query.year as number }));
    renderPage('/reports/settlements/withholdings?channel=1&year=1999');

    await screen.findByRole('table', { name: 'Retenciones de Rappi por mes de 2026' });
    expect(calls('get', '/api/v1/platform-withholdings')[0].query).toEqual({ channel: OrderChannel.Rappi, year: 2026 });
  });

  it('takes the year of the business on the last night of December', async () => {
    // 2027-01-01 03:00 UTC is still December 31 in Mexico City: 2026, whole.
    vi.setSystemTime(new Date('2027-01-01T03:00:00Z'));
    serve((query) => withholdings({ year: query.year as number }));
    renderPage('/reports/settlements/withholdings?channel=4');

    expect(await screen.findByRole('rowheader', { name: 'Diciembre' })).toBeInTheDocument();
    expect(calls('get', '/api/v1/platform-withholdings')[0].query).toEqual({ channel: OrderChannel.UberEats, year: 2026 });
  });

  it('opens the upload from its button, and says when the list does not bring every CFDI', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    serve(() => withholdings({ certificatesTruncated: true }));
    renderPage();

    expect(await screen.findByText('La lista muestra los primeros 200 CFDI; los meses cuentan todos.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Subir CFDI de retenciones' }));
    expect(screen.getByRole('dialog', { name: 'Subir CFDI de retenciones' })).toBeInTheDocument();
  });

  it('removes nothing when the owner cancels, and says why the server refused', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    serve(() => withholdings());
    renderPage();
    const quitar = await screen.findByRole('button', { name: /^Quitar el CFDI de septiembre de 2026/ });

    prompt.mockResolvedValueOnce(null);
    await user.click(quitar);
    expect(calls('post', '/api/v1/platform-withholdings/{id}/remove')).toEqual([]);

    const headers = new AxiosHeaders();
    vi.mocked(apiRequest).mockRejectedValueOnce(new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, {
      status: 409, statusText: '', headers, config: { headers }, data: { code: 'concurrency_conflict', detail: 'x' },
    } as AxiosResponse));
    prompt.mockResolvedValueOnce('Duplicado');
    await user.click(quitar);
    await waitFor(() => expect(toast.error).toHaveBeenCalled());
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('lets whoever reads the reports see it, without uploading or removing', async () => {
    signIn('TenantEmployee', ['ViewReports']);
    serve(() => withholdings());
    renderPage();

    await screen.findByRole('table', { name: /Retenciones de Uber Eats/ });
    expect(screen.queryByRole('button', { name: 'Subir CFDI de retenciones' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Quitar el CFDI/ })).not.toBeInTheDocument();
    // Without anything to do, the list has no column of actions; nor a link to a Configuración it cannot open.
    expect(screen.queryByRole('columnheader', { name: 'Acciones' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Configuración › Negocio › Plataformas de reparto' })).not.toBeInTheDocument();
  });

  it('says when the platform has no terms', async () => {
    serve(() => withholdings({ isrWithholdingPercent: null, vatWithholdingPercent: null }));
    renderPage();
    expect(await screen.findByText(/no tiene comisión ni retenciones configuradas/)).toBeInTheDocument();
  });

  it('shows the error of the server with a retry', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    vi.mocked(apiRequest).mockRejectedValueOnce(new Error('caída'));
    renderPage();

    expect(await screen.findByRole('alert')).toHaveTextContent('No se pudieron cargar las retenciones.');
    serve(() => withholdings());
    await user.click(screen.getByRole('button', { name: 'Reintentar' }));
    expect(await screen.findByRole('table', { name: /Retenciones de Uber Eats/ })).toBeInTheDocument();
  });
});
