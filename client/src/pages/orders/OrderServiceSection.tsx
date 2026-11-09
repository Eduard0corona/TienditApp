import { useMemo } from 'react';
import ServicePhotosPanel from '../../components/services/ServicePhotosPanel';
import type { OrderDto } from '../../types';
import { OrderType as OrderTypeConst } from '../../types';
import { SERVICE_PIECE_STATUS_LABELS } from '../../utils/labels';
import { getServicePriorityLabel, parseChecklist } from '../../utils/serviceDetail';
import { SERVICE_PIECE_STATUS_OPTIONS, getPieceAllowedTransitions, toLocalDateTime } from './ordersListModel';

interface OrderServiceSectionProps {
  order: OrderDto;
  /** The business does service orders: a service order shows its photos. */
  servesOrders: boolean;
  canSell: boolean;
  onPieceStatusChange: (orderId: string, pieceId: string, newStatus: string) => void;
}

/** What the detail of a service order adds: how it was received, its photos and the pieces tracked one by one. */
export default function OrderServiceSection({ order, servesOrders, canSell, onPieceStatusChange }: OrderServiceSectionProps) {
  const checklist = useMemo(() => order.servicePieces.filter((piece) => piece.isTrackable), [order]);
  const intakeChecklist = useMemo(() => parseChecklist(order.serviceDetail?.intakeChecklistJson), [order]);

  return (
    <>
      {order.serviceDetail && (
        <div className="mt-3">
          <h5 className="mb-2">Recepción de servicio</h5>
          <div className="row g-2">
            <div className="col-md-6"><strong>Recibido:</strong> {toLocalDateTime(order.serviceDetail.receivedAt)}</div>
            <div className="col-md-6">
              <strong>Promesa:</strong> {order.serviceDetail.promisedAt
                ? toLocalDateTime(order.serviceDetail.promisedAt)
                : '-'}
            </div>
            <div className="col-md-6"><strong>Prioridad:</strong> {getServicePriorityLabel(order.serviceDetail.priority)}</div>
          </div>
          {order.serviceDetail.intakeNotes && (
            <div className="mt-2">
              <strong>Notas de ingreso:</strong>
              <div className="text-muted">{order.serviceDetail.intakeNotes}</div>
            </div>
          )}
          {order.serviceDetail.deliveryNotes && (
            <div className="mt-2">
              <strong>Notas de entrega:</strong>
              <div className="text-muted">{order.serviceDetail.deliveryNotes}</div>
            </div>
          )}

          {intakeChecklist.length > 0 && (
            <div className="mt-2">
              <strong>Checklist de recepción:</strong>
              <ul className="mb-0 mt-1 ps-3">
                {intakeChecklist.map((item, index) => (
                  <li key={`${item.label || 'item'}-${index}`}>
                    {item.checked ? '[x]' : '[ ]'} {item.label || 'Item'}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}

      {order.orderType === OrderTypeConst.Service && servesOrders && (
        <div className="mt-3">
          <h5 className="mb-2">Fotos del servicio</h5>
          <ServicePhotosPanel key={order.id} orderId={order.id} orderStatus={order.status} headingLevel={6} />
        </div>
      )}

      {checklist.length > 0 && (
        <div className="mt-3">
          <h5 className="mb-2">Checklist de servicios rastreables</h5>
          <div className="table-responsive">
            <table className="table table-sm table-striped mb-0">
              <thead>
                <tr>
                  <th>Servicio</th>
                  <th>Notificar</th>
                  <th>Estado</th>
                  <th className="text-end">Acción</th>
                </tr>
              </thead>
              <tbody>
                {checklist.map((piece) => (
                  <tr key={piece.id}>
                    <td>{piece.itemName}</td>
                    <td>{piece.notifyCustomer ? 'Si' : 'No'}</td>
                    <td>{SERVICE_PIECE_STATUS_LABELS[piece.status] ?? piece.status}</td>
                    <td className="text-end" style={{ minWidth: 220 }}>
                      <select
                        className="form-select form-select-sm"
                        value=""
                        aria-label={`Actualizar estado de ${piece.itemName}`}
                        disabled={!canSell || getPieceAllowedTransitions(piece.status).length === 0}
                        onChange={(e) =>
                          onPieceStatusChange(order.id, piece.id, e.target.value)
                        }
                      >
                        <option value="">Actualizar estado</option>
                        {SERVICE_PIECE_STATUS_OPTIONS
                          .filter((status) => getPieceAllowedTransitions(piece.status).includes(status.value))
                          .map((status) => (
                            <option key={status.value} value={status.value}>
                              {status.label}
                            </option>
                          ))}
                      </select>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </>
  );
}
