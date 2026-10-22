import { useEffect, useId, useRef, useState, type ChangeEvent } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { useRefreshPlatformWithholdings, useRegisterPlatformWithholdingCertificate } from '../../api/platformWithholdings';
import { decodeCsv } from '../../utils/csv';
import { getPlatformName, type DeliveryPlatform } from '../../utils/deliveryPlatforms';
import { DELIVERY_PLATFORM_CHANNELS } from '../../utils/labels';
import { formatMoney } from '../../utils/money';
import {
  FILE_NOT_READABLE_MESSAGE,
  MAX_WITHHOLDING_XML_BYTES,
  MAX_WITHHOLDING_XML_LENGTH,
  NOT_XML_CONTENT_MESSAGE,
  NOT_XML_FILE_MESSAGE,
  XML_TOO_BIG_MESSAGE,
  describeCertificatePeriod,
} from './withholdingLabels';

/** The most files a batch takes: a year of weekly CFDI of DiDi, with room. */
export const MAX_WITHHOLDING_FILES = 60;

/** What the batch registered, for the page to show: the platform chosen and the year of the last CFDI that went in. */
export interface WithholdingUploadOutcome {
  channel: DeliveryPlatform;
  year: number;
}

interface WithholdingUploadModalProps {
  /** The platform on screen: the person can choose another one. */
  platform: DeliveryPlatform;
  /** Closes it; with what went in when the batch registered at least one CFDI. */
  onHide: (outcome?: WithholdingUploadOutcome) => void;
}

/**
 * What came of one file: registered (with what it says), kept without counting (the platform already replaced it) or
 * why it did not go in.
 */
interface FileResult {
  name: string;
  outcome: 'registered' | 'notCounting' | 'refused';
  text: string;
}

const CHIPS: Record<FileResult['outcome'], { label: string; className: string }> = {
  registered: { label: 'Registrado', className: 'ta-chip-ready' },
  notCounting: { label: 'No cuenta', className: 'ta-chip-done' },
  refused: { label: 'No entró', className: 'ta-chip-cancelled' },
};

function isXmlFile(file: File): boolean {
  return /\.xml$/i.test(file.name) || file.type === 'text/xml' || file.type === 'application/xml';
}

function summary(registered: number, total: number): string {
  if (total === 1) return registered === 1 ? 'Se registró el CFDI.' : 'No se registró el CFDI.';
  if (registered === total) return `Se registraron los ${total} CFDI.`;
  if (registered === 0) return `No se registró ninguno de los ${total}. Revisa por qué abajo.`;
  return `${registered === 1 ? 'Se registró 1' : `Se registraron ${registered}`} de ${total}. Revisa los que no entraron.`;
}

/**
 * «Subir CFDI de retenciones»: the person chooses the platform that issued them and one or more XML files; each one is
 * sent on its own and says whether it went in, and the reconciliation is read again after the last one. Leaving the page
 * mid-batch stops it at the file being sent: the rest are not sent.
 */
