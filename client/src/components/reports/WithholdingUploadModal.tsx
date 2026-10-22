import { useId, useState, type ChangeEvent } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { useRefreshPlatformWithholdings, useRegisterPlatformWithholdingCertificate } from '../../api/platformWithholdings';
import { decodeCsv } from '../../utils/csv';
import { getPlatformName, type DeliveryPlatform } from '../../utils/deliveryPlatforms';
import { DELIVERY_PLATFORM_CHANNELS } from '../../utils/labels';
import { formatMoney } from '../../utils/money';
import {
  MAX_WITHHOLDING_XML_BYTES,
  NOT_XML_FILE_MESSAGE,
  XML_TOO_BIG_MESSAGE,
  describeCertificatePeriod,
} from './withholdingLabels';

/** The most files a batch takes: a year of weekly CFDI of DiDi, with room. */
export const MAX_WITHHOLDING_FILES = 60;

interface WithholdingUploadModalProps {
  /** The platform on screen: the person can choose another one. */
  platform: DeliveryPlatform;
  onHide: () => void;
}

/** What came of one file: registered (with what it says) or why not. */
interface FileResult {
  name: string;
  ok: boolean;
  text: string;
}

function isXmlFile(file: File): boolean {
  return /\.xml$/i.test(file.name) || file.type === 'text/xml' || file.type === 'application/xml';
}

/**
 * «Subir CFDI de retenciones»: the person chooses the platform that issued them and one or more XML files; each one is
 * sent on its own and says whether it went in, and the reconciliation is read again after the last one.
 */
export default function WithholdingUploadModal({ platform, onHide }: WithholdingUploadModalProps) {
  const id = useId();
  const register = useRegisterPlatformWithholdingCertificate();
  const refresh = useRefreshPlatformWithholdings();
  const [channel, setChannel] = useState<DeliveryPlatform>(platform);
  const [files, setFiles] = useState<File[]>([]);
  const [results, setResults] = useState<FileResult[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  const choose = (event: ChangeEvent<HTMLInputElement>) => {
    const chosen = Array.from(event.target.files ?? []);
    setResults(null);
    setError(chosen.length > MAX_WITHHOLDING_FILES ? `Sube hasta ${MAX_WITHHOLDING_FILES} archivos a la vez.` : '');
    setFiles(chosen.length > MAX_WITHHOLDING_FILES ? [] : chosen);
  };

  const upload = async () => {
    setBusy(true);
    const done: FileResult[] = [];
    for (const file of files) {
      if (!isXmlFile(file)) {
        done.push({ name: file.name, ok: false, text: NOT_XML_FILE_MESSAGE });
        continue;
      }
      if (file.size > MAX_WITHHOLDING_XML_BYTES) {
        done.push({ name: file.name, ok: false, text: XML_TOO_BIG_MESSAGE });
        continue;
      }
      const xml = decodeCsv(await file.arrayBuffer());
      if (xml === null) {
        done.push({ name: file.name, ok: false, text: NOT_XML_FILE_MESSAGE });
        continue;
      }
      try {
        const certificate = await register.mutateAsync({ channel, xml });
        done.push({
          name: file.name,
          ok: true,
          text: `Registrado: ${describeCertificatePeriod(certificate)}, ISR ${formatMoney(certificate.isrWithheld)} e IVA ${formatMoney(certificate.vatWithheld)}.`,
        });
      } catch (err: unknown) {
        done.push({ name: file.name, ok: false, text: getApiErrorMessage(err, 'No se pudo registrar. Intenta de nuevo.') });
      }
    }
    setResults(done);
    setBusy(false);
    if (done.some((result) => result.ok)) void refresh();
  };

  const registered = results?.filter((result) => result.ok).length ?? 0;

  return (
    <Modal show onHide={busy ? () => undefined : onHide} centered aria-labelledby={`${id}-title`}>
      <Modal.Header closeButton={!busy}>
        <Modal.Title id={`${id}-title`}>Subir CFDI de retenciones</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {error && <div className="alert alert-danger" role="alert">{error}</div>}
        {results ? (
          <>
            <p aria-live="polite">
              {registered === results.length
                ? (registered === 1 ? 'Se registró el CFDI.' : `Se registraron los ${registered} CFDI.`)
                : `Se registraron ${registered} de ${results.length}. Revisa los que no entraron.`}
            </p>
            <ul className="list-unstyled mb-0 d-flex flex-column gap-2">
              {results.map((result, index) => (
                <li key={`${result.name}-${index}`}>
                  <span className={`ta-chip ${result.ok ? 'ta-chip-ready' : 'ta-chip-cancelled'} me-2`}>
                    {result.ok ? 'Registrado' : 'No entró'}
                  </span>
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
            <label className="form-label" htmlFor={`${id}-platform`}>Plataforma que lo emitió</label>
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
              id={`${id}-files`}
              type="file"
              className="form-control"
              accept=".xml,text/xml,application/xml"
              multiple
              onChange={choose}
              disabled={busy}
            />
            {files.length > 0 && (
              <p className="small text-muted mt-2 mb-0">
                {files.length === 1 ? '1 archivo elegido.' : `${files.length} archivos elegidos.`}
              </p>
            )}
          </>
        )}
      </Modal.Body>
      <Modal.Footer>
        {results ? (
          <>
            <button type="button" className="btn btn-light me-auto" onClick={() => { setResults(null); setFiles([]); }}>
              Subir otros
            </button>
            <button type="button" className="btn btn-primary" onClick={onHide}>Listo</button>
          </>
        ) : (
          <>
            <button type="button" className="btn btn-light" onClick={onHide} disabled={busy}>Cancelar</button>
            <button type="button" className="btn btn-primary" onClick={() => void upload()} disabled={busy || files.length === 0}>
              {busy ? 'Subiendo…' : 'Subir'}
            </button>
          </>
        )}
      </Modal.Footer>
    </Modal>
  );
}
