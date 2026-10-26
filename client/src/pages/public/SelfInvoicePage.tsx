import { useEffect, useRef, useState } from 'react';
import { useLocation, useParams } from 'react-router-dom';
import { useMutation, useQuery } from '@tanstack/react-query';
import { isAxiosError } from 'axios';
import { getApiErrorMessage } from '../../api/errors';
import {
  selfInvoiceApi,
  ticketKeyFromHash,
  type SelfInvoiceResult,
  type SelfInvoiceTicket,
  type TicketKey,
} from '../../api/selfInvoiceApi';
import PageTitle from '../../components/common/PageTitle';
import CfdiReceiverFields from '../../components/invoicing/CfdiReceiverFields';
import { SelfInvoiceOutcome, SelfInvoiceTicketStatus } from '../../api/generated/enums';
import type { CustomerFiscalData } from '../../types';
import { EMPTY_RECEIVER, isPlainEmail, normalizeReceiver } from '../../utils/cfdi';
import { formatCalendarDate, formatDate } from '../../utils/dates';
import { formatMoney } from '../../utils/money';

/**
 * The page where a business's customers invoice their tickets (Fase 28F, ADR 0050), without an account: the folio and the
 * code printed on the ticket (the QR brings them in the address's fragment), then the customer's fiscal data and email.
 * The XML and the PDF arrive by email. A ticket already invoiced has its invoice sent again to whoever knows its RFC.
 */
export default function SelfInvoicePage() {
  const { slug = '' } = useParams();
  const fromQr = ticketKeyFromHash(useLocation().hash);
  const pageQuery = useQuery({
    queryKey: ['public', 'self-invoice', slug],
    queryFn: ({ signal }) => selfInvoiceApi.getPage(slug, signal),
    retry: false,
  });
  const page = pageQuery.data;
  const [key, setKey] = useState<TicketKey>(fromQr);
  const [ticket, setTicket] = useState<{ key: TicketKey; value: SelfInvoiceTicket } | null>(null);
  const lookUp = useMutation({
    mutationFn: (typed: TicketKey) => selfInvoiceApi.lookUp(slug, typed),
    onSuccess: (value, typed) => setTicket({ key: typed, value }),
    onMutate: () => setTicket(null),
  });

  // A ticket read with its QR is looked up as soon as the page knows the business invoices online.
  const lookedUpFromQr = useRef(false);
  useEffect(() => {
    if (page?.available && fromQr.folio && fromQr.code && !lookedUpFromQr.current) {
      lookedUpFromQr.current = true;
      lookUp.mutate(fromQr);
    }
  }, [page?.available, fromQr, lookUp]);

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    lookUp.mutate({ folio: key.folio.trim(), code: key.code.trim() });
  };

  return (
    <div className="cl-page">
      <PageTitle title="Factura tu compra" />
      {page && <p className="text-muted mb-3">{page.businessName}</p>}

      {pageQuery.isLoading && (
        <div className="text-center py-5"><div className="spinner-border text-primary" role="status"><span className="visually-hidden">Cargando</span></div></div>
      )}
      {pageQuery.error && (
        <div className="alert alert-warning">
          {isAxiosError(pageQuery.error) && pageQuery.error.response?.status === 404
            ? 'No encontramos ese negocio: revisa la dirección impresa en tu ticket.'
            : getApiErrorMessage(pageQuery.error, 'No se pudo abrir la página para facturar.')}
        </div>
      )}
      {page && !page.available && (
        <div className="alert alert-info">Este negocio no factura en línea por ahora: pídele tu factura directamente.</div>
      )}

      {page?.available && (
        <div className="row g-3">
          <div className="col-12 col-lg-5">
            <div className="card mb-0">
              <div className="card-body">
                <h2 className="h6 mb-1">Tu ticket</h2>
                <p className="text-muted small">Escribe el folio y el código impresos en tu ticket, o escanea su código QR.</p>
                {page.sandbox && (
                  <div className="alert alert-warning small py-2">Modo de prueba: las facturas de esta página no tienen validez fiscal.</div>
                )}
                <form onSubmit={submit} noValidate>
                  <div className="mb-2">
                    <label className="form-label small mb-1" htmlFor="si-folio">Folio</label>
                    <input id="si-folio" className="form-control text-uppercase" placeholder="ORD-000123" maxLength={40} required
                      autoComplete="off" value={key.folio} onChange={(e) => setKey({ ...key, folio: e.target.value })} />
                  </div>
                  <div className="mb-3">
                    <label className="form-label small mb-1" htmlFor="si-code">Código</label>
                    <input id="si-code" className="form-control text-uppercase" placeholder="ABCDE-12345" maxLength={40} required
                      autoComplete="off" value={key.code} onChange={(e) => setKey({ ...key, code: e.target.value })} />
                  </div>
                  {lookUp.error && (
                    <div className="alert alert-warning small py-2" role="alert">
                      {getApiErrorMessage(lookUp.error, 'No se pudo buscar el ticket.')}
                    </div>
                  )}
                  <button type="submit" className="btn btn-primary w-100" disabled={lookUp.isPending || !key.folio.trim() || !key.code.trim()}>
                    {lookUp.isPending ? 'Buscando…' : 'Buscar ticket'}
                  </button>
                </form>
              </div>
            </div>
          </div>

          {ticket && (
            <div className="col-12 col-lg-7">
              <TicketCard slug={slug} ticketKey={ticket.key} ticket={ticket.value} />
            </div>
          )}
        </div>
      )}
    </div>
  );
}

