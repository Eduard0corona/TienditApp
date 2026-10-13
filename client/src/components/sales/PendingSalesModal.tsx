import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { useConfirm } from '../../hooks/useConfirm';
import { useOnlineStatus } from '../../hooks/useOnlineStatus';
import { usePendingSales } from '../../hooks/usePendingSales';
import { useToast } from '../../hooks/useToast';
import { discardPendingSale, retryPendingSale, type PendingSale } from '../../offline/saleQueue';
import { formatDateTime } from '../../utils/dates';
import { formatMoney } from '../../utils/money';

/** The sales charged without connection that have not reached the server, with what to do about each one. */
export default function PendingSalesModal({ onClose }: { onClose: () => void }) {
  const titleId = useId();
  const toast = useToast();
  const { confirm } = useConfirm();
  const online = useOnlineStatus();
  const { sales, sync } = usePendingSales();
  const [sending, setSending] = useState(false);

  const sendNow = async () => {
    setSending(true);
    try {
      const result = await sync();
      if (!result) toast.error('Sin conexión: se enviarán cuando vuelva.');
      else if (result.interrupted) toast.error('Se perdió la conexión otra vez: el resto se envía después.');
      else if (result.sent.length > 0) toast.success(`${result.sent.length} ${result.sent.length === 1 ? 'venta enviada' : 'ventas enviadas'}.`);
    } finally {
      setSending(false);
    }
  };

  const retry = async (sale: PendingSale) => {
    await retryPendingSale(sale);
    await sendNow();
  };

  const discard = async (sale: PendingSale) => {
    const accepted = await confirm({
      title: `Descartar la venta ${sale.folio}`,
      message: `No se registrará en el sistema. Si ya cobraste ${formatMoney(sale.total)}, regístrala de nuevo en Vender.`,
      confirmLabel: 'Descartar',
      danger: true,
    });
    if (!accepted) return;
    await discardPendingSale(sale.id);
    toast.success(`Venta ${sale.folio} descartada.`);
  };

  return (
    <Modal show onHide={onClose} centered scrollable aria-labelledby={titleId}>
      <Modal.Header closeButton>
        <Modal.Title id={titleId}>Ventas sin enviar</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        <p className="small text-muted">
          Se cobraron sin conexión y están guardadas en este dispositivo. Se envían solas al volver la conexión; el servidor
          vuelve a calcular cada venta y le da su folio definitivo.
        </p>
        {sales.length === 0 ? (
          <p className="text-muted mb-0">No hay ventas pendientes.</p>
        ) : (
          <ul className="list-group">
            {sales.map((sale) => (
              <li key={sale.id} className="list-group-item">
                <div className="d-flex justify-content-between align-items-start gap-2">
                  <div>
                    <div className="fw-semibold">{sale.folio} · <span className="ta-money">{formatMoney(sale.total)}</span></div>
                    <div className="small text-muted">{formatDateTime(sale.capturedAt)} · {sale.request.items.length} {sale.request.items.length === 1 ? 'producto' : 'productos'}</div>
                  </div>
                  <span className={`badge ${sale.status === 'failed' ? 'text-bg-danger' : 'text-bg-warning'}`}>
                    {sale.status === 'failed' ? 'No aceptada' : 'Pendiente'}
                  </span>
                </div>
                {sale.status === 'pending' && sale.error && (
                  <div className="small text-muted mt-1">Último intento: {sale.error}</div>
                )}
                {sale.status === 'failed' && (
                  <>
                    <div className="small text-danger mt-1">{sale.error}</div>
                    <div className="d-flex gap-2 mt-2">
                      <button type="button" className="btn btn-sm btn-light" disabled={sending || !online} onClick={() => void retry(sale)}>Reintentar</button>
                      <button type="button" className="btn btn-sm btn-outline-danger" onClick={() => void discard(sale)}>Descartar</button>
                    </div>
                  </>
                )}
              </li>
            ))}
          </ul>
        )}
      </Modal.Body>
      <Modal.Footer>
        <button type="button" className="btn btn-light" onClick={onClose}>Cerrar</button>
        <button type="button" className="btn btn-primary" disabled={sending || !online || !sales.some((sale) => sale.status === 'pending')}
          onClick={() => void sendNow()}>
          {sending ? 'Enviando…' : online ? 'Enviar ahora' : 'Sin conexión'}
        </button>
      </Modal.Footer>
    </Modal>
  );
}
