import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { getApiErrorMessage } from '../../api/errors';
import { ordersApi } from '../../api/ordersApi';
import { type AccountOf, useCustomerAccount, usePayCustomerAccount, useWriteOffCustomerAccount } from '../../api/customerCredit';
import ChargePanel, { type ChargeResult } from '../sales/ChargePanel';
import WriteOffPanel, { type WriteOffChoice } from './WriteOffPanel';
import StatusChip from '../common/StatusChip';
import WhatsAppLink from '../common/WhatsAppLink';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { getAccess } from '../../utils/access';
import { formatDate, formatDateTime } from '../../utils/dates';
import { getPaymentMethodLabel } from '../../utils/labels';
import { getWriteOffReasonLabel } from '../../utils/writeOffReasons';
import { formatMoney, toCents } from '../../utils/money';
import { accountSummaryText, daysSince, describeDays, describeDue, describeTerm } from './customerAccount';

interface CustomerAccountModalProps {
  of: AccountOf;
  /** Shown while the account loads. */
  name?: string;
  onClose: () => void;
}

/** What the modal shows: the account, a payment to it, or a write-off (owners and admins). */
type Mode = 'account' | 'paying' | 'writingOff';

const TITLES: Record<Mode, string> = { account: 'Estado de cuenta', paying: 'Abonar a la cuenta', writingOff: 'Condonar' };

/**
 * The account of a customer (fiado, Fase 26A): what they may and do owe, the orders owed from the oldest, their latest
 * payments, «Abonar» for whoever charges and the summary for WhatsApp. Fase 26B: what is past the business's term, what
 * was forgiven, and «Condonar» for owners and admins.
 */
