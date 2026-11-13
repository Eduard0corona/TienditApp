import { useState } from 'react';
import { getApiErrorMessage } from '../../api/errors';
import { saveBlob } from '../../utils/cfdi';

interface QuotePdfButtonProps {
  /** Downloads the PDF: the business's version or the link's quote. */
  download: () => Promise<{ blob: Blob; fileName: string }>;
  /** What the button names, for whoever hears it: «Descargar PDF de la cotización COT-000123». */
  label: string;
  disabled?: boolean;
  className?: string;
}

/**
 * «Descargar PDF» of a quote (Fase 31M, ADR 0093): the browser saves it with the name the server gives. While it comes the
 * button says so, and a refusal (the link expired, say) is said under it.
 */
export default function QuotePdfButton({ download, label, disabled = false, className = 'btn btn-light' }: QuotePdfButtonProps) {
  const [downloading, setDownloading] = useState(false);
  const [error, setError] = useState('');

  const save = async () => {
    setDownloading(true);
    setError('');
    try {
      const { blob, fileName } = await download();
      saveBlob(blob, fileName);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo descargar el PDF. Inténtalo otra vez.'));
    } finally {
      setDownloading(false);
    }
  };

  return (
    <>
      <button type="button" className={className} disabled={disabled || downloading} aria-label={label} onClick={() => void save()}>
        <i className="ri-file-pdf-2-line me-1" aria-hidden="true"></i>{downloading ? 'Descargando…' : 'Descargar PDF'}
      </button>
      {error && <p className="text-danger small mb-0" role="alert">{error}</p>}
    </>
  );
}
