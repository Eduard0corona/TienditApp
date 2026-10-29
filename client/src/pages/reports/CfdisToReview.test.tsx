import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders } from 'axios';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { IssuedCfdi } from '../../api/invoicing';
import { apiRequest } from '../../api/typedClient';
import ConfirmProvider from '../../components/feedback/ConfirmProvider';
import { IssuedCfdiKind, OrderInvoiceStatus } from '../../types';
import { setBusinessTimeZone } from '../../utils/dates';
import CfdisToReview from './CfdisToReview';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
const toast = { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() };
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

const request = vi.mocked(apiRequest);
const UUID = '5F0B7C1E-2A3D-4E5F-8A9B-0C1D2E3F4A5B';

function cfdi(patch: Partial<IssuedCfdi> = {}): IssuedCfdi {
  return {
    id: 'i-1', kind: IssuedCfdiKind.OrderInvoice, number: 'F-12', uuid: null, status: OrderInvoiceStatus.Stamping,
    issuedAt: '2026-10-22T16:30:00Z', stampedAt: null, cancellationMotive: undefined, cancelledAt: null,
    failureMessage: 'El PAC no responde.', receiverRfc: 'XIQB891116QE4', receiverName: 'KARLA FUENTE NOLASCO', paymentMethod: 'PUE',
    paymentForm: '04', subTotal: 172.41, taxTotal: 27.59, total: 200, paidAmount: null, orderId: 'o-1', orderNumber: 'ORD-000123',
    relatedNumber: null, replacedByNumber: null, periodicity: undefined, periodFrom: null, periodTo: null, orderCount: null, hasXml: false,
    ...patch,
  };
}

function badRequest(detail: string): AxiosError {
  const headers = new AxiosHeaders();
  return new AxiosError('Bad Request', 'ERR_BAD_REQUEST', { headers }, null, {
    status: 400, statusText: 'Bad Request', headers, config: { headers }, data: { detail },
  });
}

interface Answers {
  stamped?: (body: { uuid: string }) => Promise<IssuedCfdi>;
  notStamped?: () => Promise<IssuedCfdi>;
}

function renderSection(toReview: () => Promise<IssuedCfdi[]>, answers: Answers = {}) {
  request.mockImplementation(((method: string, path: string, options?: { body?: { uuid: string } }) => {
    if (path === '/api/v1/invoicing/issued-cfdis/to-review') return toReview();
    if (path === '/api/v1/invoicing/issued-cfdis/{kind}/{cfdiId}/stamped' && answers.stamped) return answers.stamped(options!.body!);
    if (path === '/api/v1/invoicing/issued-cfdis/{kind}/{cfdiId}/not-stamped' && answers.notStamped) return answers.notStamped();
    return Promise.reject(new Error(`Sin respuesta para ${method} ${path}`));
  }) as unknown as typeof apiRequest);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
        <CfdisToReview />
      </ConfirmProvider>
    </QueryClientProvider>,
  );
}