function TicketCard({ slug, ticketKey, ticket }: { slug: string; ticketKey: TicketKey; ticket: SelfInvoiceTicket }) {
  return (
    <div className="card mb-0">
      <div className="card-body">
        <div className="d-flex justify-content-between align-items-start gap-2 mb-3">
          <div>
            <h2 className="h6 mb-0">Ticket {ticket.orderNumber}</h2>
            <div className="text-muted small">{formatDate(ticket.date)}</div>
          </div>
          <div className="fw-semibold">{formatMoney(ticket.total)}</div>
        </div>

        {ticket.status === SelfInvoiceTicketStatus.CanInvoice && (
          <InvoiceForm slug={slug} ticketKey={ticketKey} invoiceBy={ticket.invoiceBy} />
        )}
        {ticket.status === SelfInvoiceTicketStatus.Invoiced && (
          <ResendForm slug={slug} ticketKey={ticketKey} invoiceNumber={ticket.invoiceNumber!} />
        )}
        {ticket.status === SelfInvoiceTicketStatus.Stamping && (
          <div className="alert alert-info mb-0">
            La factura {ticket.invoiceNumber} de este ticket se está timbrando: llegará al correo que escribiste.
          </div>
        )}
        {ticket.status === SelfInvoiceTicketStatus.NotOnline && (
          <div className="alert alert-warning mb-0">{ticket.message}</div>
        )}
      </div>
    </div>
  );
}

function InvoiceForm({ slug, ticketKey, invoiceBy }: { slug: string; ticketKey: TicketKey; invoiceBy?: string | null }) {
  const [receiver, setReceiver] = useState<CustomerFiscalData>(EMPTY_RECEIVER);
  const [email, setEmail] = useState('');
  const invoice = useMutation({
    mutationFn: () => selfInvoiceApi.invoice(slug, ticketKey, normalizeReceiver(receiver), email.trim()),
  });
  const result = invoice.data;
  const emailInvalid = email.trim().length > 0 && !isPlainEmail(email.trim());
  const done = result && result.outcome !== SelfInvoiceOutcome.Refused;

  if (done) return <InvoiceResult result={result} />;

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    invoice.mutate();
  };

  return (
    <form onSubmit={submit} noValidate>
      <p className="small mb-3">
        Escribe tus datos como aparecen en tu constancia de situación fiscal.
        {invoiceBy && <> Puedes facturar esta compra hasta el {formatCalendarDate(invoiceBy)}.</>}
      </p>
      <CfdiReceiverFields idPrefix="si-receiver" value={receiver} onChange={setReceiver} disabled={invoice.isPending} />
      <div className="mt-2 mb-3">
        <label className="form-label small mb-1" htmlFor="si-email">Correo para recibir la factura</label>
        <input id="si-email" type="email" className={`form-control${emailInvalid ? ' is-invalid' : ''}`} maxLength={254} required
          autoComplete="email" aria-invalid={emailInvalid} disabled={invoice.isPending}
          value={email} onChange={(e) => setEmail(e.target.value)} />
        {emailInvalid && <div className="invalid-feedback">Revisa el correo: debe ser como nombre@ejemplo.com.</div>}
      </div>
      {result?.outcome === SelfInvoiceOutcome.Refused && (
        <div className="alert alert-danger small" role="alert">
          El SAT no aceptó la factura {result.invoiceNumber}: {result.message} Corrige tus datos y vuelve a intentarlo.
        </div>
      )}
      {invoice.error && (
        <div className="alert alert-warning small" role="alert">{getApiErrorMessage(invoice.error, 'No se pudo facturar el ticket.')}</div>
      )}
      <button type="submit" className="btn btn-primary"
        disabled={invoice.isPending || !receiver.rfc.trim() || !receiver.name.trim() || !receiver.postalCode.trim() || !email.trim() || emailInvalid}>
        {invoice.isPending ? 'Facturando…' : 'Facturar'}
      </button>
    </form>
  );
}