export default function WithholdingUploadModal({ platform, onHide }: WithholdingUploadModalProps) {
  const id = useId();
  const register = useRegisterPlatformWithholdingCertificate();
  const refresh = useRefreshPlatformWithholdings();
  const [channel, setChannel] = useState<DeliveryPlatform>(platform);
  const [files, setFiles] = useState<File[]>([]);
  const [results, setResults] = useState<FileResult[] | null>(null);
  const [sending, setSending] = useState<number | null>(null);
  const [outcome, setOutcome] = useState<WithholdingUploadOutcome | undefined>(undefined);
  const [error, setError] = useState('');
  const stopped = useRef(false);
  const summaryRef = useRef<HTMLParagraphElement>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  // «Subir otros» brings the file input back: the focus goes to it once it is there.
  const focusFileNext = useRef(false);
  const busy = sending !== null;

  // Unmounted (another screen, the browser's back), the batch stops before the next file. Mounted again (StrictMode
  // in development mounts twice), it starts afresh.
  useEffect(() => {
    stopped.current = false;
    return () => {
      stopped.current = true;
    };
  }, []);

  // Closing or reloading the tab mid-batch asks first: the files after the current one would not be sent.
  useEffect(() => {
    if (!busy) return undefined;
    const keep = (event: BeforeUnloadEvent) => {
      event.preventDefault();
    };
    window.addEventListener('beforeunload', keep);
    return () => window.removeEventListener('beforeunload', keep);
  }, [busy]);

  // The summary takes the focus when it arrives, so a screen reader reads it; the file input, after «Subir otros».
  useEffect(() => {
    if (results) {
      summaryRef.current?.focus();
    } else if (focusFileNext.current) {
      focusFileNext.current = false;
      fileRef.current?.focus();
    }
  }, [results]);

  const choose = (event: ChangeEvent<HTMLInputElement>) => {
    const chosen = Array.from(event.target.files ?? []);
    setResults(null);
    setError(chosen.length > MAX_WITHHOLDING_FILES ? `Sube hasta ${MAX_WITHHOLDING_FILES} archivos a la vez.` : '');
    setFiles(chosen.length > MAX_WITHHOLDING_FILES ? [] : chosen);
  };

  /** What one file says: its XML, or why it is not sent. */
  const read = async (file: File): Promise<{ xml: string } | { problem: string }> => {
    if (!isXmlFile(file)) return { problem: NOT_XML_FILE_MESSAGE };
    if (file.size > MAX_WITHHOLDING_XML_BYTES) return { problem: XML_TOO_BIG_MESSAGE };
    let bytes: ArrayBuffer;
    try {
      bytes = await file.arrayBuffer();
    } catch {
      // It changed or went away after it was chosen (a folder synced with the cloud, a USB taken out).
      return { problem: FILE_NOT_READABLE_MESSAGE };
    }
    const xml = decodeCsv(bytes);
    if (xml === null) return { problem: NOT_XML_CONTENT_MESSAGE };
    // The server counts characters: a UTF-16 file weighs twice what it says.
    if (xml.length > MAX_WITHHOLDING_XML_LENGTH) return { problem: XML_TOO_BIG_MESSAGE };
    return { xml };
  };

  const upload = async () => {
    const done: FileResult[] = [];
    let last: WithholdingUploadOutcome | undefined;
    for (const [index, file] of files.entries()) {
      if (stopped.current) return;
      setSending(index + 1);
      const reading = await read(file);
      if ('problem' in reading) {
        done.push({ name: file.name, outcome: 'refused', text: reading.problem });
        continue;
      }
      try {
        const certificate = await register.mutateAsync({ channel, xml: reading.xml });
        last = { channel, year: certificate.year };
        const period = describeCertificatePeriod(certificate);
        done.push(certificate.removedAt
          ? { name: file.name, outcome: 'notCounting', text: `Se guardó sin contar (${period}): ${certificate.removalReason ?? ''}` }
          : {
              name: file.name,
              outcome: 'registered',
              text: `Registrado: ${period}, ISR ${formatMoney(certificate.isrWithheld)} e IVA ${formatMoney(certificate.vatWithheld)}.`,
            });
      } catch (err: unknown) {
        done.push({ name: file.name, outcome: 'refused', text: getApiErrorMessage(err, 'No se pudo registrar. Intenta de nuevo.') });
      }
    }
    if (last) void refresh();
    if (stopped.current) return;
    setOutcome(last ?? outcome);
    setResults(done);
    setSending(null);
  };

  const registered = results?.filter((result) => result.outcome !== 'refused').length ?? 0;
  const close = () => onHide(outcome);

  return (
    <Modal show onHide={busy ? () => undefined : close} centered aria-labelledby={`${id}-title`}>
      <Modal.Header closeButton={!busy}>
        <Modal.Title id={`${id}-title`}>Subir CFDI de retenciones</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {error && <div className="alert alert-danger" role="alert">{error}</div>}
        {results ? (
          <>
            <p ref={summaryRef} tabIndex={-1}>{summary(registered, results.length)}</p>
            <ul className="list-unstyled mb-0 d-flex flex-column gap-2">
              {results.map((result, index) => (
                <li key={`${result.name}-${index}`}>
                  <span className={`ta-chip ${CHIPS[result.outcome].className} me-2`}>{CHIPS[result.outcome].label}</span>
                  <span className="fw-semibold text-break">{result.name}</span>
                  <div className="small text-muted">{result.text}</div>
                </li>
              ))}
            </ul>
          </>
        ) : (
          <>
            <p className="small text-muted">
              Descarga de la plataforma el XML de cada CFDI de retenciones (mensual o semanal). Se guarda lo que dice, no el
              archivo, y cada CFDI cuenta una sola vez.
            </p>
            <label className="form-label" htmlFor={`${id}-platform`}>Plataforma que emitió los CFDI</label>
            <select
              id={`${id}-platform`}
              className="form-select mb-3"
              value={channel}
              onChange={(event) => setChannel(Number(event.target.value) as DeliveryPlatform)}
              disabled={busy}
            >
              {DELIVERY_PLATFORM_CHANNELS.map((option) => <option key={option} value={option}>{getPlatformName(option)}</option>)}
            </select>
            <label className="form-label" htmlFor={`${id}-files`}>Archivos XML</label>
            <input
              ref={fileRef}
              id={`${id}-files`}
              type="file"
              className="form-control"
              accept=".xml,text/xml,application/xml"
              multiple
              onChange={choose}
              disabled={busy}
            />
            <p className="small text-muted mt-2 mb-0" aria-live="polite">
              {busy
                ? `Subiendo ${sending} de ${files.length}…`
                : files.length > 0 && (files.length === 1 ? '1 archivo elegido.' : `${files.length} archivos elegidos.`)}
            </p>
          </>
        )}
      </Modal.Body>
      <Modal.Footer>
        {results ? (
          <>
            <button
              type="button"
              className="btn btn-light me-auto"
              onClick={() => {
                focusFileNext.current = true;
                setResults(null);
                setFiles([]);
              }}
            >
              Subir otros
            </button>
            <button type="button" className="btn btn-primary" onClick={close}>Listo</button>
          </>
        ) : (
          <>
            <button type="button" className="btn btn-light" onClick={close} disabled={busy}>Cancelar</button>
            <button type="button" className="btn btn-primary" onClick={() => void upload()} disabled={busy || files.length === 0}>
              {busy ? 'Subiendo…' : 'Subir'}
            </button>
          </>
        )}
      </Modal.Footer>
    </Modal>
  );
}
