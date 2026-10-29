import { useEffect, useId, useRef, useState, type FormEvent } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import {
  useCfdisToReview,
  useConfirmStuckCfdiNotStamped,
  useConfirmStuckCfdiStamped,
  type IssuedCfdi,
} from '../../api/invoicing';
import { inGender, kindLabel, theDocument } from '../../components/invoicing/issuedCfdiLabels';
import { useConfirm } from '../../hooks/useConfirm';
import { useToast } from '../../hooks/useToast';
import { IssuedCfdiKind } from '../../types';
import { isTooSoonForNotStamped } from '../../utils/cfdi';
import { formatDateTime } from '../../utils/dates';
import { formatMoney } from '../../utils/money';

// As the API: the folio fiscal is 32 hexadecimal digits in groups of 8, 4, 4, 4 and 12.
const UUID_PATTERN = /^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$/;
const UUID_MESSAGE = 'Escribe el folio fiscal (UUID) completo, como aparece en el portal del SAT.';

// The API shows the oldest 100.
const MAX_SHOWN = 100;

/** «Factura F-12», «Complemento de pago P-3». */
function title(cfdi: IssuedCfdi): string {
  return `${kindLabel(cfdi.kind)} ${cfdi.number}`;
}

/** What refusing it gives back, as the API does for each kind (a replacement gives the order its invoice back, Fase 28G). */
function whatComesBack(cfdi: IssuedCfdi): string {
  switch (cfdi.kind) {
    case IssuedCfdiKind.GlobalInvoice:
      return 'sus ventas pueden entrar en otra factura global';
    case IssuedCfdiKind.PaymentComplement:
      return 'el cobro se declara otra vez con «Emitir otra vez» en su pedido';
    case IssuedCfdiKind.CreditNote:
      return 'la nota se emite otra vez en su pedido';
    default:
      return cfdi.relatedNumber
        ? `el pedido se queda con la factura ${cfdi.relatedNumber}, que puedes sustituir otra vez`
        : 'el pedido se puede facturar otra vez';
  }
}

/**
 * «Por revisar en el portal del SAT» (Fase 28K, ADR 0056): every CFDI still «Timbrando» past the PAC's window, whatever its
 * month. TienditApp no longer asks for their stamp, and the PAC has no search without the folio fiscal, so a person looks for
 * each one in the SAT's portal by its date, receiver and total, and says what it found: its folio fiscal, which brings its
 * XML back, or, once the SAT shows whatever was stamped, that it is not there, which refuses it so what it covered can be
 * invoiced again. Nothing shows while there are none; after resolving the last one, it says so where the focus goes.
 */
