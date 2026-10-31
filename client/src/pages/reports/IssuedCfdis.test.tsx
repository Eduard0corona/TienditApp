import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import api from '../../api/axios';
import type { CfdiMonthEmail, IssuedCfdi, IssuedCfdiSummary } from '../../api/invoicing';
import { apiRequest } from '../../api/typedClient';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import {
  GlobalInvoicePeriodicity, IssuedCfdiExportFormat, IssuedCfdiKind, OrderInvoiceStatus, TenantCapability, type TenantProfileDto,
} from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import IssuedCfdis from './IssuedCfdis';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
vi.mock('../../api/axios', () => ({ default: { get: vi.fn() } }));
vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
const toast = { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() };
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => ({ confirm: vi.fn(), prompt: vi.fn() }) }));

const request = vi.mocked(apiRequest);
type Query = { year: number; month: number; kind?: IssuedCfdiKind; status?: OrderInvoiceStatus; search?: string; pageNumber?: number };

function cfdi(patch: Partial<IssuedCfdi> = {}): IssuedCfdi {
  return {
    id: 'i-1', kind: IssuedCfdiKind.OrderInvoice, number: 'F-12', uuid: '5FB2A1', status: OrderInvoiceStatus.Stamped,
    issuedAt: '2026-10-20T18:00:00Z', stampedAt: '2026-10-20T18:00:02Z', cancellationMotive: undefined, cancelledAt: null,
    failureMessage: null, receiverRfc: 'XIQB891116QE4', receiverName: 'KARLA FUENTE NOLASCO', paymentMethod: 'PUE',
    paymentForm: '04', subTotal: 172.41, taxTotal: 27.59, total: 200, paidAmount: null, orderId: 'o-1', orderNumber: 'ORD-000123',
    relatedNumber: null, replacedByNumber: null, periodicity: undefined, periodFrom: null, periodTo: null, orderCount: null, hasXml: true,
    ...patch,
  };
}

const SUMMARY: IssuedCfdiSummary = {
  year: 2026, month: 10, from: '2026-10-01', to: '2026-10-31',
  kinds: [
    { kind: IssuedCfdiKind.OrderInvoice, inForce: 2, subTotal: 344.82, taxTotal: 55.18, total: 400, cancelling: 1, cancelled: 1, stamping: 0, refused: 1 },
    { kind: IssuedCfdiKind.GlobalInvoice, inForce: 1, subTotal: 86.21, taxTotal: 13.79, total: 100, cancelling: 0, cancelled: 0, stamping: 0, refused: 0 },
    { kind: IssuedCfdiKind.PaymentComplement, inForce: 1, subTotal: 0, taxTotal: 0, total: 120, cancelling: 0, cancelled: 0, stamping: 0, refused: 0 },
    { kind: IssuedCfdiKind.CreditNote, inForce: 1, subTotal: 17.24, taxTotal: 2.76, total: 20, cancelling: 0, cancelled: 0, stamping: 0, refused: 0 },
  ],
};

interface Answers {
  /** The total the list says it has (more than a page lets the test go to the second one). */
  totalCount?: number;
  summary?: (query: Query) => Promise<IssuedCfdiSummary>;
  route?: string;
  /** The app's cache, to come back to the page with what it read before. */
  client?: QueryClient;
  /** The CFDI still «Timbrando» past the PAC's window (Fase 28K); none by default. */
  toReview?: IssuedCfdi[];
  /** The month's emails to the accountant (Fase 28O), the newest first; none by default. */
  accountantEmails?: CfdiMonthEmail[];
  /** The accountant's email the business keeps. */
  accountantEmail?: string | null;
  /** What «Mandar al contador» answers. */
  send?: (body: { year: number; month: number; email: string }) => Promise<CfdiMonthEmail>;
}

