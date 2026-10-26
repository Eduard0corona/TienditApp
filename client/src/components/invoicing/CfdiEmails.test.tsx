import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { isOrderInvoicingWaiting, type CfdiEmail } from '../../api/invoicing';
import { apiRequest } from '../../api/typedClient';
import { CfdiEmailDocument, CfdiEmailFailure, OrderInvoiceStatus } from '../../types';
import { isPlainEmail } from '../../utils/cfdi';
import { CfdiEmailStatus, SendCfdiEmailButton } from './CfdiEmails';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
const toast = { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() };
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

const NOW = Date.parse('2026-10-26T18:00:00Z');
const TOO_MANY = 'F-1 ya se envió 5 veces hoy: revisa el correo con el cliente antes de enviarlo otra vez.';

function email(overrides: Partial<CfdiEmail> = {}): CfdiEmail {
  return {
    id: 'e-1', document: CfdiEmailDocument.Invoice, cfdiId: 'inv-1', to: 'karla@ejemplo.mx', requestedAt: '2026-10-26T17:55:00Z',
    byTienditApp: true, sentAt: null, sentWithoutPdf: false, failedAt: null, failure: undefined, failedAttempts: 0,
    lastFailedAttemptAt: null, ...overrides,
  };
}

function renderButton(props: {
  invoiceEmail?: string | null;
  latestEmail?: CfdiEmail;
  proposedEmail?: string | null;
  document?: CfdiEmailDocument;
}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <SendCfdiEmailButton
        orderId="o-1"
        document={props.document ?? CfdiEmailDocument.Invoice}
        cfdiId="cfdi-1"
        number={props.document === CfdiEmailDocument.CreditNote ? 'NC-2' : 'F-1'}
        invoiceEmail={props.invoiceEmail}
        latestEmail={props.latestEmail}
        proposedEmail={props.proposedEmail}
      />
    </QueryClientProvider>,
  );
}

describe('CfdiEmailStatus', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(NOW);
  });

  it('says where and when the latest email went, and when it went without the PDF', () => {
    const { rerender } = render(<CfdiEmailStatus emails={[email({ sentAt: '2026-10-26T17:56:00Z' }), email({ to: 'vieja@ejemplo.mx' })]} />);
    expect(screen.getByText(/Se envió por correo a karla@ejemplo\.mx el .+\.$/)).toBeInTheDocument();
    expect(screen.queryByText(/vieja@ejemplo\.mx/)).not.toBeInTheDocument();

    rerender(<CfdiEmailStatus emails={[email({ sentAt: '2026-10-26T17:56:00Z', sentWithoutPdf: true })]} />);
    expect(screen.getByText(/solo con el XML: el PDF no estuvo disponible\./)).toBeInTheDocument();
  });

  it('says why it was not sent', () => {
    const { rerender } = render(
      <CfdiEmailStatus emails={[email({ failedAt: '2026-10-26T17:56:00Z', failure: CfdiEmailFailure.Rejected })]} />,
    );
    expect(screen.getByText('No se envió a karla@ejemplo.mx: el servidor de correo lo rechazó. Revisa la dirección con el cliente.'))
      .toBeInTheDocument();

    rerender(<CfdiEmailStatus emails={[email({ failedAt: '2026-10-26T17:56:00Z', failure: CfdiEmailFailure.NotConfigured })]} />);
    expect(screen.getByText(/TienditApp no tiene el envío de correos configurado/)).toBeInTheDocument();

    rerender(<CfdiEmailStatus emails={[email({ failedAt: '2026-10-26T22:40:00Z', failure: CfdiEmailFailure.NotDelivered, failedAttempts: 8 })]} />);
    expect(screen.getByText(
      'No se envió a karla@ejemplo.mx: el servidor de correo no lo tomó después de varios intentos. Envíalo otra vez.')).toBeInTheDocument();
  });

  it('shows one on its way, and the tries the mail server did not take', () => {
    const { rerender } = render(<CfdiEmailStatus emails={[email()]} />);
    expect(screen.getByRole('status')).toHaveTextContent(/^Enviándose por correo a karla@ejemplo\.mx…$/);

    // Hours later it is still on its way while the queue tries it: it ends as not sent after its last attempt.
    rerender(<CfdiEmailStatus emails={[email({ requestedAt: '2026-10-26T14:00:00Z', failedAttempts: 3 })]} />);
    expect(screen.getByRole('status')).toHaveTextContent(
      'Enviándose por correo a karla@ejemplo.mx… El servidor de correo no lo ha tomado; TienditApp lo vuelve a intentar.');
  });

  it('keeps reading the invoicing while the latest email is on its way, until it ends', () => {
    const waiting = (latest: CfdiEmail) => isOrderInvoicingWaiting({
      invoices: [{ status: OrderInvoiceStatus.Stamped, emails: [latest] }] as never, payments: [], adjustments: [],
    });
    expect(waiting(email({ requestedAt: '2026-10-26T14:00:00Z', failedAttempts: 5 }))).toBe(true);
    expect(waiting(email({ sentAt: '2026-10-26T17:56:00Z' }))).toBe(false);
    expect(waiting(email({ failedAt: '2026-10-26T17:56:00Z', failure: CfdiEmailFailure.NotDelivered }))).toBe(false);
  });
});

