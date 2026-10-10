import { useOnlineStatus } from '../../hooks/useOnlineStatus';

interface NewVersionNoticeProps {
  onClose: () => void;
}

/**
 * Tells an open page that a newer version is deployed, so a till left open since the morning picks up the changes.
 * Reloading is the person's call: a sale or a form may be half done. Without connection it offers no reload, which
 * would leave a till selling offline on the browser's error page.
 */
export default function NewVersionNotice({ onClose }: NewVersionNoticeProps) {
  const online = useOnlineStatus();

  return (
    <div className="ta-new-version ta-toast ta-toast-info" role="status">
      <i className="ri-refresh-line ta-toast-icon" aria-hidden="true"></i>
      <div className="ta-toast-body">
        <div className="fw-bold">Hay una versión nueva de TienditApp</div>
        {online ? (
          <>
            <div className="small">Actualiza cuando termines lo que estás haciendo.</div>
            <button type="button" className="btn btn-sm btn-primary mt-2" onClick={() => window.location.reload()}>
              Actualizar
            </button>
          </>
        ) : (
          <div className="small">Actualiza cuando vuelva la conexión.</div>
        )}
      </div>
      <button type="button" className="btn-close btn-sm" aria-label="Cerrar aviso" onClick={onClose} />
    </div>
  );
}
