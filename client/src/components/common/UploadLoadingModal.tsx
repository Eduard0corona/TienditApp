import { createPortal } from 'react-dom';

type UploadLoadingModalProps = {
  show: boolean;
  title?: string;
  description?: string;
};

export default function UploadLoadingModal({
  show,
  title = 'Subiendo imagen',
  description = 'Espera un momento mientras procesamos el archivo.',
}: UploadLoadingModalProps) {
  if (!show || typeof document === 'undefined') return null;

  return createPortal(
    <div
      style={{
        position: 'fixed',
        inset: 0,
        backgroundColor: 'rgba(15, 23, 42, 0.55)',
        zIndex: 99999,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: '1rem',
      }}
    >
      <div
        className="card border-0 shadow-lg"
        style={{ width: '100%', maxWidth: 420 }}
      >
        <div className="card-body text-center p-4">
          <div
            className="spinner-border text-primary"
            role="status"
            style={{ width: '3rem', height: '3rem' }}
          >
            <span className="visually-hidden">Subiendo...</span>
          </div>
          <h5 className="mt-3 mb-1">{title}</h5>
          <p className="text-muted mb-0">{description}</p>
        </div>
      </div>
    </div>,
    document.body,
  );
}