describe('CfdisToReview', () => {
  beforeEach(() => setBusinessTimeZone('America/Mexico_City'));
  afterEach(() => vi.clearAllMocks());

  it('shows nothing while no CFDI stayed «Timbrando» past the window', async () => {
    const { container } = renderSection(() => Promise.resolve([]));

    await waitFor(() => expect(request).toHaveBeenCalledWith('get', '/api/v1/invoicing/issued-cfdis/to-review', expect.anything()));
    expect(container).toBeEmptyDOMElement();
  });

  it('lists each one with what finds it in the SAT\'s portal: its date, receiver and total', async () => {
    renderSection(() => Promise.resolve([
      cfdi(),
      cfdi({ id: 'p-1', kind: IssuedCfdiKind.PaymentComplement, number: 'P-3', total: 0, paidAmount: 120 }),
    ]));

    const section = await screen.findByRole('region', { name: 'Por revisar en el portal del SAT' });
    expect(section).toHaveTextContent('Consultar facturas emitidas');
    const rows = within(section).getAllByRole('row').slice(1);
    expect(rows[0]).toHaveTextContent('F-12');
    expect(rows[0]).toHaveTextContent('Factura');
    expect(rows[0]).toHaveTextContent('KARLA FUENTE NOLASCO');
    expect(rows[0]).toHaveTextContent('XIQB891116QE4');
    expect(rows[0]).toHaveTextContent('$200.00');
    // 16:30 UTC is 10:30 in Mexico City, the business's time zone.
    expect(rows[0]).toHaveTextContent('10:30');
    // A complement is worth zero: what it says is what was paid.
    expect(rows[1]).toHaveTextContent('Pagado $120.00');
    expect(within(rows[1]).getByRole('button', { name: 'Ya se timbró el complemento de pago P-3' })).toBeInTheDocument();
    expect(within(rows[0]).getByRole('button', { name: 'No se timbró la factura F-12' })).toBeInTheDocument();
  });

  it('brings the stamp back with its folio fiscal, and keeps what the API refuses on the form to correct it', async () => {
    const user = userEvent.setup();
    const sent: string[] = [];
    let answers = 0;
    let listed = [cfdi()];
    renderSection(() => Promise.resolve(listed), {
      stamped: ({ uuid }) => {
        sent.push(uuid);
        answers += 1;
        if (answers === 1) return Promise.reject(badRequest('Ese folio fiscal es de otro comprobante, no de F-12: revisa su serie y folio en el portal del SAT.'));
        listed = [];
        return Promise.resolve(cfdi({ status: OrderInvoiceStatus.Stamped, uuid: UUID, hasXml: true }));
      },
    });

    await user.click(await screen.findByRole('button', { name: 'Ya se timbró la factura F-12' }));
    const dialog = screen.getByRole('dialog', { name: 'Factura F-12 ya se timbró' });
    const input = within(dialog).getByLabelText('Folio fiscal (UUID)');

    // Not even sent until it is complete.
    await user.type(input, '5F0B7C1E-2A3D');
    await user.click(within(dialog).getByRole('button', { name: 'Guardar el timbre' }));
    expect(input).toHaveAccessibleDescription(expect.stringContaining('Escribe el folio fiscal (UUID) completo'));
    expect(sent).toEqual([]);

    await user.clear(input);
    await user.type(input, ` ${UUID.toLowerCase()} `);
    await user.click(within(dialog).getByRole('button', { name: 'Guardar el timbre' }));
    expect(await within(dialog).findByText(/es de otro comprobante, no de F-12/)).toBeInTheDocument();
    expect(input).toHaveValue(` ${UUID.toLowerCase()} `);

    await user.click(within(dialog).getByRole('button', { name: 'Guardar el timbre' }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Factura F-12 timbrada: ya tiene su XML y su PDF.'));
    expect(sent).toEqual([UUID.toLowerCase(), UUID.toLowerCase()]);
    expect(request).toHaveBeenCalledWith('post', '/api/v1/invoicing/issued-cfdis/{kind}/{cfdiId}/stamped', {
      path: { kind: IssuedCfdiKind.OrderInvoice, cfdiId: 'i-1' },
      body: { uuid: UUID.toLowerCase() },
    });
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    // Read again: resolved, it leaves the list.
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Por revisar en el portal del SAT' })).not.toBeInTheDocument());
  });

  it('refuses one that is not in the portal only once confirmed, in its gender', async () => {
    const user = userEvent.setup();
    const complement = cfdi({ id: 'p-1', kind: IssuedCfdiKind.PaymentComplement, number: 'P-3', paidAmount: 120 });
    renderSection(() => Promise.resolve([complement]), {
      notStamped: () => Promise.resolve({ ...complement, status: OrderInvoiceStatus.Refused }),
    });

    await user.click(await screen.findByRole('button', { name: 'No se timbró el complemento de pago P-3' }));
    let dialog = screen.getByRole('dialog', { name: 'Complemento de pago P-3 no se timbró' });
    expect(dialog).toHaveTextContent('el complemento de pago P-3 queda rechazado con tu nombre');
    await user.click(within(dialog).getByRole('button', { name: 'Volver' }));
    expect(request).not.toHaveBeenCalledWith('post', '/api/v1/invoicing/issued-cfdis/{kind}/{cfdiId}/not-stamped', expect.anything());

    await user.click(screen.getByRole('button', { name: 'No se timbró el complemento de pago P-3' }));
    dialog = screen.getByRole('dialog', { name: 'Complemento de pago P-3 no se timbró' });
    await user.click(within(dialog).getByRole('button', { name: 'No se timbró' }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith(
      'Complemento de pago P-3 queda rechazado: ya puedes facturar otra vez lo que cubría.'));
    expect(request).toHaveBeenCalledWith('post', '/api/v1/invoicing/issued-cfdis/{kind}/{cfdiId}/not-stamped', {
      path: { kind: IssuedCfdiKind.PaymentComplement, cfdiId: 'p-1' },
    });
  });

  it('says when the list could not be read, with a way to try again', async () => {
    const user = userEvent.setup();
    let fail = true;
    renderSection(() => (fail ? Promise.reject(new Error('sin red')) : Promise.resolve([cfdi()])));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('No se pudieron cargar los CFDI por revisar en el portal del SAT.');
    fail = false;
    await user.click(within(alert).getByRole('button', { name: 'Reintentar' }));

    expect(await screen.findByRole('region', { name: 'Por revisar en el portal del SAT' })).toBeInTheDocument();
  });
});
