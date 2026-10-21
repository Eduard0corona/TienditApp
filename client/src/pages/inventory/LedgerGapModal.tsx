import { useId } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { useReconciliationDetail, type InventoryReconciliationItem } from '../../api/inventoryReconciliation';
import { formatStockQuantity } from '../../utils/units';
import { describeLedgerEvent, formatSignedQuantity } from './ledgerGaps';

interface LedgerGapModalProps {
  item: InventoryReconciliationItem;
  reconciling: boolean;
  onHide: () => void;
  onReconcile: (item: InventoryReconciliationItem) => void;
}

/**
 * Por qué no cuadra (Fase 27C): where the history of one stock stops adding up to it, oldest first, each place with what
 * it adds to the difference.
 */
export default function LedgerGapModal({ item, reconciling, onHide, onReconcile }: LedgerGapModalProps) {
  const titleId = useId();
  const detail = useReconciliationDetail(item.supplyItemId, item.inventoryLocationId);
  const data = detail.data;
  const unit = item.unit;

  return (
    <Modal show onHide={onHide} size="lg" centered aria-labelledby={titleId}>
      <Modal.Header closeButton>
        <Modal.Title id={titleId}>Por qué no cuadra</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        <p className="mb-2">
          <span className="fw-semibold">{item.supplyItemName}</span> en {item.inventoryLocationName}
        </p>
        {detail.isLoading && <p className="text-muted mb-0" role="status">Revisando su historial…</p>}
        {detail.isError && (
          <div className="alert alert-danger mb-0" role="alert">{getApiErrorMessage(detail.error, 'No se pudo revisar su historial.')}</div>
        )}
        {data && (
          <>
            <p className="text-muted small">
              Existencia {formatStockQuantity(data.onHandQuantity, unit)} · el historial suma {formatStockQuantity(data.historyQuantity, unit)}
              {' · '}
              {data.difference === 0 ? 'ya cuadra' : `diferencia ${formatSignedQuantity(data.difference, unit)}`}
            </p>
            {data.events.length === 0 ? (
              <p className="mb-0">Cada movimiento dejó la existencia donde empezó el siguiente: el historial cuadra.</p>
            ) : (
              <ol className="list-group list-group-numbered mb-0">
                {data.events.map((event, index) => (
                  <li key={index} className="list-group-item d-flex justify-content-between align-items-start gap-3">
                    <span className="ms-1 me-auto text-wrap">{describeLedgerEvent(event, unit)}</span>
                    <span className="fw-semibold text-nowrap">{formatSignedQuantity(event.quantity, unit)}</span>
                  </li>
                ))}
              </ol>
            )}
            {(data.reviewedCount < data.movementCount || data.eventCount > data.events.length) && (
              <p className="form-text mb-0 mt-2">
                {data.reviewedCount < data.movementCount
                  ? `Se revisaron los ${data.reviewedCount.toLocaleString('es-MX')} movimientos más recientes de ${data.movementCount.toLocaleString('es-MX')}. `
                  : ''}
                {data.eventCount > data.events.length ? `Se muestran los ${data.events.length} lugares más recientes de ${data.eventCount}.` : ''}
              </p>
            )}
          </>
        )}
      </Modal.Body>
      <Modal.Footer>
        <button type="button" className="btn btn-light" onClick={onHide}>Cerrar</button>
        {data && data.difference !== 0 && (
          // What is reconciled is what this detail shows, read after the list.
          <button
            type="button"
            className="btn btn-primary"
            disabled={reconciling}
            onClick={() => onReconcile({ ...item, onHandQuantity: data.onHandQuantity, historyQuantity: data.historyQuantity, difference: data.difference })}
          >
            {reconciling ? 'Cuadrando…' : 'Cuadrar'}
          </button>
        )}
      </Modal.Footer>
    </Modal>
  );
}
