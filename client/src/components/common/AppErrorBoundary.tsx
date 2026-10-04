import { Component, type ErrorInfo, type ReactNode } from 'react';
import { isChunkLoadError, reloadOnceForNewVersion } from '../../utils/chunkReload';

interface AppErrorBoundaryProps {
  children: ReactNode;
  /** A new value (the route) clears the error, so moving to another screen recovers. */
  resetKey?: string;
}

interface AppErrorBoundaryState {
  error: Error | null;
}

/**
 * Catches a screen that fails to render, instead of leaving the app blank. A screen that cannot load after
 * a deploy (its file no longer exists) reloads the page once to get the new version.
 */
export default class AppErrorBoundary extends Component<AppErrorBoundaryProps, AppErrorBoundaryState> {
  state: AppErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): AppErrorBoundaryState {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    if (isChunkLoadError(error) && reloadOnceForNewVersion()) return;
    console.error('La pantalla no se pudo mostrar', error, info.componentStack);
  }

  componentDidUpdate(previous: AppErrorBoundaryProps) {
    if (this.state.error && previous.resetKey !== this.props.resetKey) this.setState({ error: null });
  }

  render() {
    const { error } = this.state;
    if (!error) return this.props.children;

    const newVersion = isChunkLoadError(error);
    return (
      <div className="py-5 px-2">
        <div className="card mx-auto" style={{ maxWidth: 520 }}>
          <div className="card-body text-center p-4" role="alert">
            <i className={`${newVersion ? 'ri-refresh-line' : 'ri-error-warning-line'} fs-1 text-primary`} aria-hidden="true"></i>
            <h4 className="mt-2">{newVersion ? 'Hay una versión nueva de TienditApp' : 'Esta pantalla no se pudo mostrar'}</h4>
            <p className="text-muted mb-4">
              {newVersion
                ? 'Recarga para usarla. Lo que ya guardaste no se pierde.'
                : 'Recarga la página. Si vuelve a pasar, regresa al inicio y avísanos qué estabas haciendo.'}
            </p>
            <div className="d-flex justify-content-center flex-wrap gap-2">
              <button type="button" className="btn btn-primary" onClick={() => window.location.reload()}>
                Recargar
              </button>
              <a className="btn btn-light" href="/">Ir al inicio</a>
            </div>
          </div>
        </div>
      </div>
    );
  }
}
