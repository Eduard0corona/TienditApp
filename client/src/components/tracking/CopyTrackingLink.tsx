import { useState } from 'react';

interface CopyTrackingLinkProps {
  token: string;
  /** The page that opens the link: an order's (/seguimiento), an appointment's (/cita, Fase 24D) or a quote's (/cotizacion, Fase 29C). */
  path?: '/seguimiento' | '/cita' | '/cotizacion';
}

const LABELS: Record<NonNullable<CopyTrackingLinkProps['path']>, { copy: string; copied: string }> = {
  '/seguimiento': { copy: 'Copiar enlace de seguimiento', copied: 'Enlace de seguimiento copiado' },
  '/cita': { copy: 'Copiar enlace de tu cita', copied: 'Enlace de tu cita copiado' },
  '/cotizacion': { copy: 'Copiar enlace', copied: 'Enlace de la cotización copiado' },
};

/**
 * Copies the link to follow an online order (Fase 24C) or an appointment booked online (Fase 24D), so a guest keeps it
 * after leaving the receipt, or the link of a quote the business sends (Fase 29C). Where the browser does not let the page
 * copy (an insecure address, a denied permission), the link shows to copy by hand.
 */
export default function CopyTrackingLink({ token, path = '/seguimiento' }: CopyTrackingLinkProps) {
  const [state, setState] = useState<'idle' | 'copied' | 'failed'>('idle');
  const url = `${window.location.origin}${path}#${token}`;
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(url);
      setState('copied');
    } catch {
      setState('failed');
    }
  };

  return (
    <>
      <button type="button" className="btn btn-light btn-touch" onClick={() => void copy()}>
        <i className={`${state === 'copied' ? 'ri-check-line' : 'ri-file-copy-line'} me-1`} aria-hidden="true"></i>
        {state === 'copied' ? 'Enlace copiado' : LABELS[path].copy}
      </button>
      <span className="visually-hidden" role="status">{state === 'copied' ? LABELS[path].copied : ''}</span>
      {state === 'failed' && <p className="small text-break mb-0">Cópialo de aquí: {url}</p>}
    </>
  );
}
