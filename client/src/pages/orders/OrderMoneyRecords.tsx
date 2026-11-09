import PrivateFileLink from '../../components/common/PrivateFileLink';
import type { OrderDto } from '../../types';
import { OrderPaymentMethod } from '../../types';
import { getPaymentMethodLabel } from '../../utils/labels';
import { formatMoney } from '../../utils/money';
import { describeOnlineDispute, describeOnlineRefund, isDisputeRefund } from '../../utils/onlineRefunds';
import { toPrivateFilePath } from '../../utils/privateFiles';
import { getWriteOffReasonLabel } from '../../utils/writeOffReasons';
import { getPaymentMethodText, toLocalDateTime } from './ordersListModel';

/**
 * The money of an order in its detail, as it happened: the dispute of its online payment, then what was paid, refunded and
 * written off. Each record stays; a correction is one more record (a refund, a write-off undone), never an edit.
 */
export default function OrderMoneyRecords({ order }: { order: OrderDto }) {
  const refunds = order.refunds ?? [];
  const writeOffs = order.writeOffs ?? [];
  const dispute = describeOnlineDispute(order.onlineDispute, refunds.some((refund) => isDisputeRefund(refund.onlineReason)));

  return (
    <>
      {dispute && (
        <div className={`alert alert-${dispute.variant} mt-3 mb-0`} role="status">
          <strong>{dispute.title}.</strong> {dispute.text}
          {order.onlineDisputeUpdatedAt && (
            <div className="small">Última noticia de Mercado Pago: {toLocalDateTime(order.onlineDisputeUpdatedAt)}</div>
          )}
        </div>
      )}

      {order.payments.length > 0 && (
        <div className="mt-3">
          <h5 className="mb-2">Pagos</h5>
          <div className="table-responsive">
            <table className="table table-sm table-striped mb-0">
              <thead>
                <tr>
                  <th>Fecha</th>
                  <th>Forma de pago</th>
                  <th>Referencia</th>
                  <th>Comprobante</th>
                  <th className="text-end">Monto</th>
                </tr>
              </thead>
              <tbody>
                {order.payments.map((payment) => (
                  <tr key={payment.id}>
                    <td>{toLocalDateTime(payment.paidAt)}</td>
                    <td>{getPaymentMethodText(order, payment)}</td>
                    {/* The folio of what the platform charged is its reference: it already reads with the method. */}
                    <td>{(payment.method === OrderPaymentMethod.Platform ? '' : payment.reference) || '-'}</td>
                    <td>
                      {toPrivateFilePath(payment.evidenceUrl) ? (
                        <PrivateFileLink path={toPrivateFilePath(payment.evidenceUrl)!}>Ver</PrivateFileLink>
                      ) : '-'}
                    </td>
                    <td className="text-end">{formatMoney(payment.amount)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {refunds.length > 0 && (
        <div className="mt-3">
          <h5 className="mb-2">Reembolsos</h5>
          <div className="table-responsive">
            <table className="table table-sm table-striped mb-0">
              <thead>
                <tr>
                  <th>Fecha</th>
                  <th>Forma</th>
                  <th>Motivo</th>
                  <th>Artículos</th>
                  <th className="text-end">Monto</th>
                </tr>
              </thead>
              <tbody>
                {refunds.map((refund) => (
                  <tr key={refund.id}>
                    <td>
                      {toLocalDateTime(refund.refundedAt)}
                      <div className="text-muted small">{refund.refundedBy}</div>
                    </td>
                    <td>
                      {getPaymentMethodLabel(refund.method)}
                      {refund.method === OrderPaymentMethod.Online && (() => {
                        const online = describeOnlineRefund(refund.onlineStatus, refund.onlineFailure, refund.onlineReason);
                        return online && <div className={`small text-${online.tone}`}>{online.text}</div>;
                      })()}
                    </td>
                    <td>{refund.reason}</td>
                    <td>
                      {refund.lines.length === 0 ? '-' : refund.lines.map((line) => {
                        const item = order.items.find((candidate) => candidate.id === line.orderItemId);
                        return <div key={line.orderItemId}>{line.quantity} × {item?.productName ?? 'Artículo'}</div>;
                      })}
                    </td>
                    <td className="text-end text-warning">−{formatMoney(refund.amount)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {writeOffs.length > 0 && (
        <div className="mt-3">
          <h5 className="mb-2">Condonado</h5>
          <p className="small text-muted mb-2">Lo que el negocio condonó o descontó de lo que debía el cliente: no salió dinero de la caja.</p>
          <div className="table-responsive">
            <table className="table table-sm table-striped mb-0">
              <thead>
                <tr>
                  <th>Fecha</th>
                  <th>Motivo</th>
                  <th className="text-end">Monto</th>
                </tr>
              </thead>
              <tbody>
                {writeOffs.map((writeOff) => (
                  <tr key={writeOff.id}>
                    <td>
                      {toLocalDateTime(writeOff.writtenOffAt)}
                      <div className="text-muted small">{writeOff.writtenOffBy}</div>
                    </td>
                    <td>
                      {getWriteOffReasonLabel(writeOff.reason)}
                      {writeOff.notes && <div className="text-muted small">{writeOff.notes}</div>}
                    </td>
                    <td className="text-end">
                      <span className={writeOff.reversedAt ? 'text-muted text-decoration-line-through' : ''}>−{formatMoney(writeOff.amount)}</span>
                      {writeOff.reversedAt && (
                        <div className="text-muted small">Deshecho el {toLocalDateTime(writeOff.reversedAt)}{writeOff.reversedBy ? ` por ${writeOff.reversedBy}` : ''}</div>
                      )}
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