function monthEmail(patch: Partial<CfdiMonthEmail> = {}): CfdiMonthEmail {
  return {
    id: 'm-1', year: 2026, month: 10, to: 'contador@despacho.mx', requestedAt: '2026-10-21T16:00:00Z', byTienditApp: false,
    sentAt: null, cfdiCount: null, failedAt: null, failure: undefined, failedAttempts: 0, lastFailedAttemptAt: null,
    ...patch,
  };
}

function renderPage(list: (query: Query) => IssuedCfdi[], answers: Answers = {}) {
  request.mockImplementation(((method: string, path: string, options?: { query: Query }) => {
    if (path === '/api/v1/invoicing/issued-cfdis') {
      const items = list(options!.query);
      const totalCount = answers.totalCount ?? items.length;
      return Promise.resolve({
        items, pageNumber: options!.query.pageNumber ?? 1, totalPages: Math.ceil(totalCount / 20), totalCount,
        hasPreviousPage: false, hasNextPage: totalCount > 20,
      });
    }
    if (path === '/api/v1/invoicing/issued-cfdis/summary') {
      return answers.summary ? answers.summary(options!.query) : Promise.resolve({ ...SUMMARY, ...options!.query });
    }
    if (path === '/api/v1/invoicing/issued-cfdis/to-review') return Promise.resolve(answers.toReview ?? []);
    if (path === '/api/v1/invoicing/issued-cfdis/accountant-emails' && method === 'get') return Promise.resolve(answers.accountantEmails ?? []);
    if (path === '/api/v1/invoicing/issued-cfdis/accountant-emails' && method === 'post' && answers.send) {
      return answers.send((options as unknown as { body: { year: number; month: number; email: string } }).body);
    }
    if (path === '/api/v1/invoicing/settings') return Promise.resolve({ accountantEmail: answers.accountantEmail ?? null });
    return Promise.reject(new Error(`Sin respuesta para ${method} ${path}`));
  }) as unknown as typeof apiRequest);
  const client = answers.client ?? new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[answers.route ?? '/reports/issued-cfdis']}>
        <IssuedCfdis />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function lastQuery(path: string): Query {
  const calls = request.mock.calls.filter(([, called]) => called === path);
  return (calls[calls.length - 1][2] as unknown as { query: Query }).query;
}

/** An owner of a store: the month's CFDI are theirs, and the tabs of Reportes show them. */
function signInAsOwner() {
  vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role: 'TenantAdmin', permissions: [] } } as unknown as ReturnType<typeof useAuth>);
  vi.mocked(useTenant).mockReturnValue({
    tenant: { id: 't1', name: 'Tienda Luna', slug: 'luna', timeZone: 'America/Mexico_City' } as TenantProfileDto,
    capabilities: TenantCapability.Retail,
    isLoading: false,
    refreshTenant: vi.fn(),
  });
}

