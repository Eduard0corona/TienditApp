import { useId, useState } from 'react';
import { Modal } from 'react-bootstrap';
import { useQueryClient } from '@tanstack/react-query';
import { getApiErrorMessage } from '../../api/errors';
import { ordersApi } from '../../api/ordersApi';
import {
  type AccountOf, type CustomerAccount, type CustomerAccountWriteOff, creditKeys, useAddCustomerDeposit, useCustomerAccount,
  usePayCustomerAccount, useReturnCustomerBalance, useReverseWriteOff, useWriteOffCustomerAccount,
} from '../../api/customerCredit';
import { OrderPaymentMethod } from '../../types';
import ChargePanel, { type ChargeResult } from '../sales/ChargePanel';
import BalanceMovementPanel, { type BalanceMovement, type BalanceMovementKind } from './BalanceMovementPanel';
import WriteOffPanel, { type WriteOffChoice } from './WriteOffPanel';
import StatusChip from '../common/StatusChip';
import WhatsAppLink from '../common/WhatsAppLink';
import { useAuth } from '../../hooks/useAuth';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { getAccess } from '../../utils/access';
import { formatDate, formatDateTime } from '../../utils/dates';
import { getPaymentMethodLabel } from '../../utils/labels';
import { getWriteOffReasonLabel } from '../../utils/writeOffReasons';
import { formatMoney, toCents } from '../../utils/money';
import {
  accountSummaryText, daysSince, describeBalanceEntry, describeDays, describeDue, describeReminder, describeTerm,
} from './customerAccount';

interface CustomerAccountModalProps {
  of: AccountOf;
  /** Shown while the account loads. */
  name?: string;
  onClose: () => void;
}

/**
 * What the modal shows: the account, a payment to it, a write-off (owners and admins), or a movement of the balance in
 * their favor (Fase 26D): leaving it, using it or giving it back.
 */
type Mode = 'account' | 'paying' | 'writingOff' | BalanceMovementKind;

const TITLES: Record<Mode, string> = {
  account: 'Estado de cuenta',
  paying: 'Abonar a la cuenta',
  writingOff: 'Condonar',
  deposit: 'Dejar a favor',
  use: 'Usar saldo a favor',
  return: 'Devolver saldo a favor',
};

/**
 * The account of a customer (fiado, Fase 26A): what they may and do owe, the orders owed from the oldest, their latest
 * payments, «Abonar» for whoever charges and the summary for WhatsApp. Fase 26B: what is past the business's term, what
 * was forgiven, and «Condonar» for owners and admins. Fase 26C: whether the term is the customer's own, and the WhatsApp
 * reminders of what is overdue. Fase 26D: what they have in their favor and its movements; whoever charges leaves it,
 * uses it or leaves the change of a payment in it, and who may also refund gives it back.
 */
