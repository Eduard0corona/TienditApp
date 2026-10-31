import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { useSendIssuedCfdisToAccountant, type CfdiMonthEmail } from '../../api/invoicing';
import { useToast } from '../../hooks/useToast';
import { CfdiMonthEmailFailure } from '../../types';
import { isPlainEmail } from '../../utils/cfdi';
import { formatDateTime } from '../../utils/dates';

const FAILURES: Record<CfdiMonthEmailFailure, string> = {
  [CfdiMonthEmailFailure.Rejected]: 'el servidor de correo lo rechazó. Revisa la dirección con tu contador y vuelve a mandarlo.',
  [CfdiMonthEmailFailure.NotConfigured]: 'TienditApp no tiene el envío de correos configurado. Descarga los XML y compártelos.',
  [CfdiMonthEmailFailure.NotDelivered]: 'el servidor de correo no lo tomó después de varios intentos. Vuelve a mandarlo.',
  [CfdiMonthEmailFailure.TooLarge]: 'el archivo del mes pesa más de lo que aguanta un correo. Descarga los XML y compártelos por otro medio.',
  [CfdiMonthEmailFailure.TooMany]: 'el mes tiene más de 5,000 CFDI, los que junta un correo. Pide los XML con la descarga masiva del portal del SAT.',
};

interface AccountantEmailStatusProps {
  /** The month's latest emails to the accountant, the newest first. */
  emails: CfdiMonthEmail[];
}

/**
 * How the month's latest email to the accountant went (Fase 28O): sent with how many CFDI, on its way, or not sent and why.
 * Its three states share one live region, so the outcome is announced when it replaces «Mandando…».
 */
export function AccountantEmailStatus({ emails }: AccountantEmailStatusProps) {
  const latest = emails[0];
  if (!latest) return null;
  const byTienditApp = latest.byTienditApp && ' TienditApp los mandó solo, al empezar el mes.';

  if (latest.sentAt) {
    return (
      <p className="small text-muted" role="status">
        <i className="ri-mail-check-line me-1" aria-hidden="true"></i>
        Se mandaron a {latest.to} el {formatDateTime(latest.sentAt)}, con {latest.cfdiCount ?? 0} CFDI.
        {byTienditApp}
      </p>
    );
  }
  if (latest.failedAt) {
    return (
      <p className="small text-danger" role="status">
        <i className="ri-mail-close-line me-1" aria-hidden="true"></i>
        No se mandaron a {latest.to}: {latest.failure != null ? FAILURES[latest.failure] : 'no se pudo mandar.'}
        {byTienditApp}
      </p>
    );
  }
  return (
    <p className="small text-muted" role="status">
      <i className="ri-mail-send-line me-1" aria-hidden="true"></i>
      Mandando los CFDI a {latest.to}…
      {latest.failedAttempts > 0 && ' El servidor de correo no los ha tomado; TienditApp lo vuelve a intentar.'}
      {byTienditApp}
    </p>
  );
}

interface SendToAccountantButtonProps {
  year: number;
  month: number;
  /** «octubre de 2026». */
  monthLabel: string;
  /** The address the dialog proposes: the accountant's, or the last one the month went to. */
  proposedEmail?: string | null;
  /** The month has nothing to send. */
  disabled: boolean;
}

/** «Mandar al contador»: the month's file by email to the address typed, which stays as the accountant's. */
export function SendToAccountantButton({ year, month, monthLabel, proposedEmail, disabled }: SendToAccountantButtonProps) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <button type="button" className="btn btn-light btn-sm" disabled={disabled} onClick={() => setOpen(true)}>
        <i className="ri-mail-send-line me-1" aria-hidden="true"></i>
        Mandar al contador
      </button>
      {open && (
        <SendToAccountantModal
          year={year}
          month={month}
          monthLabel={monthLabel}
          initialEmail={proposedEmail ?? ''}
          onClose={() => setOpen(false)}
        />
      )}
    </>
  );
}

interface SendToAccountantModalProps {
  year: number;
  month: number;
  monthLabel: string;
  initialEmail: string;
  onClose: () => void;
}

function SendToAccountantModal({ year, month, monthLabel, initialEmail, onClose }: SendToAccountantModalProps) {
  const titleId = useId();
  const toast = useToast();
  const send = useSendIssuedCfdisToAccountant();
  const [email, setEmail] = useState(initialEmail);
  const typed = email.trim();
  const valid = isPlainEmail(typed);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!valid) return;
    try {
      await send.mutateAsync({ year, month, email: typed });
      toast.success(`Se están mandando los CFDI de ${monthLabel} a ${typed}.`);
      onClose();
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudieron mandar los CFDI al contador.'));
    }
  };

  return (
    <Modal show onHide={onClose} centered aria-labelledby={titleId}>
      <form onSubmit={submit} noValidate>
        <Modal.Header closeButton>
          <Modal.Title id={titleId} as="h2" className="h5">Mandar al contador los CFDI de {monthLabel}</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <p className="small">
            Le llega el archivo del mes: el XML de cada CFDI timbrado, por tipo, y la lista de todos en CSV. Si responde el correo,
            la respuesta llega al correo de tu negocio.
          </p>
          <label className="form-label small mb-1" htmlFor={`${titleId}-email`}>Correo del contador</label>
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
            aria-describedby={typed && !valid ? `${titleId}-invalid` : `${titleId}-kept`}
          />
          {typed && !valid && (
            <div id={`${titleId}-invalid`} className="invalid-feedback">Escribe un correo válido, como nombre@ejemplo.com.</div>
          )}
          <p id={`${titleId}-kept`} className="small text-muted mt-2 mb-0">
            Lo guardamos como el correo de tu contador en Configuración › Facturación.
          </p>
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={onClose}>Cancelar</button>
          <button type="submit" className="btn btn-primary" disabled={!valid || send.isPending}>
            {send.isPending ? 'Mandando…' : 'Mandar'}
          </button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