export default function CfdisToReview() {
  const toast = useToast();
  const { confirm } = useConfirm();
  const headingId = useId();
  const heading = useRef<HTMLHeadingElement>(null);
  const cfdis = useCfdisToReview();
  const notStamped = useConfirmStuckCfdiNotStamped();
  const [stamped, setStamped] = useState<IssuedCfdi | null>(null);
  const [resolvedCount, setResolvedCount] = useState(0);
  const [focusRequest, setFocusRequest] = useState(0);

  // The row resolved leaves the list (it is read again before the change answers): the focus goes to the section's title,
  // after the dialog gives it back to the button that is gone.
  useEffect(() => {
    if (focusRequest > 0) heading.current?.focus();
  }, [focusRequest]);

  const resolved = () => {
    setResolvedCount((count) => count + 1);
    setFocusRequest((count) => count + 1);
  };

  const refuse = async (cfdi: IssuedCfdi) => {
    const accepted = await confirm({
      title: `${title(cfdi)} no se timbró`,
      message: `Si no está en el portal del SAT, ${theDocument(cfdi)} queda ${inGender(cfdi.kind, 'rechazada')} y ${whatComesBack(cfdi)}. Si TienditApp ${inGender(cfdi.kind, 'la')} hubiera timbrado, ahí saldría con su folio fiscal. Queda registrado que tú lo revisaste.`,
      confirmLabel: 'No se timbró',
      cancelLabel: 'Volver',
      danger: true,
    });
    if (!accepted) return;
    try {
      const result = await notStamped.mutateAsync(cfdi);
      toast.success(`${title(result)} queda ${inGender(result.kind, 'rechazada')}: ${whatComesBack(result)}.`);
      resolved();
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo guardar que no se timbró.'));
    }
  };

  const items = cfdis.data ?? [];
  let content = null;
  if (cfdis.isError) {
    content = (
      <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2" role="alert">
        <span className="me-auto">{getApiErrorMessage(cfdis.error, 'No se pudieron cargar los CFDI por revisar en el portal del SAT.')}</span>
        <button type="button" className="btn btn-sm btn-light" onClick={() => void cfdis.refetch()}>Reintentar</button>
      </div>
    );
  } else if (items.length > 0 || resolvedCount > 0) {
    const one = items.length === 1;
    content = (
      <section className="card border-warning mb-3" aria-labelledby={headingId}>
        <div className="card-body">
          <h2 id={headingId} ref={heading} tabIndex={-1} className="h5">Por revisar en el portal del SAT</h2>
          {items.length === 0 ? (
            <p className="mb-0">Ya no queda ningún CFDI por revisar.</p>
          ) : (
            <>
              <p className="small text-muted">
                {one
                  ? 'Se pidió hace más de 71 horas y el PAC ya no devuelve su timbre, así que TienditApp no puede saber si se timbró. Búscalo'
                  : 'Se pidieron hace más de 71 horas y el PAC ya no devuelve su timbre, así que TienditApp no puede saber si se timbraron. Búscalos'}{' '}
                en el portal del SAT, en Factura electrónica › Consultar facturas emitidas, por su fecha, su receptor y su total. Si
                está, escribe su folio fiscal; si no, di que no se timbró. El SAT tarda hasta 72 horas en mostrar un CFDI timbrado:
                «No se timbró» se puede decir cuando pasan.
              </p>
              <div className="table-responsive">
                <table className="table table-sm align-middle mb-0" aria-labelledby={headingId}>
                  <thead>
                    <tr>
                      <th scope="col">CFDI</th>
                      <th scope="col" className="d-none d-md-table-cell">Fecha</th>
                      <th scope="col" className="d-none d-md-table-cell">Receptor</th>
                      <th scope="col" className="text-end">Total</th>
                      <th scope="col" className="text-end">Acciones</th>
                    </tr>
                  </thead>
                  <tbody>
                    {items.map((cfdi) => (
                      <ToReviewRow
                        key={cfdi.id}
                        cfdi={cfdi}
                        refusing={notStamped.isPending}
                        onStamped={() => setStamped(cfdi)}
                        onNotStamped={() => void refuse(cfdi)}
                      />
                    ))}
                  </tbody>
                </table>
              </div>
              {items.length >= MAX_SHOWN && (
                <p className="small text-muted mt-2 mb-0">Se ven los {MAX_SHOWN} más viejos: al resolverlos aparecen los demás.</p>
              )}
            </>
          )}
        </div>
      </section>
    );
  }

  return (
    <>
      {content}
      {stamped && (
        <StampedModal
          cfdi={stamped}
          onClose={() => setStamped(null)}
          onStamped={() => {
            setStamped(null);
            resolved();
          }}
        />
      )}
    </>
  );
}

/**
 * One CFDI to look for in the SAT's portal. «No se timbró» waits until the SAT shows whatever the PAC stamped (the API says
 * when, and decides by its own clock); before that, the row says from when.
 */
