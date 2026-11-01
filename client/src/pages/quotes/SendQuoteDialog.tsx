import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { useMutation } from '@tanstack/react-query';
import { getApiErrorMessage } from '../../api/errors';
import { quotesApi, type Quote } from '../../api/quotesApi';
import { quoteFolio } from '../../utils/quotes';

export type QuoteChannel = 'email' | 'whatsapp';

interface SendQuoteDialogProps {
  quote: Quote;
  channel: QuoteChannel;
  onClose: () => void;
  /** It is on its way: what to tell the person. */
  onSent: (message: string) => void;
}

/**
 * Sends a valid sent version from TienditApp (Fase 29D, ADR 0053 point 16): by email, from the platform in the name of the
 * business, or by WhatsApp, from the business's number. It starts with the customer's address or number, which can change.
 */
export default function SendQuoteDialog({ quote, channel, onClose, onSent }: SendQuoteDialogProps) {
  const titleId = useId();
  const byEmail = channel === 'email';
  const [to, setTo] = useState(byEmail ? (quote.customerEmail ?? '') : quote.customerPhone);
  const [error, setError] = useState('');

  const send = useMutation({
    // What the server took: the address, or the number as it was normalized.
    mutationFn: async (address: string): Promise<string> => (byEmail
      ? (await quotesApi.sendByEmail(quote.id, address)).to
      : (await quotesApi.sendByWhatsApp(quote.id, address)).phone),
    onSuccess: (sentTo) => onSent(`La cotización ${quoteFolio(quote.number, quote.revision)} va en camino a ${sentTo}.`),
    onError: (err) => setError(getApiErrorMessage(err, 'No se pudo enviar la cotización.')),
  });

  return (
    <Modal show onHide={() => !send.isPending && onClose()} centered aria-labelledby={titleId}>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          setError('');
          send.mutate(to.trim());
        }}
      >
        <Modal.Header closeButton={!send.isPending}>
          <Modal.Title id={titleId} as="h2" className="h5">{byEmail ? 'Enviar por correo' : 'Enviar por WhatsApp'}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <fieldset disabled={send.isPending}>
          <label className="form-label" htmlFor="quote-send-to">{byEmail ? 'Correo del cliente' : 'WhatsApp del cliente'}</label>
          <input
            id="quote-send-to"
            className="form-control"
            type={byEmail ? 'email' : 'tel'}
            inputMode={byEmail ? 'email' : 'tel'}
            autoComplete="off"
            maxLength={byEmail ? 254 : 30}
            required
            value={to}
            onChange={(event) => setTo(event.target.value)}
            autoFocus
          />
          <p className="form-text mb-0">
            {byEmail
              ? 'Sale de TienditApp a nombre de tu negocio, con la cotización y su enlace. Si el cliente responde, la respuesta llega al correo de tu negocio.'
              : 'Sale del WhatsApp de tu negocio con la plantilla de tus cotizaciones o el texto de TienditApp. Sin una plantilla aprobada en Meta, solo le llega si te escribió en las últimas 24 horas.'}
          </p>
          </fieldset>
          {error && <div className="alert alert-danger mt-3 mb-0" role="alert">{error}</div>}
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={onClose} disabled={send.isPending}>Cancelar</button>
          <button type="submit" className="btn btn-primary" disabled={send.isPending || !to.trim()}>
            {send.isPending ? 'Enviando…' : 'Enviar'}
          </button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
