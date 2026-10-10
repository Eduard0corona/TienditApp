import { useState } from 'react';
import { useNewVersion } from '../../hooks/useNewVersion';

/**
 * Tells an open page that a newer version is deployed, so a till left open since the morning picks up the changes.
 * Reloading is the person's call: a sale or a form may be half done.
 */
export default function NewVersionNotice() {
  const available = useNewVersion();
  const [dismissed, setDismissed] = useState(false);
  if (!available || dismissed) return null;

  return (
    <div className="ta-new-version ta-toast ta-toast-info" role="status">
      <i className="ri-refresh-line ta-toast-icon" aria-hidden="true"></i>
      <div className="ta-toast-body">
        <div className="fw-bold">Hay una versión nueva de TienditApp</div>
        <div className="small">Actualiza cuando termines lo que estás haciendo.</div>
        <button type="button" className="btn btn-sm btn-primary mt-2" onClick={() => window.location.reload()}>
          Actualizar
        </button>
      </div>
      <button type="button" className="btn-close btn-sm" aria-label="Cerrar aviso" onClick={() => setDismissed(true)} />
    </div>
  );
}
