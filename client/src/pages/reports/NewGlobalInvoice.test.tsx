import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes, useParams } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { GlobalInvoice, GlobalInvoiceDraft } from '../../api/invoicing';
import { apiRequest } from '../../api/typedClient';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { useConfirm } from '../../hooks/useConfirm';
import { GlobalInvoicePeriodicity, OrderInvoiceStatus, TenantCapability, type TenantProfileDto } from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import NewGlobalInvoice from './NewGlobalInvoice';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: vi.fn() }));
const toast = { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() };
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

const request = vi.mocked(apiRequest);
const confirm = vi.fn();
type Options = { query?: { periodicity?: GlobalInvoicePeriodicity; day?: string }; body?: unknown };

function draft(patch: Partial<GlobalInvoiceDraft> = {}): GlobalInvoiceDraft {
  return {
    periodicity: GlobalInvoicePeriodicity.Daily,
    periodFrom: '2026-10-22',
    periodTo: '2026-10-22',
    months: '10',
    year: 2026,
    available: true,
    sandbox: false,
    problem: null,
    blockers: [],
    warnings: [],
    isReadyToStamp: true,
    paymentForm: '04',
    subTotal: 258.62,
    totalTransferred: 41.38,
    total: 300,
    transfers: [{ base: 258.62, tax: '002', factorType: 'Tasa', rateOrQuota: 0.16, amount: 41.38 }],
    saleCount: 2,
    conceptCount: 2,
    sales: [
      { orderId: 'o-1', orderNumber: 'ORD-000001', paidAt: '2026-10-22T18:00:00Z', total: 200 },
      { orderId: 'o-2', orderNumber: 'ORD-000002', paidAt: '2026-10-22T19:00:00Z', total: 100 },
    ],
    leftOut: [{ orderId: 'o-3', orderNumber: 'ORD-000003', total: 50, reason: 'Tiene su propia factura (F-4).' }],
    fingerprint: 'a1b2c3',
    ...patch,
  };
}

const STAMPED: GlobalInvoice = {
  id: 'g-1', number: 'F-5', status: OrderInvoiceStatus.Stamped, periodicity: GlobalInvoicePeriodicity.Daily,
  periodFrom: '2026-10-22', periodTo: '2026-10-22', months: '10', year: 2026, orderCount: 2, paymentForm: '04',
  subTotal: 258.62, taxTotal: 41.38, total: 300, requestedAt: '2026-10-23T15:00:00Z', uuid: 'A1B2', stampedAt: '2026-10-23T15:00:02Z',
  hasXml: true, hasPdf: true, failureMessage: null, refusedAt: null, cancellationRequestedAt: null, cancelledAt: null,
};

function Detail() {
  return <p>Detalle {useParams().id}</p>;
}

function renderPage(
  preview: (query: Options['query']) => GlobalInvoiceDraft | Promise<GlobalInvoiceDraft>,
  issue: (body: unknown) => Promise<unknown> = () => Promise.resolve(STAMPED),
) {
  request.mockImplementation(((method: string, path: string, options?: Options) => {
    if (method === 'get' && path === '/api/v1/invoicing/global-invoices/draft') return Promise.resolve(preview(options?.query));
    if (method === 'post' && path === '/api/v1/invoicing/global-invoices') return issue(options?.body);
    return Promise.reject(new Error(`Sin respuesta para ${method} ${path} ${JSON.stringify(options)}`));
  }) as unknown as typeof apiRequest);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/reports/global-invoices/new']}>
        <Routes>
          <Route path="/reports/global-invoices/new" element={<NewGlobalInvoice />} />
          <Route path="/reports/global-invoices/:id" element={<Detail />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

/** An owner of a restaurant: the global invoice is theirs, and the tabs of Reportes show it. */
function signInAsOwner() {
  vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role: 'TenantAdmin', permissions: [] } } as unknown as ReturnType<typeof useAuth>);
  vi.mocked(useTenant).mockReturnValue({
    tenant: { id: 't1', name: 'Cocina Lupita', slug: 'lupita', timeZone: 'America/Mexico_City' } as TenantProfileDto,
    capabilities: TenantCapability.Food,
    isLoading: false,
    refreshTenant: vi.fn(),
  });
}

