import { formatDateTime } from '../../utils/dates';

interface SellNoticesProps {
  /** When the catalog Vender sells with without connection was saved; null with connection. */
  offlineCatalogAt: string | null;
  cashClosed: boolean;
  /** The business takes orders of a delivery platform: those are registered without a drawer. */
  hasPlatforms: boolean;
  onOpenDrawer: () => void;
}

/** What Vender warns of above the catalog: selling without connection, and a closed drawer. */
export default function SellNotices({ offlineCatalogAt, cashClosed, hasPlatforms, onOpenDrawer }: SellNoticesProps) {
  return (
    <>
      {offlineCatalogAt && (
        <div className="alert alert-warning mt-3 mb-0" role="status">
          <i className="ri-wifi-off-line me-1" aria-hidden="true"></i>
          Sin conexión: vendes con el catálogo guardado ({formatDateTime(offlineCatalogAt)}). Las ventas cobradas se guardan
          en este dispositivo y se envían solas al volver la conexión.
        </div>
      )}

      {cashClosed && (
        <div className="alert alert-warning d-flex flex-wrap align-items-center justify-content-between gap-2 mt-3 mb-0" role="status">
          <span>
            <i className="ri-safe-2-line me-1" aria-hidden="true"></i>
            {hasPlatforms
              ? 'Tu caja está cerrada: ábrela para cobrar. Sin ella puedes guardar pedidos y registrar los que ya cobró una plataforma.'
              : 'Tu caja está cerrada: ábrela para cobrar. Todo cobro se registra en una caja; sin ella solo puedes guardar pedidos.'}
          </span>
          <button type="button" className="btn btn-sm btn-warning" onClick={onOpenDrawer}>
            Abrir caja
          </button>
        </div>
      )}
    </>
  );
}
