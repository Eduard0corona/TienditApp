import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { useSendCfdiByEmail, type CfdiEmail } from '../../api/invoicing';
import { useToast } from '../../hooks/useToast';
import { CfdiEmailDocument, CfdiEmailFailure, CfdiEmailKind } from '../../types';
import { cfdiEmailLabel, isPlainEmail } from '../../utils/cfdi';
import { formatDateTime } from '../../utils/dates';

const FAILURES: Record<CfdiEmailFailure, string> = {
  [CfdiEmailFailure.Rejected]: 'el servidor de correo lo rechazó. Revisa la dirección con el cliente.',
  [CfdiEmailFailure.NotConfigured]: 'TienditApp no tiene el envío de correos configurado. Descarga el XML y el PDF y compártelos.',
  [CfdiEmailFailure.NoLongerStamped]: 'ya no está vigente.',
  [CfdiEmailFailure.NotDelivered]: 'el servidor de correo no lo tomó después de varios intentos. Envíalo otra vez.',
};

// The notice that the SAT cancelled it (Fase 28N) is not sent again by hand: the customer hears it another way.
const NOTICE_FAILURES: Record<CfdiEmailFailure, string> = {
  [CfdiEmailFailure.Rejected]: 'el servidor de correo rechazó el aviso. Avísale al cliente por otro medio.',
  [CfdiEmailFailure.NotConfigured]: 'TienditApp no tiene el envío de correos configurado. Avísale al cliente por otro medio.',
  [CfdiEmailFailure.NoLongerStamped]: 'TienditApp no pudo mandar el aviso. Avísale al cliente por otro medio.',
  [CfdiEmailFailure.NotDelivered]: 'el servidor de correo no tomó el aviso después de varios intentos. Avísale al cliente por otro medio.',
};

interface CfdiEmailStatusProps {
  /** The CFDI's latest emails, the newest first. */
  emails: CfdiEmail[];
}

/**
 * How the latest email of a CFDI to the customer went: sent (maybe without the PDF), on its way, or not sent and why. Once
 * the SAT cancels it, and its invoice had an email, the latest is the notice of that (Fase 28N).
 */
export function CfdiEmailStatus({ emails }: CfdiEmailStatusProps) {
  const latest = emails[0];
  if (!latest) return null;
  if (latest.kind === CfdiEmailKind.Cancellation) return <CancellationNoticeStatus notice={latest} earlier={emails.slice(1)} />;

  if (latest.sentAt) {
    return (
      <p className="small text-muted mb-0 mt-1">
        <i className="ri-mail-check-line me-1" aria-hidden="true"></i>
        Se envió por correo a {latest.to} el {formatDateTime(latest.sentAt)}
        {latest.sentWithoutPdf ? ', solo con el XML: el PDF no estuvo disponible.' : '.'}
        {latest.byCustomer && ' Lo pidió el cliente en línea.'}
      </p>
    );
  }
  if (latest.failedAt) {
    return (
      <p className="small text-danger mb-0 mt-1">
        <i className="ri-mail-close-line me-1" aria-hidden="true"></i>
        No se envió a {latest.to}: {latest.failure != null ? FAILURES[latest.failure] : 'no se pudo enviar.'}
        {latest.byCustomer && ' Lo pidió el cliente en línea.'}
      </p>
    );
  }
  return (
    <p className="small text-muted mb-0 mt-1" role="status">
      <i className="ri-mail-send-line me-1" aria-hidden="true"></i>
      Enviándose por correo a {latest.to}…
      {latest.failedAttempts > 0 && ' El servidor de correo no lo ha tomado; TienditApp lo vuelve a intentar.'}
      {latest.byCustomer && ' Lo pidió el cliente en línea.'}
    </p>
  );
}

interface CancellationNoticeStatusProps {
  notice: CfdiEmail;
  /** The CFDI's earlier emails shown, the newest first. */
  earlier: CfdiEmail[];
}

/**
 * How the notice that the SAT cancelled the CFDI went; TienditApp sends it on its own, without files, to the invoice's
 * email. Its three states share one live region, so the outcome is announced when it replaces «Avisando…».
 */
function CancellationNoticeStatus({ notice, earlier }: CancellationNoticeStatusProps) {
  // Whoever got its files at another address hears nothing from TienditApp.
  const others = [...new Set(earlier
    .filter((email) => email.kind === CfdiEmailKind.Documents && email.sentAt && email.to.toLowerCase() !== notice.to.toLowerCase())
    .map((email) => email.to))];
  const alsoTo = others.length > 0 && ` Los archivos también llegaron a ${others.join(' y ')}: avísale por otro medio.`;

  if (notice.sentAt) {
    // «p.m.» already ends the sentence.
    const when = formatDateTime(notice.sentAt);
    return (
      <p className="small text-muted mb-0 mt-1" role="status">
        <i className="ri-mail-check-line me-1" aria-hidden="true"></i>
        Se avisó la cancelación por correo a {notice.to} el {when}{when.endsWith('.') ? '' : '.'}
        {alsoTo}
      </p>
    );
  }
  if (notice.failedAt) {
    return (
      <p className="small text-danger mb-0 mt-1" role="status">
        <i className="ri-mail-close-line me-1" aria-hidden="true"></i>
        No se avisó la cancelación a {notice.to}: {NOTICE_FAILURES[notice.failure ?? CfdiEmailFailure.NoLongerStamped]}
        {alsoTo}
      </p>
    );
  }
  return (
    <p className="small text-muted mb-0 mt-1" role="status">
      <i className="ri-mail-send-line me-1" aria-hidden="true"></i>
      Avisando la cancelación por correo a {notice.to}…
      {notice.failedAttempts > 0 && ' El servidor de correo no ha tomado el aviso; TienditApp lo vuelve a intentar.'}
      {alsoTo}
    </p>
  );
}