describe('SendCfdiEmailButton', () => {
  beforeEach(() => {
    vi.useRealTimers();
    vi.resetAllMocks();
  });

  it('sends the invoice to the address typed, and keeps it for what follows when the invoice had none', async () => {
    vi.mocked(apiRequest).mockResolvedValue(email({ to: 'contabilidad@ejemplo.mx', byTienditApp: false }) as never);
    renderButton({ invoiceEmail: null, proposedEmail: 'karla@ejemplo.mx' });

    await userEvent.click(screen.getByRole('button', { name: 'Enviar por correo la factura F-1' }));
    const input = screen.getByLabelText('Correo');
    expect(input).toHaveValue('karla@ejemplo.mx');
    await userEvent.clear(input);
    await userEvent.type(input, ' contabilidad@ejemplo.mx ');
    expect(screen.getByRole('checkbox', { name: /Usar este correo para lo que siga de esta factura/ })).toBeChecked();
    await userEvent.click(screen.getByRole('button', { name: 'Enviar' }));

    await waitFor(() => expect(apiRequest).toHaveBeenCalledWith('post', '/api/v1/invoicing/invoices/{invoiceId}/email', {
      path: { invoiceId: 'cfdi-1' },
      body: { email: 'contabilidad@ejemplo.mx', useForInvoice: true },
    }));
    expect(toast.success).toHaveBeenCalledWith('Se está enviando la factura F-1 a contabilidad@ejemplo.mx.');
  });

  it('sends a credit note to its invoice email without offering to keep it, and refuses what is not an address', async () => {
    vi.mocked(apiRequest).mockResolvedValue(email() as never);
    renderButton({ document: CfdiEmailDocument.CreditNote, invoiceEmail: 'karla@ejemplo.mx' });

    await userEvent.click(screen.getByRole('button', { name: 'Enviar por correo la nota de crédito NC-2' }));
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    const input = screen.getByLabelText('Correo');
    await userEvent.clear(input);
    await userEvent.type(input, 'karla@ejemplo');
    expect(screen.getByText('Escribe un correo válido, como nombre@ejemplo.com.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Enviar' })).toBeDisabled();

    await userEvent.type(input, '.mx');
    await userEvent.click(screen.getByRole('button', { name: 'Enviar' }));
    await waitFor(() => expect(apiRequest).toHaveBeenCalledWith('post', '/api/v1/invoicing/credit-notes/{creditNoteId}/email', {
      path: { creditNoteId: 'cfdi-1' },
      body: { email: 'karla@ejemplo.mx', useForInvoice: false },
    }));
  });

  it('offers to keep the corrected address when the mail server refused the invoice email', async () => {
    vi.mocked(apiRequest).mockResolvedValue(email({ to: 'karla@ejemplo.mx' }) as never);
    const refused = email({ to: 'karla@ejemplo.com.mxx', failedAt: '2026-10-26T17:56:00Z', failure: CfdiEmailFailure.Rejected });
    renderButton({ invoiceEmail: 'Karla@ejemplo.com.mxx', latestEmail: refused, proposedEmail: 'otra@ejemplo.mx' });

    await userEvent.click(screen.getByRole('button', { name: 'Enviar por correo la factura F-1' }));
    const input = screen.getByLabelText('Correo');
    expect(input).toHaveValue('Karla@ejemplo.com.mxx');
    await userEvent.clear(input);
    await userEvent.type(input, 'karla@ejemplo.mx');
    expect(screen.getByRole('checkbox', { name: /Usar este correo para lo que siga de esta factura/ })).toBeChecked();
    await userEvent.click(screen.getByRole('button', { name: 'Enviar' }));

    await waitFor(() => expect(apiRequest).toHaveBeenCalledWith('post', '/api/v1/invoicing/invoices/{invoiceId}/email', {
      path: { invoiceId: 'cfdi-1' },
      body: { email: 'karla@ejemplo.mx', useForInvoice: true },
    }));
  });

  it('starts from the latest address without an invoice email, and leaves keeping a working one unchecked', async () => {
    const first = renderButton({ invoiceEmail: null, latestEmail: email({ to: 'ultima@ejemplo.mx' }), proposedEmail: 'karla@ejemplo.mx' });
    await userEvent.click(screen.getByRole('button', { name: 'Enviar por correo la factura F-1' }));
    expect(screen.getByLabelText('Correo')).toHaveValue('ultima@ejemplo.mx');
    first.unmount();

    renderButton({ invoiceEmail: 'karla@ejemplo.mx', latestEmail: email({ sentAt: '2026-10-26T17:56:00Z' }) });
    await userEvent.click(screen.getByRole('button', { name: 'Enviar por correo la factura F-1' }));
    await userEvent.clear(screen.getByLabelText('Correo'));
    await userEvent.type(screen.getByLabelText('Correo'), 'contabilidad@ejemplo.mx');
    expect(screen.getByRole('checkbox', { name: /Usar este correo para lo que siga de esta factura/ })).not.toBeChecked();
  });

  it('says what the API refused, such as too many emails today', async () => {
    const headers = new AxiosHeaders();
    vi.mocked(apiRequest).mockRejectedValue(new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, {
      status: 400, data: { detail: TOO_MANY }, statusText: '', headers, config: { headers },
    } as AxiosResponse));
    renderButton({ invoiceEmail: 'karla@ejemplo.mx' });

    await userEvent.click(screen.getByRole('button', { name: 'Enviar por correo la factura F-1' }));
    await userEvent.click(screen.getByRole('button', { name: 'Enviar' }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith(TOO_MANY));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });
});

describe('isPlainEmail', () => {
  it('takes one plain address only', () => {
    expect(isPlainEmail(' karla.fuente+facturas@sub.ejemplo.com.mx ')).toBe(true);
    for (const text of ['karla', 'karla@ejemplo', 'karla@ejemplo.mx.', 'karla fuente@ejemplo.mx', 'Karla <karla@ejemplo.mx>', 'a@b.mx, c@d.mx'])
      expect(isPlainEmail(text)).toBe(false);
  });

  it('takes ASCII only: the mail server gets seven-bit addresses', () => {
    for (const text of ['peña@ejemplo.mx', 'karla@peña.mx', 'karla\t@ejemplo.mx'])
      expect(isPlainEmail(text)).toBe(false);
  });
});
