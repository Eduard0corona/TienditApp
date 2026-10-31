import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { isOrderInvoicingWaiting, type CfdiWhatsApp } from '../../api/invoicing';
import { apiRequest } from '../../api/typedClient';
import { CfdiEmailDocument, OrderInvoiceStatus, OutgoingMessageStatus } from '../../types';
import { formatDateTime } from '../../utils/dates';
import { CfdiWhatsAppStatus, SendCfdiWhatsAppButton } from './CfdiWhatsApps';

vi.mock('../../api/typedClient', () => ({ apiRequest: vi.fn() }));
const toast = { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() };
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));

const NOT_CONNECTED = 'Conecta el WhatsApp de tu negocio en Configuración › WhatsApp para enviar facturas por WhatsApp.';

function message(overrides: Partial<CfdiWhatsApp> = {}): CfdiWhatsApp {
  return {
    id: 'm-1', document: CfdiEmailDocument.Invoice, cfdiId: 'inv-1', phone: '+525512345678', requestedAt: '2026-10-30T17:55:00Z',
    byTienditApp: true, status: OutgoingMessageStatus.Pending, sentAt: null, deliveredAt: null, readAt: null, attempts: 0, error: null,
    ...overrides,
  };
}

function renderButton(props: {
  invoiceWhatsApp?: string | null;
  latestWhatsApp?: CfdiWhatsApp;
  proposedWhatsApp?: string | null;
  document?: CfdiEmailDocument;
}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <SendCfdiWhatsAppButton
        orderId="o-1"
        document={props.document ?? CfdiEmailDocument.Invoice}
        cfdiId="cfdi-1"
        number={props.document === CfdiEmailDocument.PaymentComplement ? 'P-3' : 'F-1'}
        invoiceWhatsApp={props.invoiceWhatsApp}
        latestWhatsApp={props.latestWhatsApp}
        proposedWhatsApp={props.proposedWhatsApp}
      />
    </QueryClientProvider>,
  );
}

describe('CfdiWhatsAppStatus', () => {
  it('says how far the latest message got: sent, delivered or read, with the number as a person in Mexico writes it', () => {
    const { rerender, container } = render(<CfdiWhatsAppStatus messages={[]} />);
    expect(container).toBeEmptyDOMElement();

    // «a.m.» or «p.m.» already ends the sentence: it takes no second period.
    const when = (at: string) => {
      const text = formatDateTime(at);
      expect(text).toMatch(/m\.$/);
      return text;
    };
    const status = (start: RegExp) => screen.getByText(start).textContent;

    rerender(<CfdiWhatsAppStatus messages={[
      message({ status: OutgoingMessageStatus.Sent, sentAt: '2026-10-30T17:56:00Z' }), message({ phone: '+525598765432' }),
    ]} />);
    expect(status(/^Se envió por WhatsApp/)).toBe(`Se envió por WhatsApp a 5512345678 el ${when('2026-10-30T17:56:00Z')}`);
    expect(screen.queryByText(/5598765432/)).not.toBeInTheDocument();

    rerender(<CfdiWhatsAppStatus messages={[message({ status: OutgoingMessageStatus.Sent })]} />);
    expect(status(/^Se envió por WhatsApp/)).toBe('Se envió por WhatsApp a 5512345678.');

    rerender(<CfdiWhatsAppStatus messages={[message({
      status: OutgoingMessageStatus.Sent, sentAt: '2026-10-30T17:56:00Z', deliveredAt: '2026-10-30T17:57:00Z',
    })]} />);
    expect(status(/^Se entregó por WhatsApp/)).toBe(`Se entregó por WhatsApp a 5512345678 el ${when('2026-10-30T17:57:00Z')}`);

    rerender(<CfdiWhatsAppStatus messages={[message({
      phone: '+14155550123', status: OutgoingMessageStatus.Sent, sentAt: '2026-10-30T17:56:00Z', deliveredAt: '2026-10-30T17:57:00Z',
      readAt: '2026-10-30T18:10:00Z',
    })]} />);
    expect(status(/^Se leyó por WhatsApp/)).toBe(`Se leyó por WhatsApp (+14155550123) el ${when('2026-10-30T18:10:00Z')}`);
  });

  it('says, once the SAT cancelled the CFDI, that WhatsApp does not tell the customer (Fase 28N)', () => {
    const read = message({ status: OutgoingMessageStatus.Sent, sentAt: '2026-10-30T17:56:00Z', readAt: '2026-10-30T18:01:00Z' });
    const { rerender } = render(<CfdiWhatsAppStatus messages={[read]} cancelled />);
    expect(screen.getByText(/^Se leyó por WhatsApp \(5512345678\) el .+ Por WhatsApp no se avisa la cancelación: avísale por otro medio\.$/))
      .toBeInTheDocument();

    rerender(<CfdiWhatsAppStatus messages={[read]} />);
    expect(screen.queryByText(/no se avisa la cancelación/)).not.toBeInTheDocument();
  });

  it('shows one on its way, the tries WhatsApp did not take, and why one was not sent', () => {
    const { rerender } = render(<CfdiWhatsAppStatus messages={[message()]} />);
    expect(screen.getByRole('status')).toHaveTextContent(/^Enviándose por WhatsApp a 5512345678…$/);

    rerender(<CfdiWhatsAppStatus messages={[message({ attempts: 2, error: 'Error al enviar mensaje de WhatsApp: TooManyRequests' })]} />);
    expect(screen.getByRole('status')).toHaveTextContent(
      'Enviándose por WhatsApp a 5512345678… No ha salido todavía; TienditApp lo vuelve a intentar.');

    rerender(<CfdiWhatsAppStatus messages={[message({ status: OutgoingMessageStatus.Skipped, error: 'El CFDI ya no está vigente.' })]} />);
    expect(screen.getByText('No se envió por WhatsApp a 5512345678. El CFDI ya no está vigente.')).toBeInTheDocument();

    rerender(<CfdiWhatsAppStatus messages={[message({ status: OutgoingMessageStatus.Failed, error: null })]} />);
    expect(screen.getByText('No se envió por WhatsApp a 5512345678. Envíalo otra vez o descarga el XML y el PDF.')).toBeInTheDocument();
  });

  it('keeps reading the invoicing while the latest message is in the queue, until it ends', () => {
    const waiting = (latest: CfdiWhatsApp) => isOrderInvoicingWaiting({
      invoices: [{ status: OrderInvoiceStatus.Stamped, emails: [], whatsApps: [latest] }] as never, payments: [], adjustments: [],
    });
    expect(waiting(message({ attempts: 3 }))).toBe(true);
    expect(waiting(message({ status: OutgoingMessageStatus.Sent, sentAt: '2026-10-30T17:56:00Z' }))).toBe(false);
    expect(waiting(message({ status: OutgoingMessageStatus.Failed, error: 'No se pudo' }))).toBe(false);
  });
});

