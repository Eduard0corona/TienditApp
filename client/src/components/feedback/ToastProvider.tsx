import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react';
import { ToastContext, type ToastApi, type ToastOptions, type ToastVariant } from './feedbackContexts';

interface ToastItem {
  id: number;
  variant: ToastVariant;
  message: string;
  title?: string;
}

const DEFAULT_DURATION_MS: Record<ToastVariant, number> = {
  success: 4000,
  info: 5000,
  warning: 7000,
  error: 8000,
};

const ICONS: Record<ToastVariant, string> = {
  success: 'ri-checkbox-circle-fill',
  info: 'ri-information-fill',
  warning: 'ri-error-warning-fill',
  error: 'ri-close-circle-fill',
};

// At most this many at once: a burst of errors should not cover the screen.
const MAX_VISIBLE = 4;

/** Short notices for the result of an action, announced to screen readers. */
export default function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const nextId = useRef(1);
  const timers = useRef(new Map<number, number>());

  const dismiss = useCallback((id: number) => {
    window.clearTimeout(timers.current.get(id));
    timers.current.delete(id);
    setToasts((current) => current.filter((toast) => toast.id !== id));
  }, []);

  const show = useCallback(
    (variant: ToastVariant, message: string, options?: ToastOptions) => {
      const id = nextId.current++;
      setToasts((current) => [...current, { id, variant, message, title: options?.title }].slice(-MAX_VISIBLE));
      const duration = options?.durationMs ?? DEFAULT_DURATION_MS[variant];
      timers.current.set(id, window.setTimeout(() => dismiss(id), duration));
    },
    [dismiss],
  );

  const api = useMemo<ToastApi>(
    () => ({
      show,
      success: (message, options) => show('success', message, options),
      error: (message, options) => show('error', message, options),
      info: (message, options) => show('info', message, options),
      warning: (message, options) => show('warning', message, options),
    }),
    [show],
  );

  return (
    <ToastContext.Provider value={api}>
      {children}
      <div className="ta-toast-stack" aria-live="polite" aria-atomic="false">
        {toasts.map((toast) => (
          <div
            key={toast.id}
            className={`ta-toast ta-toast-${toast.variant}`}
            role={toast.variant === 'error' ? 'alert' : 'status'}
          >
            <i className={`${ICONS[toast.variant]} ta-toast-icon`} aria-hidden="true"></i>
            <div className="ta-toast-body">
              {toast.title && <div className="fw-bold">{toast.title}</div>}
              <div>{toast.message}</div>
            </div>
            <button type="button" className="btn-close btn-sm" aria-label="Cerrar aviso" onClick={() => dismiss(toast.id)} />
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}
