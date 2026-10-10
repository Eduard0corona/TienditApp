import { useId } from 'react';
import { Modal } from 'react-bootstrap';
import type { InventoryMovementDto } from '../../types';
import { formatMoney, formatUnitCost } from '../../utils/money';
import { formatStockQuantity } from '../../utils/units';
import { formatDate, formatDateTime, getMovementReasonLabel, getMovementTypeLabel } from './inventoryLabels';

interface MovementDetailModalProps {
  show: boolean;
  loading: boolean;
  movement: InventoryMovementDto | null;
  onHide: () => void;
}

/** A movement with what each line left before and after. */
export default function MovementDetailModal({ show, loading, movement, onHide }: MovementDetailModalProps) {
  const titleId = useId();
  return (
    <Modal show={show} onHide={onHide} size="lg" centered aria-labelledby={titleId}>
      <Modal.Header closeButton>
        <Modal.Title id={titleId}>Detalle de movimiento</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {loading ? (
          <div className="text-center py-4">
            <div className="spinner-border text-primary" role="status"></div>
          </div>
        ) : movement ? (
          <>
            <div className="row mb-3">
              <div className="col-md-6">
                <p className="mb-1"><strong>Folio:</strong> {movement.movementNumber}</p>
                <p className="mb-1"><strong>Tipo:</strong> {getMovementTypeLabel(movement.movementType)}</p>
                <p className="mb-1"><strong>Motivo:</strong> {getMovementReasonLabel(movement.movementReason)}</p>
              </div>
              <div className="col-md-6">
                <p className="mb-1"><strong>Fecha:</strong> {formatDateTime(movement.movementDate)}</p>
                <p className="mb-1"><strong>Referencia:</strong> {movement.referenceNumber || '-'}</p>
                <p className="mb-1"><strong>Monto:</strong> {formatMoney(movement.totalAmount)}</p>
              </div>
            </div>

            <div className="table-responsive">
              <table className="table table-sm table-striped mb-2">
                <thead>
                  <tr>
                    <th>Insumo</th>
                    <th>Ubicación</th>
                    <th className="text-end">Cantidad</th>
                    <th className="text-end">Costo unit.</th>
                    <th className="text-end">Total</th>
                    <th className="text-end">Antes</th>
                    <th className="text-end">Después</th>
                    <th>Lote</th>
                    <th>Caducidad</th>
                  </tr>
                </thead>
                <tbody>
                  {movement.lines.map((line) => (
                    <tr key={line.id}>
                      <td>{line.supplyItemName}</td>
                      <td>{line.inventoryLocationName}</td>
                      <td className="text-end">{formatStockQuantity(line.quantity)}</td>
                      <td className="text-end">{line.unitCost !== undefined ? formatUnitCost(line.unitCost) : '-'}</td>
                      <td className="text-end">{line.totalCost !== undefined ? formatMoney(line.totalCost) : '-'}</td>
                      <td className="text-end">{formatStockQuantity(line.quantityBefore)}</td>
                      <td className="text-end">{formatStockQuantity(line.quantityAfter)}</td>
                      <td>{line.lotNumber || '-'}</td>
                      <td>{formatDate(line.expirationDate)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {movement.notes && (
              <div className="mt-2">
                <strong>Notas:</strong> {movement.notes}
              </div>
            )}
          </>
        ) : (
          <p className="text-muted mb-0">No se encontró información.</p>
        )}
      </Modal.Body>
    </Modal>
  );
}
