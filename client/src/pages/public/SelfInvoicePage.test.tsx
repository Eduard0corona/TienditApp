import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from '../../api/typedClient';
import { selfInvoiceUrl, ticketKeyFromHash } from '../../api/selfInvoiceApi';
import { SelfInvoiceOutcome, SelfInvoiceTicketStatus } from '../../api/generated/enums';
import SelfInvoicePage from './SelfInvoicePage';

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));

const request = vi.mocked(apiRequest);
const PAGE = { businessName: 'Café Luna', available: true, sandbox: false };
const NOT_FOUND = 'No encontramos ese ticket: revisa el folio y el código impresos en él.';

function ticket(overrides: Record<string, unknown> = {}) {
  return {
    orderNumber: 'ORD-000012', date: '2026-10-20T18:00:00Z', total: 116, status: SelfInvoiceTicketStatus.CanInvoice,
    invoiceNumber: null, message: null, invoiceBy: '2026-10-31', ...overrides,
  };
}

function apiError(status: number, detail: string) {
  const headers = new AxiosHeaders();
  return new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, {
    status, data: { detail }, statusText: '', headers, config: { headers },
  } as AxiosResponse);
}

/** Answers the page, and each POST by its route. */
function api(answers: { ticket?: unknown; invoice?: unknown; resend?: unknown; page?: unknown }) {
  request.mockImplementation((async (_method: string, path: string) => {
    const answer = path.endsWith('/ticket') ? answers.ticket
      : path.endsWith('/invoice') ? answers.invoice
        : path.endsWith('/resend') ? answers.resend
          : answers.page ?? PAGE;
    if (answer instanceof Error) throw answer;
    return answer;
  }) as unknown as typeof apiRequest);
}