interface SendCfdiEmailButtonProps {
  orderId: string;
  document: CfdiEmailDocument;
  cfdiId: string;
  number: string;
  /** Where its invoice's CFDI go now; empty when nowhere. */
  invoiceEmail?: string | null;
  /** The CFDI's latest email, if any. */
  latestEmail?: CfdiEmail;
  /** The address the order's invoicing proposes. */
  proposedEmail?: string | null;
}

/** «Enviar por correo» a stamped CFDI of the order's invoice: to the address typed, out from the queue. */
export function SendCfdiEmailButton({ orderId, document, cfdiId, number, invoiceEmail, latestEmail, proposedEmail }: SendCfdiEmailButtonProps) {
  const [open, setOpen] = useState(false);
  const label = cfdiEmailLabel(document, number);
  return (
    <>
      <button
        type="button"
        className="btn btn-sm btn-outline-secondary"
        aria-label={`Enviar por correo ${label}`}
        onClick={() => setOpen(true)}
      >
        Enviar por correo
      </button>
      {open && (
        <SendCfdiEmailModal
          orderId={orderId}
          document={document}
          cfdiId={cfdiId}
          label={label}
          invoiceEmail={invoiceEmail ?? null}
          invoiceEmailRefused={!!invoiceEmail && latestEmail?.failure === CfdiEmailFailure.Rejected
            && latestEmail.to.toLowerCase() === invoiceEmail.toLowerCase()}
          initialEmail={invoiceEmail || latestEmail?.to || proposedEmail || ''}
          onClose={() => setOpen(false)}
        />
      )}
    </>
  );
}

interface SendCfdiEmailModalProps {
  orderId: string;
  document: CfdiEmailDocument;
  cfdiId: string;
  label: string;
  invoiceEmail: string | null;
  /** The mail server refused the invoice's address for good: what follows should go to the corrected one. */
  invoiceEmailRefused: boolean;
  initialEmail: string;
  onClose: () => void;
}

function SendCfdiEmailModal({ orderId, document, cfdiId, label, invoiceEmail, invoiceEmailRefused, initialEmail, onClose }: SendCfdiEmailModalProps) {
  const titleId = useId();
  const toast = useToast();
  const send = useSendCfdiByEmail(orderId);
  const [email, setEmail] = useState(initialEmail);
  // Without an email in the invoice, or with one that was refused, what comes next of it goes to this one unless the
  // person says otherwise.
  const [useForInvoice, setUseForInvoice] = useState(!invoiceEmail || invoiceEmailRefused);
  const typed = email.trim();
  const valid = isPlainEmail(typed);
  const offerForInvoice = typed.toLowerCase() !== (invoiceEmail ?? '').toLowerCase();

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!valid) return;
    try {
      await send.mutateAsync({ document, cfdiId, email: typed, useForInvoice: offerForInvoice && useForInvoice });
      toast.success(`Se está enviando ${label} a ${typed}.`);
      onClose();
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo enviar el correo.'));
    }
  };

  return (
    <Modal show onHide={onClose} centered aria-labelledby={titleId}>
      <form onSubmit={submit} noValidate>
        <Modal.Header closeButton>
          <Modal.Title id={titleId} as="h2" className="h5">Enviar por correo {label}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <p className="small">Le llegan el XML y el PDF. Si responde el correo, la respuesta llega al correo de tu negocio.</p>
          <label className="form-label small mb-1" htmlFor={`${titleId}-email`}>Correo</label>
          <input
            id={`${titleId}-email`}
            type="email"
            className={`form-control${typed && !valid ? ' is-invalid' : ''}`}
            maxLength={254}
            autoComplete="off"
            required
            disabled={send.isPending}
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            aria-invalid={typed && !valid ? true : undefined}
            aria-describedby={typed && !valid ? `${titleId}-invalid` : undefined}
          />
          {typed && !valid && (
            <div id={`${titleId}-invalid`} className="invalid-feedback">Escribe un correo válido, como nombre@ejemplo.com.</div>
          )}
          {offerForInvoice && (
            <div className="form-check mt-2">
              <input
                id={`${titleId}-for-invoice`}
                type="checkbox"
                className="form-check-input"
                checked={useForInvoice}
                disabled={send.isPending}
                onChange={(event) => setUseForInvoice(event.target.checked)}
              />
              <label className="form-check-label small" htmlFor={`${titleId}-for-invoice`}>
                Usar este correo para lo que siga de esta factura (complementos de pago y notas de crédito)
              </label>
            </div>
          )}
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={onClose}>Cancelar</button>
          <button type="submit" className="btn btn-primary" disabled={!valid || send.isPending}>
            {send.isPending ? 'Enviando…' : 'Enviar'}
          </button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
