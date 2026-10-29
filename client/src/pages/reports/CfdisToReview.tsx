import { useId, useState, type FormEvent } from 'react';
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

/**
 * «Por revisar en el portal del SAT» (Fase 28K, ADR 0056): every CFDI still «Timbrando» past the PAC's window, whatever its
 * month. TienditApp no longer asks for their stamp, and the PAC has no search without the folio fiscal, so a person looks for
 * each one in the SAT's portal by its date, receiver and total, and says what it found: its folio fiscal, which brings its
 * XML back, or that it is not there, which refuses it so what it covered can be invoiced again. Nothing shows while there
 * are none.
 */
export default function CfdisToReview() {
  const toast = useToast();
  const { confirm } = useConfirm();
  const headingId = useId();
  const cfdis = useCfdisToReview();
  const notStamped = useConfirmStuckCfdiNotStamped();
  const [stamped, setStamped] = useState<IssuedCfdi | null>(null);

  if (cfdis.isError) {
    return (
      <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2" role="alert">
        <span className="me-auto">{getApiErrorMessage(cfdis.error, 'No se pudieron cargar los CFDI por revisar en el portal del SAT.')}</span>
        <button type="button" className="btn btn-sm btn-light" onClick={() => void cfdis.refetch()}>Reintentar</button>
      </div>
    );
  }
  if (!cfdis.data?.length) return null;

  const refuse = async (cfdi: IssuedCfdi) => {
    const accepted = await confirm({
      title: `${title(cfdi)} no se timbró`,
      message: `Si no está en el portal del SAT, ${theDocument(cfdi)} queda ${inGender(cfdi.kind, 'rechazada')} con tu nombre y lo que cubría se puede facturar otra vez. Si TienditApp ${inGender(cfdi.kind, 'la')} hubiera timbrado, ahí saldría con su folio fiscal.`,
      confirmLabel: 'No se timbró',
      cancelLabel: 'Volver',
      danger: true,
    });
    if (!accepted) return;
    try {
      const result = await notStamped.mutateAsync(cfdi);
      toast.success(`${title(result)} queda ${inGender(result.kind, 'rechazada')}: ya puedes facturar otra vez lo que cubría.`);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo guardar que no se timbró.'));
    }
  };

  return (
    <section className="card border-warning mb-3" aria-labelledby={headingId}>
      <div className="card-body">
        <h2 id={headingId} className="h5">Por revisar en el portal del SAT</h2>
        <p className="small text-muted">
          Se pidieron hace más de 71 horas y el PAC ya no devuelve su timbre, así que TienditApp no puede saber si se timbraron.
          Búscalos en el portal del SAT, en Factura electrónica › Consultar facturas emitidas, por su fecha, su receptor y su
          total. Si está, escribe su folio fiscal; si no, di que no se timbró y factura otra vez lo que cubría. El SAT puede
          tardar en mostrar uno recién timbrado: si no lo encuentras, búscalo otra vez al día siguiente antes de decir que no se
          timbró.
        </p>
        <div className="table-responsive">
          <table className="table table-sm align-middle mb-0" aria-labelledby={headingId}>
            <thead>
              <tr>
                <th scope="col">CFDI</th>
                <th scope="col">Fecha</th>
                <th scope="col">Receptor</th>
                <th scope="col" className="text-end">Total</th>
                <th scope="col" className="text-end">Acciones</th>
              </tr>
            </thead>
            <tbody>
              {cfdis.data.map((cfdi) => (
                <tr key={cfdi.id}>
                  <td>
                    <div className="fw-semibold">{cfdi.number}</div>
                    <div className="small text-muted">{kindLabel(cfdi.kind)}</div>
                  </td>
                  <td className="text-nowrap">{formatDateTime(cfdi.issuedAt)}</td>
                  <td>
                    {cfdi.receiverName}
                    <div className="small text-muted">{cfdi.receiverRfc}</div>
                  </td>
                  <td className="text-end">
                    {cfdi.kind === IssuedCfdiKind.PaymentComplement
                      ? <>Pagado <span className="ta-money">{formatMoney(cfdi.paidAmount ?? 0)}</span></>
                      : <span className="ta-money">{formatMoney(cfdi.total ?? 0)}</span>}
                  </td>
                  <td className="text-end text-nowrap">
                    <button
                      type="button"
                      className="btn btn-light btn-sm me-1"
                      aria-label={`Ya se timbró ${theDocument(cfdi)}`}
                      onClick={() => setStamped(cfdi)}
                    >
                      Ya se timbró
                    </button>
                    <button
                      type="button"
                      className="btn btn-light btn-sm"
                      disabled={notStamped.isPending}
                      aria-label={`No se timbró ${theDocument(cfdi)}`}
                      onClick={() => void refuse(cfdi)}
                    >
                      No se timbró
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {cfdis.data.length >= MAX_SHOWN && (
          <p className="small text-muted mt-2 mb-0">Se ven los {MAX_SHOWN} más viejos: al resolverlos aparecen los demás.</p>
        )}
      </div>
      {stamped && <StampedModal cfdi={stamped} onClose={() => setStamped(null)} />}
    </section>
  );
}

/**
 * «Ya se timbró»: the folio fiscal the SAT's portal shows for the CFDI. TienditApp brings its XML from the PAC and keeps it
 * only if it is this CFDI; what the API answers otherwise (another CFDI's, one the PAC does not have) stays on the form to
 * correct it.
 */
function StampedModal({ cfdi, onClose }: { cfdi: IssuedCfdi; onClose: () => void }) {
  const toast = useToast();
  const confirmStamped = useConfirmStuckCfdiStamped();
  const titleId = useId();
  const inputId = useId();
  const helpId = useId();
  const errorId = useId();
  const [uuid, setUuid] = useState('');
  const [error, setError] = useState<string | null>(null);

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
      onClose();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo traer su timbre del PAC. Intenta de nuevo.'));
    }
  };

  return (
    <Modal show onHide={onClose} centered aria-labelledby={titleId}>
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
            receptor y su total.
          </p>
        </Modal.Body>
        <Modal.Footer>
          <button type="button" className="btn btn-light" onClick={onClose}>Volver</button>
          <button type="submit" className="btn btn-primary" disabled={confirmStamped.isPending}>
            {confirmStamped.isPending ? 'Guardando…' : 'Guardar el timbre'}
          </button>
        </Modal.Footer>
      </form>
    </Modal>
  );
}
