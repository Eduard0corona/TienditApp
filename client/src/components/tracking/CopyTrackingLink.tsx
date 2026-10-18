import { useState } from 'react';

interface CopyTrackingLinkProps {
  token: string;
  /** The page that opens the link: an order's (/seguimiento) or an appointment's (/cita, Fase 24D). */
  path?: '/seguimiento' | '/cita';
}

/**
 * Copies the link to follow an online order (Fase 24C) or an appointment booked online (Fase 24D), so a guest keeps it
 * after leaving the receipt. Where the browser does not let the page copy (an insecure address, a denied permission), the
 * link shows to copy by hand.
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
        {state === 'copied' ? 'Enlace copiado' : path === '/cita' ? 'Copiar enlace de tu cita' : 'Copiar enlace de seguimiento'}
      </button>
      <span className="visually-hidden" role="status">{state === 'copied' ? (path === '/cita' ? 'Enlace de tu cita copiado' : 'Enlace de seguimiento copiado') : ''}</span>
      {state === 'failed' && <p className="small text-break mb-0">Cópialo de aquí: {url}</p>}
    </>
  );
}
