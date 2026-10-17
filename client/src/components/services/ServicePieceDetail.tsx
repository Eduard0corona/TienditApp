import { useId } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Offcanvas } from 'react-bootstrap';
import { Link } from 'react-router-dom';
import { ordersApi } from '../../api/ordersApi';
import { getApiErrorMessage } from '../../api/errors';
import type { OrderDto, ServiceBoardPieceDto } from '../../types';
import { formatDateTime } from '../../utils/dates';
import { getServicePieceStatusLabel } from '../../utils/labels';
import { formatDuration, getServicePriorityLabel, parseChecklist } from '../../utils/serviceDetail';
import ServicePhotosPanel from './ServicePhotosPanel';

interface ServicePieceDetailProps {
  piece: ServiceBoardPieceDto | null;
  onHide: () => void;
}

/** Everything a technician needs to take the next job: what came in, how, its photos (and the ones they add) and the rest of the order. */
export default function ServicePieceDetail({ piece, onHide }: ServicePieceDetailProps) {
  const titleId = useId();
  const orderId = piece?.orderId;
  const orderQuery = useQuery({
    queryKey: ['orders', 'detail', orderId],
    queryFn: async () => (await ordersApi.getById(orderId!)).data,
    enabled: !!orderId,
  });
  const order: OrderDto | undefined = orderQuery.data;
  const error = orderQuery.error ? getApiErrorMessage(orderQuery.error, 'No se pudo cargar el pedido.') : '';

  const detail = order?.serviceDetail;
  const fullPiece = order?.servicePieces.find((item) => item.id === piece?.pieceId);
  const checklist = parseChecklist(detail?.intakeChecklistJson);
  const description = fullPiece
    ? [fullPiece.brand, fullPiece.model, fullPiece.color, fullPiece.size, fullPiece.material].filter(Boolean).join(' · ')
    : '';

  return (
    <Offcanvas show={piece !== null} onHide={onHide} placement="end" style={{ width: 'min(480px, 100vw)' }} aria-labelledby={titleId}>
      {piece && (
        <>
          <Offcanvas.Header closeButton>
            <Offcanvas.Title id={titleId} as="h2" className="h5 mb-0">
              {piece.itemName}
              <span className="d-block small text-muted fw-normal">{piece.orderNumber} · {getServicePieceStatusLabel(piece.status)}</span>
            </Offcanvas.Title>
          </Offcanvas.Header>
          <Offcanvas.Body>
            <div className="d-flex flex-wrap gap-2 mb-3">
              <span className={`badge ${piece.priority >= 4 ? 'bg-danger' : piece.priority <= 1 ? 'bg-light text-dark border' : 'bg-secondary-subtle text-dark'}`}>
                Prioridad {getServicePriorityLabel(piece.priority).toLowerCase()}
              </span>
              {piece.isOverdue && piece.delayMinutes != null && (
                <span className="badge bg-danger">Atrasada {formatDuration(piece.delayMinutes)}</span>
              )}
              {!piece.isOverdue && piece.minutesToPromise != null && (
                <span className="badge bg-warning text-dark">Entrega en {formatDuration(piece.minutesToPromise)}</span>
              )}
            </div>

            <dl className="row small mb-3">
              <dt className="col-4">Cliente</dt>
              <dd className="col-8">
                {piece.customerName}
                {piece.customerPhone && (
                  <a className="d-block" href={`tel:${piece.customerPhone}`}>{piece.customerPhone}</a>
                )}
              </dd>
              {piece.requestedService && (
                <>
                  <dt className="col-4">Servicio</dt>
                  <dd className="col-8">{piece.requestedService}</dd>
                </>
              )}
              {description && (
                <>
                  <dt className="col-4">Pieza</dt>
                  <dd className="col-8">{description}</dd>
                </>
              )}
              {fullPiece?.conditionNotes && (
                <>
                  <dt className="col-4">Cómo llegó</dt>
                  <dd className="col-8">{fullPiece.conditionNotes}</dd>
                </>
              )}
              <dt className="col-4">Recibido</dt>
              <dd className="col-8">{formatDateTime(detail?.receivedAt ?? piece.orderCreatedAt)}</dd>
              {piece.promisedAt && (
                <>
                  <dt className="col-4">Prometido</dt>
                  <dd className={`col-8 ${piece.isOverdue ? 'text-danger fw-semibold' : ''}`}>{formatDateTime(piece.promisedAt)}</dd>
                </>
              )}
              <dt className="col-4">Asignado</dt>
              <dd className="col-8">{piece.assignedToName || 'Sin asignar'}</dd>
            </dl>

            {piece.internalNotes && <div className="alert alert-light py-2 small"><strong>Nota interna:</strong> {piece.internalNotes}</div>}
            {detail?.intakeNotes && <div className="alert alert-light py-2 small"><strong>Al recibir:</strong> {detail.intakeNotes}</div>}

            {/* The photos open for whoever works the board, also when the rest of the order is not theirs to see. */}
            <h3 className="h6">Fotos del servicio</h3>
            <ServicePhotosPanel key={piece.orderId} orderId={piece.orderId} />

            {error && <div className="alert alert-danger" role="alert">{error}</div>}
            {!order && !error && <div className="text-muted small mb-3">Cargando el pedido…</div>}

            {order && (
              <>
                <div className="mb-3">
                  <h3 className="h6">Lista de revisión al recibir</h3>
                  {checklist.length === 0 ? (
                    <div className="text-muted small">Sin lista de revisión.</div>
                  ) : (
                    <ul className="list-unstyled small mb-0">
                      {checklist.map((item, index) => (
                        <li key={`${item.label ?? 'item'}-${index}`}>
                          <i className={`${item.checked ? 'ri-checkbox-circle-fill text-success' : 'ri-checkbox-blank-circle-line text-muted'} me-1`} aria-hidden="true"></i>
                          {item.label || 'Punto'}
                          <span className="visually-hidden">{item.checked ? ' (revisado)' : ' (pendiente)'}</span>
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
                {order.servicePieces.length > 1 && (
                  <div className="mb-3">
                    <h3 className="h6">Otras piezas del pedido</h3>
                    <ul className="list-unstyled small mb-0">
                      {order.servicePieces.filter((item) => item.id !== piece.pieceId).map((item) => (
                        <li key={item.id} className="d-flex justify-content-between border-bottom py-1">
                          <span>{item.itemName}</span>
                          <span className="text-muted">{getServicePieceStatusLabel(item.status)}</span>
                        </li>
                      ))}
                    </ul>
                  </div>
                )}
                <Link to={`/orders?search=${encodeURIComponent(order.orderNumber)}`} className="btn btn-light btn-sm">
                  <i className="ri-file-list-3-line me-1" aria-hidden="true"></i>Ver el pedido
                </Link>
              </>
            )}
          </Offcanvas.Body>
        </>
      )}
    </Offcanvas>
  );
}