export default function CustomerAccountModal({ of, name, onClose }: CustomerAccountModalProps) {
  const titleId = useId();
  const toast = useToast();
  const { user } = useAuth();
  const { tenant, capabilities } = useTenant();
  const access = getAccess(user, capabilities);
  const canCharge = access.charge;
  const canWriteOff = access.isAdmin;
  const { data: account, error, isPending } = useCustomerAccount(of);
  const pay = usePayCustomerAccount();
  const writeOff = useWriteOffCustomerAccount();
  const [mode, setMode] = useState<Mode>('account');
  const paying = mode === 'paying';
  const busy = pay.isPending || writeOff.isPending;
  const [payError, setPayError] = useState('');
  const [writeOffError, setWriteOffError] = useState('');
  // Some payments went through and one did not: the panel closes, so pressing «Abonar» again never sends again the ones
  // already registered (a payment has no idempotency key), and the account shows what is still owed.
  const [partialNotice, setPartialNotice] = useState('');

  const submitPayment = async (charge: ChargeResult) => {
    if (!account) return;
    setPayError('');
    // One payment per method: each one is split over the orders owed from the oldest. Each says what the account owed
    // as this screen saw it, so a payment sent twice finds it changed and is refused instead of registered again.
    const owedCents = toCents(account.owed);
    let paidCents = 0;
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
          expectedOwed: (owedCents - paidCents) / 100,
        });
        paidCents += toCents(payment.amount);
      }
      toast.success(`Abono de ${formatMoney(paidCents / 100)} registrado.`);
      setMode('account');
    } catch (err: unknown) {
      const message = getApiErrorMessage(err, 'No se pudo registrar el abono.');
      if (paidCents > 0) {
        setMode('account');
        setPartialNotice(`Se registraron ${formatMoney(paidCents / 100)}; el resto no: ${message} Revisa lo que debe antes de cobrar lo que falta.`);
      } else {
        setPayError(message);
      }
    }
  };

  const submitWriteOff = async (choice: WriteOffChoice) => {
    if (!account) return;
    setWriteOffError('');
    try {
      // What the account owed as this screen saw it: a write-off sent twice finds it changed and is refused.
      await writeOff.mutateAsync({
        customerId: account.customerId ?? undefined,
        phone: account.customerId ? undefined : account.phone,
        orderId: choice.orderId ?? undefined,
        amount: choice.amount,
        reason: choice.reason,
        notes: choice.notes ?? undefined,
        expectedOwed: account.owed,
      });
      toast.success(`Se condonaron ${formatMoney(choice.amount)}.`);
      setMode('account');
    } catch (err: unknown) {
      setWriteOffError(getApiErrorMessage(err, 'No se pudo condonar.'));
    }
  };

  return (
    <Modal show onHide={() => !busy && onClose()} centered size="lg" fullscreen="sm-down" aria-labelledby={titleId}>
      <Modal.Header closeButton={!busy}>
        <Modal.Title id={titleId} as="h2" className="h5">
          {TITLES[mode]}
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
            {account.creditTermDays != null && (
              <p className="small mb-3">
                <span className="text-muted">Plazo de {describeTerm(account.creditTermDays)} · </span>
                {account.overdue > 0
                  ? <strong className="text-danger">Vencido <span className="ta-money">{formatMoney(account.overdue)}</span></strong>
                  : <span className="text-muted">Nada vencido</span>}
              </p>
            )}
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
                  setMode('account');
                  setPayError('');
                }}
                uploadEvidence={async (file) => {
                  const { data } = await ordersApi.uploadPaymentEvidence(file);
                  return { url: data.url, fileName: data.fileName };
                }}
              />
            ) : mode === 'writingOff' ? (
              <WriteOffPanel
                account={account}
                submitting={writeOff.isPending}
                error={writeOffError}
                onSubmit={(choice) => void submitWriteOff(choice)}
                onCancel={() => {
                  setMode('account');
                  setWriteOffError('');
                }}
              />
            ) : (
              <>
                {partialNotice && <div className="alert alert-warning py-2" role="status">{partialNotice}</div>}
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
                              {describeDue(order) && (
                                <small className={`d-block ${order.isOverdue ? 'text-danger fw-semibold' : 'text-muted'}`}>{describeDue(order)}</small>
                              )}
                            </td>
                            <td className="text-end ta-money">{formatMoney(order.total)}</td>
                            <td className="text-end">
                              <span className="ta-money">{formatMoney(order.paid)}</span>
                              {order.writtenOff > 0 && (
                                <small className="d-block text-muted">Condonado <span className="ta-money">{formatMoney(order.writtenOff)}</span></small>
                              )}
                            </td>
                            <td className="text-end fw-semibold ta-money text-danger">{formatMoney(order.balance)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                    {account.ordersCount > account.orders.length && (
                      <p className="small text-muted mt-1 mb-0">
                        {account.ordersCount - account.orders.length === 1
                          ? 'Y 1 pedido más, más reciente.'
                          : `Y ${account.ordersCount - account.orders.length} pedidos más, más recientes.`}
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

                {account.recentWriteOffs.length > 0 && (
                  <>
                    <h3 className="h6 mt-3">Lo condonado</h3>
                    <ul className="list-unstyled small mb-0">
                      {account.recentWriteOffs.map((writeOffRecord, index) => (
                        <li key={`${writeOffRecord.orderNumber}-${writeOffRecord.writtenOffAt}-${index}`}>
                          <div className="d-flex justify-content-between gap-2">
                            <span>
                              {formatDateTime(writeOffRecord.writtenOffAt)} · {getWriteOffReasonLabel(writeOffRecord.reason)} · {writeOffRecord.orderNumber}
                            </span>
                            <span className="ta-money">{formatMoney(writeOffRecord.amount)}</span>
                          </div>
                          {writeOffRecord.notes && <div className="text-muted">{writeOffRecord.notes}</div>}
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
      {account && mode === 'account' && (
        <Modal.Footer className="flex-wrap">
          <WhatsAppLink
            text={accountSummaryText(account, tenant?.name ?? 'TienditApp')}
            phone={account.phone}
            className="btn btn-light btn-touch"
          />
          {canWriteOff && account.owed > 0 && (
            <button
              type="button"
              className="btn btn-light text-danger btn-touch"
              onClick={() => {
                setPartialNotice('');
                setWriteOffError('');
                setMode('writingOff');
              }}
            >
              Condonar
            </button>
          )}
          {canCharge && account.owed > 0 && (
            <button
              type="button"
              className="btn btn-primary btn-touch"
              onClick={() => {
                setPartialNotice('');
                setMode('paying');
              }}
            >
              <i className="ri-hand-coin-line me-1" aria-hidden="true"></i>Abonar
            </button>
          )}
        </Modal.Footer>
      )}
    </Modal>
  );
}
