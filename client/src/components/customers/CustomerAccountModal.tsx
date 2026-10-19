import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { ordersApi } from '../../api/ordersApi';
import { type AccountOf, useCustomerAccount, usePayCustomerAccount } from '../../api/customerCredit';
import ChargePanel, { type ChargeResult } from '../sales/ChargePanel';
import StatusChip from '../common/StatusChip';
import WhatsAppLink from '../common/WhatsAppLink';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { getAccess } from '../../utils/access';
import { formatDate, formatDateTime } from '../../utils/dates';
import { getPaymentMethodLabel } from '../../utils/labels';
import { formatMoney } from '../../utils/money';
import { accountSummaryText, daysSince, describeDays } from './customerAccount';

interface CustomerAccountModalProps {
  of: AccountOf;
  /** Shown while the account loads. */
  name?: string;
  onClose: () => void;
}

/**
 * The account of a customer (fiado, Fase 26A): what they may and do owe, the orders owed from the oldest, their latest
 * payments, «Abonar» for whoever charges and the summary for WhatsApp.
 */
export default function CustomerAccountModal({ of, name, onClose }: CustomerAccountModalProps) {
  const titleId = useId();
  const toast = useToast();
  const { user } = useAuth();
  const { tenant, capabilities } = useTenant();
  const canCharge = getAccess(user, capabilities).charge;
  const { data: account, error, isPending } = useCustomerAccount(of);
  const pay = usePayCustomerAccount();
  const [paying, setPaying] = useState(false);
  const [payError, setPayError] = useState('');

  const submitPayment = async (charge: ChargeResult) => {
    if (!account) return;
    setPayError('');
    // One payment per method: each one is split over the orders owed from the oldest.
    let paid = 0;
    try {
      for (const payment of charge.payments) {
        await pay.mutateAsync({
          customerId: account.customerId ?? undefined,
          phone: account.customerId ? undefined : account.phone,
          amount: payment.amount,
          method: payment.method,
          reference: payment.reference,
          evidenceUrl: payment.evidenceUrl,
          cashReceived: payment.cashReceived,
        });
        paid += payment.amount;
      }
      toast.success(`Abono de ${formatMoney(paid)} registrado.`);
      setPaying(false);
    } catch (err: unknown) {
      const message = getApiErrorMessage(err, 'No se pudo registrar el abono.');
      setPayError(paid > 0 ? `Se registraron ${formatMoney(paid)}; el resto no: ${message}` : message);
    }
  };

  return (
    <Modal show onHide={() => !pay.isPending && onClose()} centered size="lg" fullscreen="sm-down" aria-labelledby={titleId}>
      <Modal.Header closeButton={!pay.isPending}>
        <Modal.Title id={titleId} as="h2" className="h5">
          {paying ? 'Abonar a la cuenta' : 'Estado de cuenta'}
        </Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {error && <div className="alert alert-warning mb-0">{getApiErrorMessage(error, 'No se pudo cargar la cuenta.')}</div>}
        {!error && (isPending || !account) && <p className="text-muted mb-0">Cargando cuenta{name ? ` de ${name}` : ''}…</p>}
        {account && (
          <>
            <div className="mb-3">
              <div className="fw-semibold">{account.name ?? name ?? 'Cliente'}</div>
              <div className="text-muted small">{account.phone}</div>
            </div>

            <div className="row g-2 mb-3 text-center">
              <div className="col-4">
                <div className={`border rounded p-2 h-100 ${account.owed > 0 ? 'border-danger' : ''}`}>
                  <div className="small text-muted">Debe</div>
                  <div className={`fw-semibold ta-money ${account.owed > 0 ? 'text-danger' : ''}`}>{formatMoney(account.owed)}</div>
                </div>
              </div>
              <div className="col-4">
                <div className="border rounded p-2 h-100">
                  <div className="small text-muted">Límite</div>
                  <div className="fw-semibold ta-money">{account.creditLimit != null ? formatMoney(account.creditLimit) : 'Sin fiado'}</div>
                </div>
              </div>
              <div className="col-4">
                <div className="border rounded p-2 h-100">
                  <div className="small text-muted">Le quedan</div>
                  <div className="fw-semibold ta-money">{account.available != null ? formatMoney(account.available) : '—'}</div>
                </div>
              </div>
            </div>
            {account.overLimit && (
              <p className="small text-danger mb-3">Debe más que su límite: no se le fía más hasta que abone.</p>
            )}
            {account.creditLimit != null && !account.sellsOnCredit && (
              <p className="small text-muted mb-3">Tiene límite, pero ahora no se le fía: el cliente está desactivado o no tiene teléfono.</p>
            )}

            {paying ? (
              <ChargePanel
                amountDue={account.owed}
                submitting={pay.isPending}
                allowPartial
                allowTip={false}
                confirmLabel="Abonar"
                error={payError}
                onSubmit={(charge) => void submitPayment(charge)}
                onCancel={() => {
                  setPaying(false);
                  setPayError('');
                }}
                uploadEvidence={async (file) => {
                  const { data } = await ordersApi.uploadPaymentEvidence(file);
                  return { url: data.url, fileName: data.fileName };
                }}
              />
            ) : (
              <>
                <h3 className="h6">Lo que debe</h3>
                {account.orders.length === 0 ? (
                  <p className="text-muted small">No debe nada.</p>
                ) : (
                  <div className="table-responsive mb-3">
                    <table className="table table-sm align-middle mb-0">
                      <thead>
                        <tr>
                          <th scope="col">Pedido</th>
                          <th scope="col" className="text-end">Total</th>
                          <th scope="col" className="text-end">Pagado</th>
                          <th scope="col" className="text-end">Debe</th>
                        </tr>
                      </thead>
                      <tbody>
                        {account.orders.map((order) => (
                          <tr key={order.orderId}>
                            <td>
                              <div className="fw-semibold">
                                {order.orderNumber}
                                {order.soldOnCredit && <span className="badge ms-1 bg-warning-subtle text-warning-emphasis">Fiado</span>}
                              </div>
                              <small className="text-muted">
                                {formatDate(order.createdAt)} · {describeDays(daysSince(order.createdAt))}
                              </small>{' '}
                              <StatusChip status={order.status} />
                            </td>
                            <td className="text-end ta-money">{formatMoney(order.total)}</td>
                            <td className="text-end ta-money">{formatMoney(order.paid)}</td>
                            <td className="text-end fw-semibold ta-money text-danger">{formatMoney(order.balance)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                    {account.ordersCount > account.orders.length && (
                      <p className="small text-muted mt-1 mb-0">
                        Y {account.ordersCount - account.orders.length} pedidos más, más recientes.
                      </p>
                    )}
                  </div>
                )}

                {account.recentPayments.length > 0 && (
                  <>
                    <h3 className="h6">Últimos pagos</h3>
                    <ul className="list-unstyled small mb-0">
                      {account.recentPayments.map((payment, index) => (
                        <li key={`${payment.orderNumber}-${payment.paidAt}-${index}`} className="d-flex justify-content-between gap-2">
                          <span>{formatDateTime(payment.paidAt)} · {getPaymentMethodLabel(payment.method)} · {payment.orderNumber}</span>
                          <span className="ta-money">{formatMoney(payment.amount)}</span>
                        </li>
                      ))}
                    </ul>
                  </>
                )}
              </>
            )}
          </>
        )}
      </Modal.Body>
      {account && !paying && (
        <Modal.Footer className="flex-wrap">
          <WhatsAppLink
            text={accountSummaryText(account, tenant?.name ?? 'TienditApp')}
            phone={account.phone}
            className="btn btn-light btn-touch"
          />
          {canCharge && account.owed > 0 && (
            <button type="button" className="btn btn-primary btn-touch" onClick={() => setPaying(true)}>
              <i className="ri-hand-coin-line me-1" aria-hidden="true"></i>Abonar
            </button>
          )}
        </Modal.Footer>
      )}
    </Modal>
  );
}