function ToReviewRow({ cfdi, refusing, onStamped, onNotStamped }: {
  cfdi: IssuedCfdi;
  refusing: boolean;
  onStamped: () => void;
  onNotStamped: () => void;
}) {
  const notYet = isTooSoonForNotStamped(cfdi);
  return (
    <tr>
      <td>
        <div className="fw-semibold">{cfdi.number}</div>
        <div className="small text-muted">{kindLabel(cfdi.kind)}</div>
        {/* On a phone, what finds it in the portal goes under its folio. */}
        <div className="small text-muted d-md-none">{formatDateTime(cfdi.issuedAt)}</div>
        <div className="small text-muted d-md-none">{cfdi.receiverName}</div>
      </td>
      <td className="d-none d-md-table-cell text-nowrap">{formatDateTime(cfdi.issuedAt)}</td>
      <td className="d-none d-md-table-cell">
        {cfdi.receiverName}
        <div className="small text-muted">{cfdi.receiverRfc}</div>
      </td>
      <td className="text-end">
        {cfdi.kind === IssuedCfdiKind.PaymentComplement
          ? <>Pagado <span className="ta-money">{formatMoney(cfdi.paidAmount ?? 0)}</span></>
          : <span className="ta-money">{formatMoney(cfdi.total ?? 0)}</span>}
      </td>
      <td className="text-end">
        <div className="d-flex flex-wrap justify-content-end gap-1">
          <button type="button" className="btn btn-light btn-sm text-nowrap" aria-label={`Ya se timbró ${theDocument(cfdi)}`} onClick={onStamped}>
            Ya se timbró
          </button>
          {!notYet && (
            <button
              type="button"
              className="btn btn-light btn-sm text-nowrap"
              disabled={refusing}
              aria-label={`No se timbró ${theDocument(cfdi)}`}
              onClick={onNotStamped}
            >
              No se timbró
            </button>
          )}
        </div>
        {notYet && (
          <div className="small text-muted mt-1">«No se timbró», desde el {formatDateTime(cfdi.notStampedFrom)}</div>
        )}
      </td>
    </tr>
  );
}

/**
 * «Ya se timbró»: the folio fiscal the SAT's portal shows for the CFDI. TienditApp brings its XML from the PAC and keeps it
 * only if it is this CFDI; what the API answers otherwise (another CFDI's, one the PAC does not have, one cancelled) stays on
 * the form to correct it. It does not close while it saves.
 */
function StampedModal({ cfdi, onClose, onStamped }: { cfdi: IssuedCfdi; onClose: () => void; onStamped: () => void }) {
  const toast = useToast();
  const confirmStamped = useConfirmStuckCfdiStamped();
  const titleId = useId();
  const inputId = useId();
  const helpId = useId();
  const errorId = useId();
  const [uuid, setUuid] = useState('');
  const [error, setError] = useState<string | null>(null);
  const saving = confirmStamped.isPending;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    const written = uuid.trim();
    if (!UUID_PATTERN.test(written)) {
      setError(UUID_MESSAGE);
      return;
    }
    setError(null);
    try {
      const result = await confirmStamped.mutateAsync({ cfdi, uuid: written });
      toast.success(`${title(result)} ${inGender(result.kind, 'timbrada')}: ya tiene su XML y su PDF.`);
      onStamped();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo traer su timbre del PAC. Intenta de nuevo.'));
    }
  };

  return (
    <Modal
      show
      onHide={saving ? () => undefined : onClose}
      backdrop={saving ? 'static' : true}
      keyboard={!saving}
      centered
      aria-labelledby={titleId}
    >
      <form onSubmit={(event) => void submit(event)} noValidate>
        <Modal.Header closeButton>
          <Modal.Title id={titleId} as="h2" className="h5">{title(cfdi)} ya se timbró</Modal.Title>
        </Modal.Header>
        <Modal.Body>
          <label className="form-label" htmlFor={inputId}>Folio fiscal (UUID)</label>
          <input
            id={inputId}
            className={`form-control ta-money${error ? ' is-invalid' : ''}`}
            value={uuid}
            maxLength={40}
            autoComplete="off"
            spellCheck={false}
            placeholder="5F0B7C1E-2A3D-4E5F-8A9B-0C1D2E3F4A5B"
            aria-invalid={error ? true : undefined}
            aria-describedby={error ? `${errorId} ${helpId}` : helpId}
            onChange={(event) => setUuid(event.target.value)}
          />
          {error && <div id={errorId} className="invalid-feedback">{error}</div>}
          <p id={helpId} className="form-text mb-0">
            Cópialo del portal del SAT. TienditApp trae su XML del PAC y revisa que sea {theDocument(cfdi)}: su serie y folio, su
            fecha, su receptor y su total.
          </p>
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" disabled={saving} onClick={onClose}>Volver</button>
          <button type="submit" className="btn btn-primary" disabled={saving}>
            {saving ? 'Guardando…' : 'Guardar el timbre'}
          </button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
