import { useState } from 'react';

/**
 * Copies the link to follow an online order (Fase 24C), so a guest keeps it after leaving the receipt. Where the browser
 * does not let the page copy (an insecure address, a denied permission), the link shows to copy by hand.
 */
export default function CopyTrackingLink({ token }: { token: string }) {
  const [state, setState] = useState<'idle' | 'copied' | 'failed'>('idle');
  const url = `${window.location.origin}/seguimiento#${token}`;
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
        {state === 'copied' ? 'Enlace copiado' : 'Copiar enlace de seguimiento'}
      </button>
      <span className="visually-hidden" role="status">{state === 'copied' ? 'Enlace de seguimiento copiado' : ''}</span>
      {state === 'failed' && <p className="small text-break mb-0">Cópialo de aquí: {url}</p>}
    </>
  );
}