describe('NewGlobalInvoice', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-23T18:00:00Z'));
    setBusinessTimeZone('America/Mexico_City');
    signInAsOwner();
    vi.mocked(useConfirm).mockReturnValue({ confirm } as unknown as ReturnType<typeof useConfirm>);
    confirm.mockResolvedValue(true);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  it('previews yesterday by default and stamps exactly the sales and total the person reviewed', async () => {
    const user = userEvent.setup();
    renderPage(() => draft());

    expect(await screen.findByRole('heading', { name: 'Diaria · 22 oct. 2026' })).toBeInTheDocument();
    expect(request).toHaveBeenCalledWith('get', '/api/v1/invoicing/global-invoices/draft', expect.objectContaining({
      query: { periodicity: GlobalInvoicePeriodicity.Daily, day: '2026-10-22' },
    }));
    expect(screen.getByText('ORD-000002')).toBeInTheDocument();
    expect(screen.getByText('Tiene su propia factura (F-4).')).toBeInTheDocument();
    expect(screen.getByText('IVA 16 % sobre $258.62')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Timbrar factura global' }));

    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ message: expect.stringContaining('2 ventas por $300.00') }));
    await waitFor(() => expect(request).toHaveBeenCalledWith('post', '/api/v1/invoicing/global-invoices', {
      body: { periodicity: GlobalInvoicePeriodicity.Daily, day: '2026-10-22', expectedSaleCount: 2, expectedTotal: 300, expectedFingerprint: 'a1b2c3' },
    }));
    expect(await screen.findByText('Detalle g-1')).toBeInTheDocument();
    expect(toast.success).toHaveBeenCalledWith('Factura global F-5 timbrada.');
  });

  it('asks for the period chosen and stamps nothing while something stops it', async () => {
    const user = userEvent.setup();
    renderPage((query) => (query?.periodicity === GlobalInvoicePeriodicity.Monthly
      ? draft({
        periodicity: GlobalInvoicePeriodicity.Monthly,
        periodFrom: '2026-10-01',
        periodTo: '2026-10-31',
        isReadyToStamp: false,
        blockers: ['El periodo termina el 31 de octubre de 2026: su factura global se emite a partir del día siguiente.'],
      })
      : draft()));
    await screen.findByRole('heading', { name: 'Diaria · 22 oct. 2026' });
    expect(screen.getByRole('button', { name: 'Timbrar factura global' })).toBeEnabled();

    await user.selectOptions(screen.getByLabelText('Periodicidad'), 'Mensual');

    expect(await screen.findByRole('heading', { name: 'Mensual · 1 al 31 oct. 2026' })).toBeInTheDocument();
    expect(screen.getByText('El periodo termina el 31 de octubre de 2026: su factura global se emite a partir del día siguiente.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Timbrar factura global' })).toBeDisabled();
  });

  it('stays on the preview, reads it again and says why when the sales changed meanwhile', async () => {
    const user = userEvent.setup();
    let reads = 0;
    renderPage(
      () => (reads++ === 0 ? draft() : draft({ saleCount: 1, total: 200, sales: draft().sales.slice(0, 1) })),
      () => Promise.reject(Object.assign(new Error('400'), {
        isAxiosError: true,
        response: { status: 400, data: { detail: 'Las ventas del periodo cambiaron desde que viste la vista previa: revísala otra vez antes de timbrar.' } },
      })),
    );
    await screen.findByText('ORD-000002');

    await user.click(screen.getByRole('button', { name: 'Timbrar factura global' }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith(
      'Las ventas del periodo cambiaron desde que viste la vista previa: revísala otra vez antes de timbrar.'));
    await waitFor(() => expect(screen.queryByText('ORD-000002')).not.toBeInTheDocument());
    expect(screen.getByText('1 venta')).toBeInTheDocument();
  });

  it('stamps the next time with the numbers of the preview read again, never the first one', async () => {
    const user = userEvent.setup();
    let reads = 0;
    const sent: unknown[] = [];
    renderPage(
      () => (reads++ === 0 ? draft() : draft({ saleCount: 1, total: 200, sales: draft().sales.slice(0, 1), fingerprint: 'd4e5f6' })),
      (body) => {
        sent.push(body);
        return sent.length === 1
          ? Promise.reject(Object.assign(new Error('400'), { isAxiosError: true, response: { status: 400, data: { detail: 'Las ventas del periodo cambiaron desde que viste la vista previa: revísala otra vez antes de timbrar.' } } }))
          : Promise.resolve(STAMPED);
      },
    );
    await screen.findByText('ORD-000002');
    await user.click(screen.getByRole('button', { name: 'Timbrar factura global' }));
    await waitFor(() => expect(screen.queryByText('ORD-000002')).not.toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'Timbrar factura global' }));

    expect(await screen.findByText('Detalle g-1')).toBeInTheDocument();
    expect(sent).toEqual([
      expect.objectContaining({ expectedSaleCount: 2, expectedTotal: 300, expectedFingerprint: 'a1b2c3' }),
      expect.objectContaining({ expectedSaleCount: 1, expectedTotal: 200, expectedFingerprint: 'd4e5f6' }),
    ]);
  });

  it('does not stamp a preview it could not read again', async () => {
    const user = userEvent.setup();
    let reads = 0;
    renderPage(
      () => (reads++ === 0
        ? draft()
        : Promise.reject(Object.assign(new Error('503'), { isAxiosError: true, response: { status: 503, data: {} } }))),
      () => Promise.reject(Object.assign(new Error('400'), { isAxiosError: true, response: { status: 400, data: { detail: 'Las ventas del periodo cambiaron desde que viste la vista previa: revísala otra vez antes de timbrar.' } } })),
    );
    await screen.findByText('ORD-000002');

    await user.click(screen.getByRole('button', { name: 'Timbrar factura global' }));

    expect(await screen.findByText('No se pudo calcular la factura global.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Timbrar factura global' })).toBeDisabled();
  });

  it('asks for nothing while the year being typed is not one the SAT takes', async () => {
    renderPage(() => draft());
    await screen.findByText('ORD-000002');
    request.mockClear();

    fireEvent.change(screen.getByLabelText('Un día del periodo'), { target: { value: '0202-10-22' } });

    expect(await screen.findByText('Elige un día del periodo, de 2022 en adelante.')).toBeInTheDocument();
    expect(request).not.toHaveBeenCalled();
    expect(screen.getByLabelText('Un día del periodo')).toHaveAttribute('max', '2026-10-22');
  });

  it('stamps nothing when the person does not confirm', async () => {
    const user = userEvent.setup();
    confirm.mockResolvedValueOnce(false);
    renderPage(() => draft());
    await screen.findByText('ORD-000002');

    await user.click(screen.getByRole('button', { name: 'Timbrar factura global' }));

    await waitFor(() => expect(confirm).toHaveBeenCalled());
    expect(request).not.toHaveBeenCalledWith('post', expect.anything(), expect.anything());
  });

  it('sends the business to set up invoicing when it is not ready', async () => {
    renderPage(() => draft({
      isReadyToStamp: false,
      problem: 'Sube el certificado de sello digital (CSD) del negocio en Configuración › Facturación para facturar.',
      saleCount: 0,
      sales: [],
      blockers: ['No hay ventas por facturar en este periodo.'],
    }));

    expect(await screen.findByRole('link', { name: 'Ir a Configuración › Facturación' })).toHaveAttribute('href', '/settings/invoicing');
    expect(screen.getByRole('button', { name: 'Timbrar factura global' })).toBeDisabled();
  });
});