export default function CustomerAccountModal({ of, name, onClose }: CustomerAccountModalProps) {
  const titleId = useId();
  const toast = useToast();
  const { user } = useAuth();
  const { tenant, capabilities } = useTenant();
  const access = getAccess(user, capabilities);
  const canCharge = access.charge;
  const canWriteOff = access.isAdmin;
  // Giving it back takes money out like a refund: whoever charges here (as Abonar, access.charge) and also refunds. The API
  // asks ChargeOrders + ProcessRefunds; like every charge, the screen also asks ManageOrders.
  const canReturnBalance = access.charge && access.refunds;
  const { data: account, error, isPending } = useCustomerAccount(of);
  const reminder = account ? describeReminder(account) : null;
  const pay = usePayCustomerAccount();
  const writeOff = useWriteOffCustomerAccount();
  const reverse = useReverseWriteOff();
  const deposit = useAddCustomerDeposit();
  const giveBack = useReturnCustomerBalance();
  const { confirm } = useConfirm();
  const queryClient = useQueryClient();
  const [mode, setMode] = useState<Mode>('account');
  // The account as it was when «Abonar» or «Condonar» opened: the panel works on it and sends what it owed. If the answer
  // to a payment or a write-off is lost although it went through, the account behind reads again, but a retry still says
  // what the panel saw, finds the account changed and is refused instead of charging or forgiving it again.
  const [opened, setOpened] = useState<CustomerAccount | null>(null);
  const paying = mode === 'paying';
  const busy = pay.isPending || writeOff.isPending || deposit.isPending || giveBack.isPending;
  const [payError, setPayError] = useState('');
  const [writeOffError, setWriteOffError] = useState('');
  const [balanceError, setBalanceError] = useState('');
  // Some payments went through and one did not: the panel closes, so pressing «Abonar» again never sends again the ones
  // already registered (a payment has no idempotency key), and the account shows what is still owed.
  const [partialNotice, setPartialNotice] = useState('');

  const open = (next: Mode) => {
    setOpened(account ?? null);
    setPartialNotice('');
    setPayError('');
    setWriteOffError('');
    setBalanceError('');
    setMode(next);
  };

  const submitPayment = async (charge: ChargeResult) => {
    if (!opened) return;
    setPayError('');
    // One payment per method: each one is split over the orders owed from the oldest. Each says what the account owed
    // as the panel saw it, so a payment sent twice finds it changed and is refused instead of registered again.
    const owedCents = toCents(opened.owed);
    let paidCents = 0;
    // The change left in their favor (Fase 26D) goes with the last payment, the cash one it came from: all it received,
    // the rest of it in their favor.
    const last = charge.payments[charge.payments.length - 1];
    const inFavorCents = charge.changeInFavor && last?.method === OrderPaymentMethod.Cash && last.cashReceived != null
      ? Math.max(0, toCents(last.cashReceived) - toCents(last.amount))
      : 0;
    try {
      for (const payment of charge.payments) {
        const keepsChange = inFavorCents > 0 && payment === last;
        const after = await pay.mutateAsync({
          customerId: opened.customerId ?? undefined,
          phone: opened.customerId ? undefined : opened.phone,
          amount: keepsChange ? payment.cashReceived! : payment.amount,
          method: payment.method,
          reference: payment.reference,
          evidenceUrl: payment.evidenceUrl,
          cashReceived: keepsChange ? undefined : payment.cashReceived,
          expectedOwed: (owedCents - paidCents) / 100,
          restInFavor: keepsChange,
          // What they had in their favor as the panel saw it: sent twice, the change stays in their favor once.
          expectedBalance: keepsChange ? opened.creditBalance : undefined,
        });
        paidCents += toCents(payment.amount);
        queryClient.setQueryData(creditKeys.account(of), after);
      }
      toast.success(inFavorCents > 0
        ? `Abono de ${formatMoney(paidCents / 100)} registrado; ${formatMoney(inFavorCents / 100)} quedaron a su favor.`
        : `Abono de ${formatMoney(paidCents / 100)} registrado.`);
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
    if (!opened) return;
    setWriteOffError('');
    try {
      // What the account owed as the panel saw it: a write-off sent twice finds it changed and is refused.
      const after = await writeOff.mutateAsync({
        customerId: opened.customerId ?? undefined,
        phone: opened.customerId ? undefined : opened.phone,
        orderId: choice.orderId ?? undefined,
        amount: choice.amount,
        reason: choice.reason,
        notes: choice.notes ?? undefined,
        expectedOwed: opened.owed,
      });
      // The account as the server left it shows at once, not the one before the write-off until it reads again.
      queryClient.setQueryData(creditKeys.account(of), after);
      toast.success(`Se condonaron ${formatMoney(choice.amount)}.`);
      setMode('account');
    } catch (err: unknown) {
      setWriteOffError(getApiErrorMessage(err, 'No se pudo condonar.'));
    }
  };

  // Leaving it, using it or giving it back (Fase 26D): each says what the panel saw, so one sent twice is refused.
  const submitBalance = async (kind: BalanceMovementKind, movement: BalanceMovement) => {
    if (!opened) return;
    setBalanceError('');
    const whose = { customerId: opened.customerId ?? undefined, phone: opened.customerId ? undefined : opened.phone };
    try {
      const after = kind === 'deposit'
        ? await deposit.mutateAsync({ ...whose, ...movement, expectedBalance: opened.creditBalance })
        : kind === 'return'
          ? await giveBack.mutateAsync({ ...whose, ...movement, expectedBalance: opened.creditBalance })
          : await pay.mutateAsync({
            ...whose,
            amount: movement.amount,
            method: OrderPaymentMethod.CustomerBalance,
            restInFavor: false,
            expectedOwed: opened.owed,
            expectedBalance: opened.creditBalance,
          });
      queryClient.setQueryData(creditKeys.account(of), after);
      toast.success(kind === 'deposit'
        ? `Quedaron ${formatMoney(movement.amount)} a su favor.`
        : kind === 'return'
          ? `Se le devolvieron ${formatMoney(movement.amount)}.`
          : `Se usaron ${formatMoney(movement.amount)} de su saldo a favor.`);
      setMode('account');
    } catch (err: unknown) {
      setBalanceError(getApiErrorMessage(err, 'No se pudo guardar.'));
    }
  };

  const undoWriteOff = async (record: CustomerAccountWriteOff) => {
    const accepted = await confirm({
      title: 'Deshacer lo condonado',
      message: `${record.orderNumber} vuelve a deber ${formatMoney(record.amount)}. Lo condonado queda registrado como deshecho.`,
      confirmLabel: 'Deshacer',
      cancelLabel: 'Volver',
      danger: true,
    });
    if (!accepted) return;
    try {
      const after = await reverse.mutateAsync(record.id);
      queryClient.setQueryData(creditKeys.account(of), after);
      toast.success(`${record.orderNumber} vuelve a deber ${formatMoney(record.amount)}.`);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo deshacer lo condonado.'));
    }
  };

  // What they have in their favor shows once there is any, or was (Fase 26D); else the three tiles of the fiado, as before.
  const showsBalance = !!account && (account.creditBalance > 0 || account.recentBalanceEntries.length > 0);
  const tileColumn = showsBalance ? 'col-6 col-md-3' : 'col-4';

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
              <div className={tileColumn}>
                <div className={`border rounded p-2 h-100 ${account.owed > 0 ? 'border-danger' : ''}`}>
                  <div className="small text-muted">Debe</div>
                  <div className={`fw-semibold ta-money ${account.owed > 0 ? 'text-danger' : ''}`}>{formatMoney(account.owed)}</div>
                </div>
              </div>
              <div className={tileColumn}>
                <div className="border rounded p-2 h-100">
                  <div className="small text-muted">Límite</div>
                  <div className="fw-semibold ta-money">{account.creditLimit != null ? formatMoney(account.creditLimit) : 'Sin fiado'}</div>
                </div>
              </div>
              <div className={tileColumn}>
                <div className="border rounded p-2 h-100">
                  <div className="small text-muted">Le quedan</div>
                  <div className="fw-semibold ta-money">{account.available != null ? formatMoney(account.available) : '—'}</div>
                </div>
              </div>
              {showsBalance && (
                <div className={tileColumn}>
                  <div className={`border rounded p-2 h-100 ${account.creditBalance > 0 ? 'border-success' : ''}`}>
                    <div className="small text-muted">A favor</div>
                    <div className={`fw-semibold ta-money ${account.creditBalance > 0 ? 'text-success' : ''}`}>{formatMoney(account.creditBalance)}</div>
                  </div>
                </div>
              )}
            </div>
            {account.creditTermDays != null && (
              <p className="small mb-3">
                <span className="text-muted">
                  Plazo de {describeTerm(account.creditTermDays)}{account.ownCreditTermDays != null ? ' (solo para este cliente)' : ''} ·{' '}
                </span>
                {account.overdue > 0
                  ? <strong className="text-danger">Vencido <span className="ta-money">{formatMoney(account.overdue)}</span></strong>
                  : <span className="text-muted">Nada vencido</span>}
                {reminder && <span className="d-block text-muted">{reminder}</span>}
              </p>
            )}
            {account.overLimit && (
              <p className="small text-danger mb-3">Debe más que su límite: no se le fía más hasta que abone.</p>
            )}
            {account.creditLimit != null && !account.sellsOnCredit && (
              <p className="small text-muted mb-3">Tiene límite, pero ahora no se le fía: el cliente está desactivado o no tiene teléfono.</p>
            )}

            {paying && opened ? (
              <ChargePanel
                amountDue={opened.owed}
                submitting={pay.isPending}
                allowPartial
                allowTip={false}
                allowChangeInFavor
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
            ) : (mode === 'deposit' || mode === 'use' || mode === 'return') && opened ? (
              <BalanceMovementPanel
                kind={mode}
                max={mode === 'use' ? Math.min(opened.owed, opened.creditBalance) : mode === 'return' ? opened.creditBalance : undefined}
                submitting={busy}
                error={balanceError}
                onSubmit={(movement) => void submitBalance(mode, movement)}
                onCancel={() => {
                  setMode('account');
                  setBalanceError('');
                }}
              />
            ) : mode === 'writingOff' && opened ? (
              <WriteOffPanel
                account={opened}
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

                {account.recentBalanceEntries.length > 0 && (
                  <>
                    <h3 className="h6 mt-3">Saldo a favor</h3>
                    <ul className="list-unstyled small mb-0">
                      {account.recentBalanceEntries.map((entry) => (
                        <li key={entry.id} className="mb-1">
                          <div className="d-flex justify-content-between gap-2">
                            <span>{formatDateTime(entry.occurredAt)} · {describeBalanceEntry(entry)}</span>
                            <span className={`ta-money ${entry.amount > 0 ? 'text-success' : ''}`}>
                              {entry.amount > 0 ? '+' : '−'}{formatMoney(Math.abs(entry.amount))}
                            </span>
                          </div>
                          {entry.notes && <div className="text-muted">{entry.notes}</div>}
                        </li>
                      ))}
                    </ul>
                  </>
                )}

                {account.recentWriteOffs.length > 0 && (
                  <>
                    <h3 className="h6 mt-3">Lo condonado</h3>
                    <ul className="list-unstyled small mb-0">
                      {account.recentWriteOffs.map((writeOffRecord) => (
                        <li key={writeOffRecord.id} className="mb-1">
                          <div className="d-flex justify-content-between gap-2">
                            <span>
                              {formatDateTime(writeOffRecord.writtenOffAt)} · {getWriteOffReasonLabel(writeOffRecord.reason)} · {writeOffRecord.orderNumber}
                            </span>
                            <span className={`ta-money ${writeOffRecord.reversedAt ? 'text-muted text-decoration-line-through' : ''}`}>
                              {formatMoney(writeOffRecord.amount)}
                            </span>
                          </div>
                          {writeOffRecord.notes && <div className="text-muted">{writeOffRecord.notes}</div>}
                          {writeOffRecord.reversedAt ? (
                            <div className="text-muted">Deshecho el {formatDateTime(writeOffRecord.reversedAt)}: lo vuelve a deber.</div>
                          ) : canWriteOff && (
                            <button
                              type="button"
                              className="btn btn-link btn-sm text-danger p-0"
                              aria-label={`Deshacer lo condonado de ${writeOffRecord.orderNumber}`}
                              onClick={() => void undoWriteOff(writeOffRecord)}
                              disabled={busy || reverse.isPending}
                            >
                              Deshacer
                            </button>
                          )}
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
              onClick={() => open('writingOff')}
            >
              Condonar
            </button>
          )}
          {canReturnBalance && account.creditBalance > 0 && (
            <button type="button" className="btn btn-light btn-touch" onClick={() => open('return')}>
              Devolver saldo
            </button>
          )}
          {canCharge && (
            <button type="button" className="btn btn-light btn-touch" onClick={() => open('deposit')}>
              <i className="ri-wallet-3-line me-1" aria-hidden="true"></i>Dejar a favor
            </button>
          )}
          {canCharge && account.owed > 0 && account.creditBalance > 0 && (
            <button type="button" className="btn btn-outline-primary btn-touch" onClick={() => open('use')}>
              Usar saldo a favor
            </button>
          )}
          {canCharge && account.owed > 0 && (
            <button
              type="button"
              className="btn btn-primary btn-touch"
              onClick={() => open('paying')}
            >
              <i className="ri-hand-coin-line me-1" aria-hidden="true"></i>Abonar
            </button>
          )}
        </Modal.Footer>
      )}
    </Modal>
  );
}
