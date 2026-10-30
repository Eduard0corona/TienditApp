import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { useSendCfdiByWhatsApp, type CfdiWhatsApp } from '../../api/invoicing';
import { useToast } from '../../hooks/useToast';
import { CfdiEmailDocument, OutgoingMessageStatus } from '../../types';
import { cfdiEmailLabel } from '../../utils/cfdi';
import { formatDateTime } from '../../utils/dates';
import { isSamePhone, isTypedPhone, localPhone } from '../../utils/phones';

interface CfdiWhatsAppStatusProps {
  /** The CFDI's latest WhatsApp messages, the newest first. */
  messages: CfdiWhatsApp[];
}

/** How the latest WhatsApp of a CFDI to the customer went (Fase 28M): read, delivered, sent, on its way, or not sent and why. */
export function CfdiWhatsAppStatus({ messages }: CfdiWhatsAppStatusProps) {
  const latest = messages[0];
  if (!latest) return null;
  const phone = localPhone(latest.phone);

  switch (latest.status) {
    case OutgoingMessageStatus.Sent:
      return (
        <p className="small text-muted mb-0 mt-1">
          <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>
          {latest.readAt
            ? `Se leyó por WhatsApp (${phone}) el ${formatDateTime(latest.readAt)}.`
            : latest.deliveredAt
              ? `Se entregó por WhatsApp a ${phone} el ${formatDateTime(latest.deliveredAt)}.`
              : `Se envió por WhatsApp a ${phone}${latest.sentAt ? ` el ${formatDateTime(latest.sentAt)}` : ''}.`}
        </p>
      );
    case OutgoingMessageStatus.Pending:
      return (
        <p className="small text-muted mb-0 mt-1" role="status">
          <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>
          Enviándose por WhatsApp a {phone}…
          {latest.attempts > 0 && ' No ha salido todavía; TienditApp lo vuelve a intentar.'}
        </p>
      );
    case OutgoingMessageStatus.Cancelled:
      return (
        <p className="small text-muted mb-0 mt-1">
          <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>
          Se canceló el envío por WhatsApp a {phone}.
        </p>
      );
    default:
      return (
        <p className="small text-danger mb-0 mt-1">
          <i className="ri-whatsapp-line me-1" aria-hidden="true"></i>
          No se envió por WhatsApp a {phone}. {latest.error ?? 'Envíalo otra vez o descarga el XML y el PDF.'}
        </p>
      );
  }
}

interface SendCfdiWhatsAppButtonProps {
  orderId: string;
  document: CfdiEmailDocument;
  cfdiId: string;
  number: string;
  /** The WhatsApp its invoice's CFDI go to now; empty when none. */
  invoiceWhatsApp?: string | null;
  /** The CFDI's latest WhatsApp message, if any. */
  latestWhatsApp?: CfdiWhatsApp;
  /** The number the order's invoicing proposes. */
  proposedWhatsApp?: string | null;
}

/** «Enviar por WhatsApp» a stamped CFDI of the order's invoice (Fase 28M): its PDF and its link, to the number typed. */
export function SendCfdiWhatsAppButton({
  orderId, document, cfdiId, number, invoiceWhatsApp, latestWhatsApp, proposedWhatsApp,
}: SendCfdiWhatsAppButtonProps) {
  const [open, setOpen] = useState(false);
  const label = cfdiEmailLabel(document, number);
  return (
    <>
      <button
        type="button"
        className="btn btn-sm btn-outline-secondary"
        aria-label={`Enviar por WhatsApp ${label}`}
        onClick={() => setOpen(true)}
      >
        Enviar por WhatsApp
      </button>
      {open && (
        <SendCfdiWhatsAppModal
          orderId={orderId}
          document={document}
          cfdiId={cfdiId}
          label={label}
          invoiceWhatsApp={invoiceWhatsApp ?? null}
          initialPhone={localPhone(invoiceWhatsApp || latestWhatsApp?.phone || proposedWhatsApp || '')}
          onClose={() => setOpen(false)}
        />
      )}
    </>
  );
}

interface SendCfdiWhatsAppModalProps {
  orderId: string;
  document: CfdiEmailDocument;
  cfdiId: string;
  label: string;
  invoiceWhatsApp: string | null;
  initialPhone: string;
  onClose: () => void;
}

function SendCfdiWhatsAppModal({ orderId, document, cfdiId, label, invoiceWhatsApp, initialPhone, onClose }: SendCfdiWhatsAppModalProps) {
  const titleId = useId();
  const toast = useToast();
  const send = useSendCfdiByWhatsApp(orderId);
  const [phone, setPhone] = useState(initialPhone);
  // Without a WhatsApp in the invoice, what comes next of it goes to this one unless the person says otherwise.
  const [useForInvoice, setUseForInvoice] = useState(!invoiceWhatsApp);
  const typed = phone.trim();
  const valid = isTypedPhone(typed);
  const offerForInvoice = !isSamePhone(typed, invoiceWhatsApp);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!valid) return;
    try {
      await send.mutateAsync({ document, cfdiId, phone: typed, useForInvoice: offerForInvoice && useForInvoice });
      toast.success(`Se está enviando ${label} por WhatsApp a ${typed}.`);
      onClose();
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo enviar por WhatsApp.'));
    }
  };

  return (
    <Modal show onHide={onClose} centered aria-labelledby={titleId}>
      <form onSubmit={submit} noValidate>
        <Modal.Header closeButton>
          <Modal.Title id={titleId} as="h2" className="h5">Enviar por WhatsApp {label}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <p className="small">Le llega el PDF con un enlace para descargar el XML, que funciona 90 días.</p>
          <label className="form-label small mb-1" htmlFor={`${titleId}-phone`}>WhatsApp</label>
          <input
            id={`${titleId}-phone`}
            type="tel"
            className={`form-control${typed && !valid ? ' is-invalid' : ''}`}
            maxLength={30}
            autoComplete="off"
            placeholder="10 dígitos"
            required
            disabled={send.isPending}
            value={phone}
            onChange={(event) => setPhone(event.target.value)}
            aria-invalid={typed && !valid ? true : undefined}
            aria-describedby={`${titleId}-help`}
          />
          {typed && !valid ? (
            <div id={`${titleId}-help`} className="invalid-feedback">
              Escribe los 10 dígitos, o el número con lada internacional (por ejemplo +1 415 555 0123).
            </div>
          ) : (
            <div id={`${titleId}-help`} className="form-text">10 dígitos si es de México; de otro país, con su lada internacional.</div>
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
                Usar este WhatsApp para lo que siga de esta factura (complementos de pago y notas de crédito)
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