describe('SendCfdiWhatsAppButton', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  it('sends the invoice to the number typed, and keeps it for what follows when the invoice had none', async () => {
    vi.mocked(apiRequest).mockResolvedValue(message({ byTienditApp: false }) as never);
    renderButton({ invoiceWhatsApp: null, proposedWhatsApp: '+525512345678' });

    await userEvent.click(screen.getByRole('button', { name: 'Enviar por WhatsApp la factura F-1' }));
    const input = screen.getByLabelText('WhatsApp');
    expect(input).toHaveValue('5512345678');
    await userEvent.clear(input);
    await userEvent.type(input, ' 55 9876 5432 ');
    expect(screen.getByRole('checkbox', { name: /Usar este WhatsApp para lo que siga de esta factura/ })).toBeChecked();
    await userEvent.click(screen.getByRole('button', { name: 'Enviar' }));

    await waitFor(() => expect(apiRequest).toHaveBeenCalledWith('post', '/api/v1/invoicing/invoices/{invoiceId}/whatsapp', {
      path: { invoiceId: 'cfdi-1' },
      body: { phone: '55 9876 5432', useForInvoice: true },
    }));
    expect(toast.success).toHaveBeenCalledWith('Se está enviando la factura F-1 por WhatsApp a 55 9876 5432.');
  });

  it('sends a complement to its invoice number without offering to keep it, and refuses what is not a number', async () => {
    vi.mocked(apiRequest).mockResolvedValue(message() as never);
    renderButton({ document: CfdiEmailDocument.PaymentComplement, invoiceWhatsApp: '+525512345678' });

    await userEvent.click(screen.getByRole('button', { name: 'Enviar por WhatsApp el complemento de pago P-3' }));
    const input = screen.getByLabelText('WhatsApp');
    expect(input).toHaveValue('5512345678');
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    await userEvent.clear(input);
    await userEvent.type(input, '551234567');
    expect(screen.getByText(/Escribe los 10 dígitos, o el número con lada internacional/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Enviar' })).toBeDisabled();

    // The same number written another way is still the invoice's.
    await userEvent.clear(input);
    await userEvent.type(input, '+52 55 1234 5678');
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Enviar' }));
    await waitFor(() => expect(apiRequest).toHaveBeenCalledWith('post', '/api/v1/invoicing/payment-complements/{complementId}/whatsapp', {
      path: { complementId: 'cfdi-1' },
      body: { phone: '+52 55 1234 5678', useForInvoice: false },
    }));
  });

  it('starts from the latest number without one in the invoice, and leaves keeping another unchecked when it has one', async () => {
    const first = renderButton({ invoiceWhatsApp: null, latestWhatsApp: message({ phone: '+14155550123' }), proposedWhatsApp: '+525512345678' });
    await userEvent.click(screen.getByRole('button', { name: 'Enviar por WhatsApp la factura F-1' }));
    expect(screen.getByLabelText('WhatsApp')).toHaveValue('+14155550123');
    first.unmount();

    renderButton({ invoiceWhatsApp: '+525512345678' });
    await userEvent.click(screen.getByRole('button', { name: 'Enviar por WhatsApp la factura F-1' }));
    await userEvent.clear(screen.getByLabelText('WhatsApp'));
    await userEvent.type(screen.getByLabelText('WhatsApp'), '5598765432');
    expect(screen.getByRole('checkbox', { name: /Usar este WhatsApp para lo que siga de esta factura/ })).not.toBeChecked();
  });

  it('says what the API refused, such as the WhatsApp of the business no longer connected', async () => {
    const headers = new AxiosHeaders();
    vi.mocked(apiRequest).mockRejectedValue(new AxiosError('Request failed', 'ERR_BAD_REQUEST', { headers }, null, {
      status: 400, data: { detail: NOT_CONNECTED }, statusText: '', headers, config: { headers },
    } as AxiosResponse));
    renderButton({ invoiceWhatsApp: '+525512345678' });

    await userEvent.click(screen.getByRole('button', { name: 'Enviar por WhatsApp la factura F-1' }));
    await userEvent.click(screen.getByRole('button', { name: 'Enviar' }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith(NOT_CONNECTED));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });
});