describe('IssuedCfdis', () => {
  beforeEach(() => {
    signInAsOwner();
    // 26 October 2026 at noon in Mexico City.
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-26T18:00:00Z'));
    setBusinessTimeZone('America/Mexico_City');
    // saveBlob clicks a link: jsdom does not download.
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
  });
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    vi.clearAllMocks();
  });

  it("opens on the business's month with what the CFDI in force add up to, and what was cancelled, waits or was refused", async () => {
    renderPage(() => [cfdi()]);

    expect(await screen.findByRole('heading', { name: 'Octubre de 2026' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'CFDI emitidos' })).toHaveAttribute('aria-current', 'page');
    expect(lastQuery('/api/v1/invoicing/issued-cfdis')).toMatchObject({ year: 2026, month: 10, pageNumber: 1 });
    const summary = await screen.findByRole('region', { name: 'Resumen de octubre de 2026' });
    const invoices = within(summary).getByRole('row', { name: /Facturas 2/ });
    expect(invoices).toHaveTextContent('$344.82');
    expect(invoices).toHaveTextContent('$400.00');
    expect(within(summary).getByRole('row', { name: /Complementos de pago/ })).toHaveTextContent('Pagado $120.00');
    expect(summary).toHaveTextContent(
      'Además: 1 cancelado; 1 espera que el cliente acepte su cancelación en el SAT; 1 rechazado por el PAC: no es un CFDI y explica el folio que falta.',
    );
    // A month that has not come yet has nothing to show.
    expect(screen.getByRole('button', { name: 'Mes siguiente (noviembre de 2026)' })).toBeDisabled();
  });

  it('lists each CFDI with its kind, state, receiver and where it comes from', async () => {
    renderPage(() => [
      cfdi({ id: 'p-1', kind: IssuedCfdiKind.PaymentComplement, number: 'P-1', total: null, subTotal: null, taxTotal: null, paidAmount: 120,
        paymentMethod: null, relatedNumber: 'F-11' }),
      cfdi({ id: 'g-1', kind: IssuedCfdiKind.GlobalInvoice, number: 'F-10', receiverRfc: 'XAXX010101000', receiverName: 'PUBLICO EN GENERAL',
        orderId: null, orderNumber: null, periodicity: GlobalInvoicePeriodicity.Daily, periodFrom: '2026-10-08', periodTo: '2026-10-08',
        orderCount: 3 }),
      cfdi({ id: 'i-2', number: 'F-9', relatedNumber: 'F-8' }),
      cfdi({ id: 'i-1', number: 'F-8', status: OrderInvoiceStatus.Cancelled, replacedByNumber: 'F-9' }),
      cfdi({ id: 'i-0', number: 'F-7', status: OrderInvoiceStatus.Refused, uuid: null, hasXml: false, failureMessage: 'El RFC del receptor no existe.' }),
    ]);

    const complement = (await screen.findByText('P-1')).closest('tr')!;
    expect(complement).toHaveTextContent('Complemento de pago');
    expect(complement).toHaveTextContent('Timbrado');
    expect(complement).toHaveTextContent('Pago de la factura F-11');
    expect(complement).toHaveTextContent('Pagado $120.00');
    expect(within(complement).getByRole('link', { name: 'ORD-000123' })).toHaveAttribute('href', '/orders?search=ORD-000123');
    expect(within(complement).getByRole('button', { name: 'Descargar XML del complemento de pago P-1' })).toBeInTheDocument();

    const global = screen.getByText('F-10').closest('tr')!;
    expect(within(global).getByRole('link', { name: 'Diaria · 8 oct. 2026: ver la factura global F-10' }))
      .toHaveAttribute('href', '/reports/global-invoices/g-1');
    expect(global).toHaveTextContent('3 ventas');
    expect(global).toHaveTextContent('PUBLICO EN GENERAL');

    expect(screen.getByText('F-9').closest('tr')).toHaveTextContent('Sustituye a la factura F-8');
    const replaced = screen.getByText('F-8', { selector: '.fw-semibold' }).closest('tr')!;
    expect(replaced).toHaveTextContent('Cancelada');
    expect(replaced).toHaveTextContent('Sustituida por la factura F-9');
    // A refused one says why, and has no files.
    const refused = screen.getByText('F-7').closest('tr')!;
    expect(refused).toHaveTextContent('El RFC del receptor no existe.');
    expect(within(refused).queryByRole('button', { name: /Descargar/ })).not.toBeInTheDocument();
  });

  it('narrows the month by kind, state and search, and goes back a month', async () => {
    const user = userEvent.setup();
    renderPage(() => [cfdi()]);
    await screen.findByText('F-12');

    await user.selectOptions(screen.getByLabelText('Tipo'), 'Notas de crédito');
    await waitFor(() => expect(lastQuery('/api/v1/invoicing/issued-cfdis')).toMatchObject({ kind: IssuedCfdiKind.CreditNote }));
    await user.selectOptions(screen.getByLabelText('Estado'), 'Cancelados');
    await waitFor(() => expect(lastQuery('/api/v1/invoicing/issued-cfdis'))
      .toMatchObject({ kind: IssuedCfdiKind.CreditNote, status: OrderInvoiceStatus.Cancelled }));
    await user.type(screen.getByPlaceholderText('Buscar folio, UUID, RFC, nombre o pedido'), 'xiqb');
    await waitFor(() => expect(lastQuery('/api/v1/invoicing/issued-cfdis')).toMatchObject({ search: 'xiqb' }));

    await user.click(screen.getByRole('button', { name: 'Mes anterior (septiembre de 2026)' }));
    expect(await screen.findByRole('heading', { name: 'Septiembre de 2026' })).toBeInTheDocument();
    await waitFor(() => expect(lastQuery('/api/v1/invoicing/issued-cfdis')).toMatchObject({ year: 2026, month: 9, pageNumber: 1 }));
    expect(lastQuery('/api/v1/invoicing/issued-cfdis/summary')).toMatchObject({ year: 2026, month: 9 });
    expect(screen.getByRole('button', { name: 'Mes siguiente (octubre de 2026)' })).toBeEnabled();
  });

  it("downloads the month's XML for the accountant, and says why when it cannot", async () => {
    const user = userEvent.setup();
    const created = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:x');
    vi.mocked(api.get).mockResolvedValueOnce({
      data: new Blob(['zip']), headers: { 'content-disposition': 'attachment; filename="CFDI_2026-10.zip"' },
    });
    renderPage(() => [cfdi()]);
    await screen.findByText('F-12');

    await user.click(screen.getByRole('button', { name: 'Descargar XML' }));

    await waitFor(() => expect(api.get).toHaveBeenCalledWith('/invoicing/issued-cfdis/export', {
      responseType: 'blob', params: { year: 2026, month: 10, format: IssuedCfdiExportFormat.Zip },
    }));
    expect(created).toHaveBeenCalled();

    vi.mocked(api.get).mockRejectedValueOnce(Object.assign(new Error('400'), {
      isAxiosError: true,
      response: { status: 400, data: new Blob([JSON.stringify({ detail: 'El mes tiene 6,000 CFDI y la descarga junta hasta 5,000.' })]) },
    }));
    await user.click(screen.getByRole('button', { name: 'Descargar lista' }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('El mes tiene 6,000 CFDI y la descarga junta hasta 5,000.'));
    created.mockRestore();
  });

  it('downloads the XML of a row from where its kind keeps it', async () => {
    const user = userEvent.setup();
    const created = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:x');
    vi.mocked(api.get).mockResolvedValue({ data: new Blob(['<cfdi/>']), headers: {} });
    renderPage(() => [cfdi({ id: 'n-1', kind: IssuedCfdiKind.CreditNote, number: 'NC-1', relatedNumber: 'F-12' })]);

    await user.click(await screen.findByRole('button', { name: 'Descargar PDF de la nota de crédito NC-1' }));

    await waitFor(() => expect(api.get).toHaveBeenCalledWith('/invoicing/credit-notes/n-1/pdf', { responseType: 'blob' }));
    created.mockRestore();
  });

  it('opens on the month of the business, not of UTC, at the turn of the month', async () => {
    // 1 November at 03:00 UTC is still 31 October at 21:00 in Mexico City.
    vi.setSystemTime(new Date('2026-11-01T03:00:00Z'));
    renderPage(() => []);

    expect(await screen.findByRole('heading', { name: 'Octubre de 2026' })).toBeInTheDocument();
    expect(lastQuery('/api/v1/invoicing/issued-cfdis')).toMatchObject({ year: 2026, month: 10 });
  });

  it('opens the month the address names, from January 2022 to the current one, and takes the focus at either end', async () => {
    const user = userEvent.setup();
    renderPage(() => [], { route: '/reports/issued-cfdis?year=2022&month=2' });

    expect(await screen.findByRole('heading', { name: 'Febrero de 2022' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Mes anterior (enero de 2022)' }));
    const heading = await screen.findByRole('heading', { name: 'Enero de 2022' });
    // Before January 2022 nothing was issued: the button is off, and the focus is on the month, not lost.
    expect(screen.getByRole('button', { name: 'Mes anterior (diciembre de 2021)' })).toBeDisabled();
    expect(heading).toHaveFocus();
  });

  it('opens the current month when the address names one out of range', async () => {
    renderPage(() => [], { route: '/reports/issued-cfdis?year=2027&month=1' });

    expect(await screen.findByRole('heading', { name: 'Octubre de 2026' })).toBeInTheDocument();
  });

  it('sends the first kind and the first state, and goes back to the first page when the list is narrowed', async () => {
    const user = userEvent.setup();
    renderPage(() => [cfdi()], { totalCount: 45 });
    await screen.findByText('F-12');

    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
    await waitFor(() => expect(lastQuery('/api/v1/invoicing/issued-cfdis')).toMatchObject({ pageNumber: 2 }));
    await user.selectOptions(screen.getByLabelText('Tipo'), 'Facturas');
    await waitFor(() => expect(lastQuery('/api/v1/invoicing/issued-cfdis'))
      .toMatchObject({ kind: IssuedCfdiKind.OrderInvoice, pageNumber: 1 }));
    await user.selectOptions(screen.getByLabelText('Estado'), 'Timbrando');
    await waitFor(() => expect(lastQuery('/api/v1/invoicing/issued-cfdis')).toMatchObject({ status: OrderInvoiceStatus.Stamping }));
  });

  it('says when the filters leave nothing, and offers no download of a month without CFDI', async () => {
    const user = userEvent.setup();
    const none: IssuedCfdiSummary = {
      ...SUMMARY, month: 9, from: '2026-09-01', to: '2026-09-30',
      kinds: SUMMARY.kinds.map((k) => ({ ...k, inForce: 0, subTotal: 0, taxTotal: 0, total: 0, cancelling: 0, cancelled: 0, stamping: 0, refused: 0 })),
    };
    renderPage((query) => (query.month === 9 || query.kind === IssuedCfdiKind.CreditNote ? [] : [cfdi()]), {
      summary: (query) => Promise.resolve(query.month === 9 ? none : { ...SUMMARY, ...query }),
    });
    await screen.findByText('F-12');

    await user.selectOptions(screen.getByLabelText('Tipo'), 'Notas de crédito');
    expect(await screen.findByText('Ningún CFDI del mes coincide con los filtros.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Descargar XML' })).toBeEnabled();

    await user.selectOptions(screen.getByLabelText('Tipo'), 'Todos');
    await user.click(screen.getByRole('button', { name: 'Mes anterior (septiembre de 2026)' }));
    expect(await screen.findByText('No hay CFDI en este mes.')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Descargar XML' })).toBeDisabled());
    expect(screen.getByRole('button', { name: 'Descargar lista' })).toBeDisabled();
  });

  it('reads the month again when it comes back to it, after a CFDI changed in its order', async () => {
    // Fresh for 30 seconds, as the app keeps what it reads.
    const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 30_000 } } });
    let status: OrderInvoiceStatus = OrderInvoiceStatus.Stamped;
    let inForce = 2;
    const summary = () => Promise.resolve({
      ...SUMMARY, kinds: SUMMARY.kinds.map((kind) => (kind.kind === IssuedCfdiKind.OrderInvoice ? { ...kind, inForce } : kind)),
    });
    const first = renderPage(() => [cfdi({ status })], { client, summary });
    expect(await screen.findByText('Timbrada')).toBeInTheDocument();
    first.unmount();

    // F-12 was cancelled from its order.
    status = OrderInvoiceStatus.Cancelled;
    inForce = 1;
    renderPage(() => [cfdi({ status })], { client, summary });

    expect(await screen.findByText('Cancelada')).toBeInTheDocument();
    const summaryTable = screen.getByRole('table', { name: 'Vigentes del mes' });
    await waitFor(() => expect(within(summaryTable).getByRole('row', { name: /^Facturas 1/ })).toBeInTheDocument());
  });

  it('says why the summary did not load, and tries again', async () => {
    const user = userEvent.setup();
    let fail = true;
    renderPage(() => [cfdi()], {
      summary: (query) => (fail ? Promise.reject(new Error('caído')) : Promise.resolve({ ...SUMMARY, ...query })),
    });

    expect(await screen.findByText('No se pudo cargar el resumen del mes.')).toBeInTheDocument();
    fail = false;
    await user.click(screen.getByRole('button', { name: 'Reintentar' }));
    expect(await screen.findByRole('region', { name: 'Resumen de octubre de 2026' })).toBeInTheDocument();
  });

  it("sends the month's file to the accountant at the address the business keeps, and says how it went (Fase 28O)", async () => {
    const user = userEvent.setup();
    let emails: CfdiMonthEmail[] = [monthEmail({ sentAt: '2026-10-21T16:01:00Z', cfdiCount: 6 })];
    const send = vi.fn((body: { year: number; month: number; email: string }) => {
      emails = [monthEmail({ id: 'm-2', to: body.email, requestedAt: '2026-10-21T17:00:00Z' }), ...emails];
      return Promise.resolve(emails[0]);
    });
    renderPage(() => [cfdi()], { accountantEmail: 'contador@despacho.mx', send, get accountantEmails() { return emails; } });

    expect(await screen.findByText(/Se mandaron a contador@despacho\.mx el .*, con 6 CFDI\./)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Mandar al contador' }));
    const dialog = await screen.findByRole('dialog', { name: 'Mandar al contador los CFDI de octubre de 2026' });
    const address = within(dialog).getByLabelText('Correo del contador');
    expect(address).toHaveValue('contador@despacho.mx');
    await user.clear(address);
    await user.type(address, ' otra@despacho.mx ');
    await user.click(within(dialog).getByRole('button', { name: 'Mandar' }));

    await waitFor(() => expect(send).toHaveBeenCalledWith({ year: 2026, month: 10, email: 'otra@despacho.mx' }));
    expect(toast.success).toHaveBeenCalledWith('Se están mandando los CFDI de octubre de 2026 a otra@despacho.mx.');
    expect(await screen.findByText('Mandando los CFDI a otra@despacho.mx…')).toBeInTheDocument();
  });

  it('offers no email to the accountant of a month without CFDI', async () => {
    renderPage(() => [], { summary: (query) => Promise.resolve({ ...SUMMARY, ...query, kinds: SUMMARY.kinds.map((k) => ({ ...k, inForce: 0, cancelled: 0, cancelling: 0, refused: 0 })) }) });

    await waitFor(() => expect(screen.getByRole('button', { name: 'Descargar XML' })).toBeDisabled());
    expect(screen.getByRole('button', { name: 'Mandar al contador' })).toBeDisabled();
  });

  it('puts the CFDI to check in the SAT\'s portal above the month, whatever month they are of (Fase 28K)', async () => {
    renderPage(() => [cfdi()], {
      toReview: [cfdi({ id: 's-1', number: 'F-9', status: OrderInvoiceStatus.Stamping, issuedAt: '2026-09-28T18:00:00Z', hasXml: false })],
    });

    const toReview = await screen.findByRole('region', { name: 'Por revisar en el portal del SAT' });
    expect(within(toReview).getByRole('button', { name: 'Ya se timbró la factura F-9' })).toBeInTheDocument();
    const month = screen.getByRole('heading', { name: 'Octubre de 2026' });
    expect(toReview.compareDocumentPosition(month) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});