function renderPage(url = '/m/cafe-luna/facturar') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[url]}>
        <Routes>
          <Route path="/m/:slug/facturar" element={<SelfInvoicePage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

async function fillReceiver() {
  await userEvent.type(screen.getByLabelText('RFC'), 'XIQB891116QE4');
  await userEvent.type(screen.getByLabelText('Nombre o razón social'), 'Karla Fuente Nolasco');
  await userEvent.type(screen.getByLabelText('Código postal fiscal'), '26015');
  await userEvent.type(screen.getByLabelText('Correo para recibir la factura'), 'karla@ejemplo.mx');
}

describe('SelfInvoicePage', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('looks up the ticket its QR brings, and invoices it to the email the customer types', async () => {
    api({
      ticket: ticket(),
      invoice: { outcome: SelfInvoiceOutcome.Stamped, invoiceNumber: 'F-7', email: 'karla@ejemplo.mx', message: null },
    });
    renderPage('/m/cafe-luna/facturar#ORD-000012.ABCDE-12345');

    expect(await screen.findByText('Ticket ORD-000012')).toBeInTheDocument();
    expect(request).toHaveBeenCalledWith('post', '/api/v1/public/self-invoice/{slug}/ticket', {
      path: { slug: 'cafe-luna' }, body: { folio: 'ORD-000012', code: 'ABCDE-12345' },
    });
    // The ticket takes the focus (below the form on a phone), and the QR is looked up once whatever renders after.
    expect(screen.getByRole('heading', { name: 'Ticket ORD-000012' })).toHaveFocus();
    expect(screen.getByLabelText('Folio')).toHaveValue('ORD-000012');
    expect(screen.getByText(/Puedes facturar esta compra hasta el 31 de octubre de 2026/)).toBeInTheDocument();

    const invoice = screen.getByRole('button', { name: 'Facturar' });
    expect(invoice).toBeDisabled();
    await fillReceiver();
    await userEvent.click(invoice);

    expect(await screen.findByText('Listo: tu factura F-7 está timbrada.')).toBeInTheDocument();
    expect(screen.getByText(/Te llegará a karla@ejemplo.mx con el XML y el PDF/)).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveFocus();
    expect(request.mock.calls.filter((call) => String(call[1]).endsWith('/ticket'))).toHaveLength(1);
    expect(request).toHaveBeenCalledWith('post', '/api/v1/public/self-invoice/{slug}/invoice', {
      path: { slug: 'cafe-luna' },
      body: {
        folio: 'ORD-000012', code: 'ABCDE-12345', email: 'karla@ejemplo.mx',
        receiver: { rfc: 'XIQB891116QE4', name: 'Karla Fuente Nolasco', postalCode: '26015', fiscalRegime: '612', cfdiUse: 'G03' },
      },
    });
  });

  it('shows the reason the PAC refused it, keeping the form to correct and try again', async () => {
    api({
      ticket: ticket(),
      invoice: { outcome: SelfInvoiceOutcome.Refused, invoiceNumber: 'F-7', email: 'karla@ejemplo.mx', message: 'El nombre no coincide con el RFC.' },
    });
    renderPage('/m/cafe-luna/facturar#ORD-000012.ABCDE-12345');
    await screen.findByText('Ticket ORD-000012');
    await fillReceiver();
    await userEvent.click(screen.getByRole('button', { name: 'Facturar' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'El SAT no aceptó la factura F-7: El nombre no coincide con el RFC. Corrige tus datos y vuelve a intentarlo.');
    expect(screen.getByRole('button', { name: 'Facturar' })).toBeEnabled();
  });

  it('tells a ticket whose invoice is stamping to look it up again in a few minutes', async () => {
    api({ ticket: ticket({ status: SelfInvoiceTicketStatus.Stamping, invoiceNumber: 'F-7', invoiceBy: null }) });
    renderPage('/m/cafe-luna/facturar#ORD-000012.ABCDE-12345');

    expect(await screen.findByText(/La factura F-7 de este ticket se está timbrando\. Vuelve a buscar tu ticket/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Facturar' })).not.toBeInTheDocument();
  });

  it('says the invoice follows by email while the PAC does not answer', async () => {
    api({
      ticket: ticket(),
      invoice: { outcome: SelfInvoiceOutcome.Stamping, invoiceNumber: 'F-8', email: 'karla@ejemplo.mx', message: null },
    });
    renderPage('/m/cafe-luna/facturar#ORD-000012.ABCDE-12345');
    await screen.findByText('Ticket ORD-000012');
    await fillReceiver();
    await userEvent.click(screen.getByRole('button', { name: 'Facturar' }));

    expect(await screen.findByText(/Tu factura F-8 se está timbrando: te llegará a karla@ejemplo.mx en cuanto quede/)).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Si no te llega en unos minutos, vuelve a buscar tu ticket.');
  });

  it('shows the API\'s reason when the invoice cannot be made, keeping the form', async () => {
    api({ ticket: ticket(), invoice: apiError(400, 'Este ticket se está facturando en este momento: revisa tu correo en unos minutos.') });
    renderPage('/m/cafe-luna/facturar#ORD-000012.ABCDE-12345');
    await screen.findByText('Ticket ORD-000012');
    await fillReceiver();
    await userEvent.click(screen.getByRole('button', { name: 'Facturar' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Este ticket se está facturando en este momento');
    expect(screen.getByRole('button', { name: 'Facturar' })).toBeEnabled();
  });

  it('puts the ticket shown away when another folio or code is typed', async () => {
    api({ ticket: ticket(), page: { ...PAGE, sandbox: true } });
    renderPage('/m/cafe-luna/facturar#ORD-000012.ABCDE-12345');
    await screen.findByText('Ticket ORD-000012');
    expect(screen.getByText('Modo de prueba: las facturas de esta página no tienen validez fiscal.')).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText('Folio'), '3');
    expect(screen.queryByText('Ticket ORD-000012')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Facturar' })).not.toBeInTheDocument();
  });

  it('says the same for a folio that is not there and a code typed wrong', async () => {
    api({ ticket: apiError(404, NOT_FOUND) });
    renderPage();

    const search = await screen.findByRole('button', { name: 'Buscar ticket' });
    expect(search).toBeDisabled();
    await userEvent.type(screen.getByLabelText('Folio'), '12');
    await userEvent.type(screen.getByLabelText('Código'), 'AAAAA-AAAAA');
    await userEvent.click(search);

    expect(await screen.findByRole('alert')).toHaveTextContent(NOT_FOUND);
    expect(screen.queryByText(/Ticket ORD/)).not.toBeInTheDocument();
  });

  it('sends an invoiced ticket\'s invoice again to whoever writes its RFC', async () => {
    api({
      ticket: ticket({ status: SelfInvoiceTicketStatus.Invoiced, invoiceNumber: 'F-3', invoiceBy: null }),
      resend: { invoiceNumber: 'F-3', email: 'otro@ejemplo.mx' },
    });
    renderPage('/m/cafe-luna/facturar#ORD-000012.ABCDE-12345');

    expect(await screen.findByText(/Este ticket ya tiene la factura F-3/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Facturar' })).not.toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('RFC de la factura'), 'xiqb891116qe4');
    await userEvent.type(screen.getByLabelText('Correo'), 'otro@ejemplo.mx');
    await userEvent.click(screen.getByRole('button', { name: 'Reenviar factura' }));

    expect(await screen.findByText('Enviamos la factura F-3 a otro@ejemplo.mx.')).toBeInTheDocument();
    expect(request).toHaveBeenCalledWith('post', '/api/v1/public/self-invoice/{slug}/resend', {
      path: { slug: 'cafe-luna' }, body: { folio: 'ORD-000012', code: 'ABCDE-12345', rfc: 'xiqb891116qe4', email: 'otro@ejemplo.mx' },
    });
  });

  it('tells why a ticket is not invoiced online', async () => {
    const message = 'Esta compra se podía facturar en línea hasta el sábado 31 de octubre de 2026: pídele tu factura al negocio.';
    api({ ticket: ticket({ status: SelfInvoiceTicketStatus.NotOnline, message, invoiceBy: null }) });
    renderPage('/m/cafe-luna/facturar#ORD-000012.ABCDE-12345');

    expect(await screen.findByText(message)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Facturar' })).not.toBeInTheDocument();
  });

  it('asks for no ticket while the business does not invoice online, and says when it does not exist', async () => {
    api({ page: { ...PAGE, available: false } });
    renderPage('/m/cafe-luna/facturar#ORD-000012.ABCDE-12345');

    expect(await screen.findByText('Este negocio no factura en línea por ahora: pídele tu factura directamente.')).toBeInTheDocument();
    expect(screen.queryByLabelText('Folio')).not.toBeInTheDocument();
    await waitFor(() => expect(request).toHaveBeenCalledTimes(1));

    vi.resetAllMocks();
    api({ page: apiError(404, 'No encontramos ese negocio.') });
    renderPage('/m/otro/facturar');
    expect(await screen.findByText('No encontramos ese negocio: revisa la dirección impresa en tu ticket.')).toBeInTheDocument();
  });
});

describe('ticket in the address', () => {
  it('reads the folio and code the QR carries in the fragment', () => {
    expect(ticketKeyFromHash('#ORD-000012.ABCDE-12345')).toEqual({ folio: 'ORD-000012', code: 'ABCDE-12345' });
    expect(ticketKeyFromHash('#ORD-000012')).toEqual({ folio: 'ORD-000012', code: '' });
    expect(ticketKeyFromHash('')).toEqual({ folio: '', code: '' });
    expect(ticketKeyFromHash('#ORD-0%.ABCDE')).toEqual({ folio: 'ORD-0%', code: 'ABCDE' });
  });

  it('puts them in the fragment of the page, never in the query', () => {
    const url = new URL(selfInvoiceUrl('/m/cafe-luna/facturar', { folio: 'ORD-000012', code: 'ABCDE-12345' }));
    expect(url.hash).toBe('#ORD-000012.ABCDE-12345');
    expect(url.pathname).toBe('/m/cafe-luna/facturar');
    expect(url.search).toBe('');
  });
});
