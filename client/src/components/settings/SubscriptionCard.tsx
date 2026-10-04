import { SUBSCRIPTION_STATUS_LABELS, SUBSCRIPTION_STATUS_TONES, PAYMENT_METHOD_LABELS } from '../../api/platformApi';
import { useMySubscription } from '../../hooks/useMySubscription';
import { formatDate } from '../../utils/dates';
import { formatMoney } from '../../utils/money';
import { moduleLabels } from '../../utils/modules';

/** The business's plan, until when it is covered, what it includes and its latest payments. */
export default function SubscriptionCard() {
  const subscription = useMySubscription();
  const data = subscription.data;
  if (!data) return null;

  if (!data.hasPlan || !data.plan) {
    return (
      <div className="card mb-3">
        <div className="card-body d-flex flex-wrap justify-content-between align-items-center gap-2">
          <div>
            <h2 className="h5 mb-1">Plan</h2>
            <p className="text-muted small mb-0">Tu negocio no tiene un plan asignado: puedes usar todos los módulos sin límites.</p>
          </div>
          <span className="small text-muted">{data.branchesUsed} sucursales · {data.usersUsed} usuarios</span>
        </div>
      </div>
    );
  }

  const plan = data.plan;
  const status = data.status ?? 0;
  return (
    <div className="card mb-3">
      <div className="card-body">
        <div className="d-flex flex-wrap justify-content-between align-items-start gap-2 mb-2">
          <div>
            <h2 className="h5 mb-1">Plan {plan.name}</h2>
            <div className="small text-muted">{formatMoney(plan.monthlyPrice)} al mes</div>
          </div>
          <span className={`badge text-bg-${SUBSCRIPTION_STATUS_TONES[status]}`}>{SUBSCRIPTION_STATUS_LABELS[status]}</span>
        </div>
        <div className="row g-2 small mb-2">
          <div className="col-sm-4">
            <div className="text-muted">{data.paidThrough ? 'Pagado hasta' : 'Prueba gratis hasta'}</div>
            <div className="fw-semibold">{formatDate(data.paidThrough ?? data.trialEndsAt)}</div>
          </div>
          <div className="col-sm-4">
            <div className="text-muted">Sucursales activas</div>
            <div className="fw-semibold">{data.branchesUsed} de {plan.maxBranches}</div>
          </div>
          <div className="col-sm-4">
            <div className="text-muted">Usuarios activos</div>
            <div className="fw-semibold">{data.usersUsed} de {plan.maxUsers}</div>
          </div>
        </div>
        {status === 2 && (
          <div className="alert alert-warning small py-2 mb-2">
            Tu pago está vencido. El servicio se suspende el {formatDate(data.suspendsAt)} si no se registra un pago.
          </div>
        )}
        {data.statusReason && status >= 3 && <div className="alert alert-danger small py-2 mb-2">{data.statusReason}</div>}
        <div className="d-flex flex-wrap gap-1 mb-2">
          {moduleLabels(plan.modules).map((label) => <span key={label} className="badge text-bg-light border">{label}</span>)}
        </div>
        {data.payments.length > 0 && (
          <details className="small">
            <summary>Últimos pagos</summary>
            <ul className="list-unstyled mb-0 mt-1">
              {data.payments.map((payment) => (
                <li key={payment.id}>
                  {formatDate(payment.paidAt)} · {formatMoney(payment.amount)} · {PAYMENT_METHOD_LABELS[payment.method]} · cubre hasta {formatDate(payment.periodEnd)}
                </li>
              ))}
            </ul>
          </details>
        )}
        <p className="text-muted small mb-0 mt-2">Para cambiar de plan o registrar un pago, contacta a soporte de TienditApp.</p>
      </div>
    </div>
  );
}
