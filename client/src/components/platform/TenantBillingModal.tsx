import { useState } from 'react';
import { Modal } from 'react-bootstrap';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { getApiErrorMessage } from '../../api/errors';
import {
  PAYMENT_METHOD_LABELS,
  platformApi,
  SUBSCRIPTION_STATUS_LABELS,
  SUBSCRIPTION_STATUS_TONES,
  SubscriptionStatusValue,
  type Plan,
  type PlatformTenant,
  type SubscriptionPaymentMethod,
} from '../../api/platformApi';
import { useConfirm } from '../../hooks/useConfirm';
import { useToast } from '../../hooks/useToast';
import { formatDate } from '../../utils/dates';
import { formatMoney } from '../../utils/money';

interface TenantBillingModalProps {
  tenant: PlatformTenant;
  plans: Plan[];
  onClose: () => void;
}

/** Billing of one business: its plan, payments, trial and access, with every action audited by the API. */
export default function TenantBillingModal({ tenant, plans, onClose }: TenantBillingModalProps) {
  const toast = useToast();
  const { confirm } = useConfirm();
  const queryClient = useQueryClient();
  const detail = useQuery({
    queryKey: ['platform', 'tenant', tenant.tenantId],
    queryFn: () => platformApi.getTenantSubscription(tenant.tenantId),
  });
  const [planId, setPlanId] = useState(tenant.planId ?? '');
  const [trialDays, setTrialDays] = useState('');
  const [months, setMonths] = useState('1');
  const [amount, setAmount] = useState('');
  const [method, setMethod] = useState<SubscriptionPaymentMethod>(0);
  const [reference, setReference] = useState('');
  const [extraDays, setExtraDays] = useState('7');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);

  const data = detail.data;
  const plan = data?.plan;
  const selectedPlan = plans.find((item) => item.id === planId);
  const monthsNumber = Math.max(1, Number(months) || 1);
  const suggestedAmount = plan ? plan.monthlyPrice * monthsNumber : 0;

  const run = async (action: () => Promise<unknown>, done: string) => {
    setBusy(true);
    try {
      await action();
      toast.success(done);
      await queryClient.invalidateQueries({ queryKey: ['platform'] });
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo completar la acción.'));
    } finally {
      setBusy(false);
    }
  };

  const assign = () => run(
    () => platformApi.assignPlan(tenant.tenantId, planId, !data?.hasPlan && trialDays !== '' ? Number(trialDays) : undefined),
    data?.hasPlan ? 'Plan cambiado.' : 'Plan asignado.',
  );

  const pay = () => run(async () => {
    await platformApi.registerPayment(tenant.tenantId, {
      months: monthsNumber,
      amount: amount.trim() === '' ? null : Number(amount),
      method,
      reference: reference.trim() || null,
    });
    setAmount('');
    setReference('');
  }, 'Pago registrado.');

  const suspendOrReactivate = async () => {
    if (data?.suspendedByPlatform || tenant.tenantStatus !== 0) {
      await run(() => platformApi.reactivate(tenant.tenantId), 'Suspensión quitada.');
      return;
    }
    if (!reason.trim()) {
      toast.error('Escribe el motivo de la suspensión.');
      return;
    }
    const accepted = await confirm({
      title: `Suspender ${tenant.name}`,
      message: 'Nadie del negocio podrá usar la aplicación hasta que quites la suspensión.',
      confirmLabel: 'Suspender',
    });
    if (accepted) await run(() => platformApi.suspend(tenant.tenantId, reason.trim()), 'Negocio suspendido.');
  };

  const cancelSubscription = async () => {
    if (!reason.trim()) {
      toast.error('Escribe el motivo de la cancelación.');
      return;
    }
    const accepted = await confirm({
      title: `Cancelar el plan de ${tenant.name}`,
      message: 'El negocio pierde el acceso. Sus datos se conservan y puedes reactivarlo después.',
      confirmLabel: 'Cancelar plan',
    });
    if (accepted) await run(() => platformApi.cancel(tenant.tenantId, reason.trim()), 'Plan cancelado.');
  };

  return (
    <Modal show onHide={onClose} size="lg" centered scrollable>
      <Modal.Header closeButton>
        <Modal.Title>{tenant.name}</Modal.Title>
      </Modal.Header>
      <Modal.Body>
        {detail.isLoading && <div className="text-center py-4"><div className="spinner-border text-primary" role="status"></div></div>}
        {detail.isError && <div className="alert alert-danger">{getApiErrorMessage(detail.error, 'No se pudo cargar la cobranza del negocio.')}</div>}

        {data && (
          <>
            <div className="d-flex flex-wrap align-items-center gap-2 mb-3">
              {data.hasPlan && data.status !== null && data.status !== undefined ? (
                <span className={`badge text-bg-${SUBSCRIPTION_STATUS_TONES[data.status]}`}>{SUBSCRIPTION_STATUS_LABELS[data.status]}</span>
              ) : (
                <span className="badge text-bg-light border">Sin plan: sin límites</span>
              )}
              {plan && <span className="fw-semibold">{plan.name} · {formatMoney(plan.monthlyPrice)} al mes</span>}
              <span className="text-muted small ms-auto">
                {data.branchesUsed}{plan ? `/${plan.maxBranches}` : ''} sucursales · {data.usersUsed}{plan ? `/${plan.maxUsers}` : ''} usuarios
              </span>
            </div>
            {data.hasPlan && (
              <dl className="row small mb-3">
                <dt className="col-sm-4">Prueba hasta</dt><dd className="col-sm-8">{formatDate(data.trialEndsAt)}</dd>
                <dt className="col-sm-4">Pagado hasta</dt><dd className="col-sm-8">{data.paidThrough ? formatDate(data.paidThrough) : 'Sin pagos'}</dd>
                {data.status !== SubscriptionStatusValue.Cancelled && (
                  <><dt className="col-sm-4">Se suspende el</dt><dd className="col-sm-8">{formatDate(data.suspendsAt)} si no paga</dd></>
                )}
                {data.statusReason && (<><dt className="col-sm-4">Motivo</dt><dd className="col-sm-8">{data.statusReason}</dd></>)}
              </dl>
            )}

            <section className="border rounded p-2 mb-3" aria-labelledby="billing-plan">
              <h3 id="billing-plan" className="h6">Plan</h3>
              <div className="row g-2 align-items-end">
                <div className="col-md-6">
                  <label className="form-label small mb-1" htmlFor="billing-plan-select">Plan</label>
                  <select id="billing-plan-select" className="form-select form-select-sm" value={planId} onChange={(event) => setPlanId(event.target.value)}>
                    <option value="">Elige un plan</option>
                    {plans.filter((item) => item.isActive || item.id === tenant.planId).map((item) => (
                      <option key={item.id} value={item.id}>{item.name} · {formatMoney(item.monthlyPrice)}</option>
                    ))}
                  </select>
                </div>
                {!data.hasPlan && (
                  <div className="col-6 col-md-3">
                    <label className="form-label small mb-1" htmlFor="billing-trial">Prueba (días)</label>
                    <input id="billing-trial" type="number" min={0} max={90} className="form-control form-control-sm"
                      placeholder={selectedPlan ? String(selectedPlan.trialDays) : ''} value={trialDays}
                      onChange={(event) => setTrialDays(event.target.value)} />
                  </div>
                )}
                <div className="col-6 col-md-3">
                  <button type="button" className="btn btn-sm btn-primary w-100" disabled={busy || !planId || planId === tenant.planId}
                    onClick={() => void assign()}>
                    {data.hasPlan ? 'Cambiar plan' : 'Asignar plan'}
                  </button>
                </div>
              </div>
              <p className="small text-muted mb-0 mt-2">El negocio conserva solo los módulos que incluye el plan.</p>
            </section>

            {data.hasPlan && data.status !== SubscriptionStatusValue.Cancelled && (
              <section className="border rounded p-2 mb-3" aria-labelledby="billing-payment">
                <h3 id="billing-payment" className="h6">Registrar pago</h3>
                <div className="row g-2 align-items-end">
                  <div className="col-4 col-md-2">
                    <label className="form-label small mb-1" htmlFor="billing-months">Meses</label>
                    <input id="billing-months" type="number" min={1} max={24} className="form-control form-control-sm" value={months}
                      onChange={(event) => setMonths(event.target.value)} />
                  </div>
                  <div className="col-8 col-md-3">
                    <label className="form-label small mb-1" htmlFor="billing-amount">Monto</label>
                    <input id="billing-amount" type="number" min={0} step="0.01" className="form-control form-control-sm ta-money"
                      placeholder={suggestedAmount.toFixed(2)} value={amount} onChange={(event) => setAmount(event.target.value)} />
                  </div>
                  <div className="col-6 col-md-3">
                    <label className="form-label small mb-1" htmlFor="billing-method">Método</label>
                    <select id="billing-method" className="form-select form-select-sm" value={method}
                      onChange={(event) => setMethod(Number(event.target.value) as SubscriptionPaymentMethod)}>
                      {Object.entries(PAYMENT_METHOD_LABELS).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
                    </select>
                  </div>
                  <div className="col-6 col-md-4">
                    <label className="form-label small mb-1" htmlFor="billing-reference">Referencia</label>
                    <input id="billing-reference" className="form-control form-control-sm" maxLength={120} value={reference}
                      onChange={(event) => setReference(event.target.value)} />
                  </div>
                </div>
                <div className="d-flex flex-wrap justify-content-between align-items-center gap-2 mt-2">
                  <span className="small text-muted">El periodo pagado sigue donde termina la cobertura actual.</span>
                  <button type="button" className="btn btn-sm btn-success" disabled={busy} onClick={() => void pay()}>
                    Registrar {formatMoney(amount.trim() === '' ? suggestedAmount : Number(amount) || 0)}
                  </button>
                </div>
              </section>
            )}

            {data.hasPlan && data.status !== SubscriptionStatusValue.Cancelled && (
              <section className="border rounded p-2 mb-3" aria-labelledby="billing-trial-extend">
                <h3 id="billing-trial-extend" className="h6">Extender prueba</h3>
                <div className="d-flex flex-wrap gap-2 align-items-end">
                  <div>
                    <label className="form-label small mb-1" htmlFor="billing-extra-days">Días</label>
                    <input id="billing-extra-days" type="number" min={1} max={90} className="form-control form-control-sm" value={extraDays}
                      onChange={(event) => setExtraDays(event.target.value)} />
                  </div>
                  <button type="button" className="btn btn-sm btn-light" disabled={busy}
                    onClick={() => void run(() => platformApi.extendTrial(tenant.tenantId, Number(extraDays) || 1), 'Prueba extendida.')}>
                    Extender
                  </button>
                </div>
              </section>
            )}

            <section className="border border-danger-subtle rounded p-2 mb-3" aria-labelledby="billing-access">
              <h3 id="billing-access" className="h6">Acceso</h3>
              <label className="form-label small mb-1" htmlFor="billing-reason">Motivo (para suspender o cancelar)</label>
              <input id="billing-reason" className="form-control form-control-sm mb-2" maxLength={500} value={reason}
                onChange={(event) => setReason(event.target.value)} />
              <div className="d-flex flex-wrap gap-2">
                <button type="button" className="btn btn-sm btn-outline-danger" disabled={busy} onClick={() => void suspendOrReactivate()}>
                  {data.suspendedByPlatform || tenant.tenantStatus !== 0 ? 'Quitar suspensión' : 'Suspender'}
                </button>
                {data.hasPlan && data.status !== SubscriptionStatusValue.Cancelled && (
                  <button type="button" className="btn btn-sm btn-outline-secondary" disabled={busy} onClick={() => void cancelSubscription()}>
                    Cancelar plan
                  </button>
                )}
              </div>
            </section>

            <h3 className="h6">Pagos</h3>
            {data.payments.length === 0 ? (
              <p className="text-muted small mb-0">Sin pagos registrados.</p>
            ) : (
              <div className="table-responsive">
                <table className="table table-sm mb-0">
                  <thead>
                    <tr><th>Fecha</th><th>Periodo</th><th>Método</th><th>Referencia</th><th className="text-end">Monto</th></tr>
                  </thead>
                  <tbody>
                    {data.payments.map((payment) => (
                      <tr key={payment.id}>
                        <td>{formatDate(payment.paidAt)}</td>
                        <td className="small">{formatDate(payment.periodStart)} – {formatDate(payment.periodEnd)}</td>
                        <td>{PAYMENT_METHOD_LABELS[payment.method]}</td>
                        <td className="small">{payment.reference ?? '—'}</td>
                        <td className="text-end ta-money">{formatMoney(payment.amount)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </>
        )}
      </Modal.Body>
      <Modal.Footer>
        <button type="button" className="btn btn-light" onClick={onClose}>Cerrar</button>
      </Modal.Footer>
    </Modal>
  );
}