function InvoiceResult({ result }: { result: SelfInvoiceResult }) {
  return result.outcome === SelfInvoiceOutcome.Stamped ? (
    <div className="alert alert-success mb-0" role="status">
      <strong>Listo: tu factura {result.invoiceNumber} está timbrada.</strong> Te llegará a {result.email} con el XML y el PDF.
    </div>
  ) : (
    <div className="alert alert-info mb-0" role="status">
      Tu factura {result.invoiceNumber} se está timbrando: te llegará a {result.email} en cuanto quede.
    </div>
  );
}

function ResendForm({ slug, ticketKey, invoiceNumber }: { slug: string; ticketKey: TicketKey; invoiceNumber: string }) {
  const [rfc, setRfc] = useState('');
  const [email, setEmail] = useState('');
  const resend = useMutation({ mutationFn: () => selfInvoiceApi.resend(slug, ticketKey, rfc.trim(), email.trim()) });
  const emailInvalid = email.trim().length > 0 && !isPlainEmail(email.trim());

  if (resend.data) {
    return (
      <div className="alert alert-success mb-0" role="status">
        Enviamos la factura {resend.data.invoiceNumber} a {resend.data.email}.
      </div>
    );
  }

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    resend.mutate();
  };

  return (
    <form onSubmit={submit} noValidate>
      <p className="small mb-3">
        Este ticket ya tiene la factura {invoiceNumber}. Si no te llegó, escribe el RFC de la factura y el correo al que la
        enviamos.
      </p>
      <div className="row g-2 mb-3">
        <div className="col-md-5">
          <label className="form-label small mb-1" htmlFor="si-resend-rfc">RFC de la factura</label>
          <input id="si-resend-rfc" className="form-control text-uppercase" maxLength={13} required autoComplete="off"
            disabled={resend.isPending} value={rfc} onChange={(e) => setRfc(e.target.value)} />
        </div>
        <div className="col-md-7">
          <label className="form-label small mb-1" htmlFor="si-resend-email">Correo</label>
          <input id="si-resend-email" type="email" className={`form-control${emailInvalid ? ' is-invalid' : ''}`} maxLength={254}
            required autoComplete="email" aria-invalid={emailInvalid} disabled={resend.isPending}
            value={email} onChange={(e) => setEmail(e.target.value)} />
          {emailInvalid && <div className="invalid-feedback">Revisa el correo: debe ser como nombre@ejemplo.com.</div>}
        </div>
      </div>
      {resend.error && (
        <div className="alert alert-warning small" role="alert">{getApiErrorMessage(resend.error, 'No se pudo reenviar la factura.')}</div>
      )}
      <button type="submit" className="btn btn-outline-primary"
        disabled={resend.isPending || !rfc.trim() || !email.trim() || emailInvalid}>
        {resend.isPending ? 'Enviando…' : 'Reenviar factura'}
      </button>
    </form>
  );
}
