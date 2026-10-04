import { Link } from 'react-router-dom';
import { SubscriptionStatusValue } from '../../api/platformApi';
import { useMySubscription } from '../../hooks/useMySubscription';
import { formatDate } from '../../utils/dates';

/** Warns owners and admins that the plan is past due and when the business would be suspended. */
export default function PastDueBanner() {
  const subscription = useMySubscription();
  const data = subscription.data;
  if (!data?.hasPlan || data.status !== SubscriptionStatusValue.PastDue) return null;

  return (
    <div className="alert alert-warning d-flex flex-wrap align-items-center justify-content-between gap-2 mt-3 mb-0" role="status">
      <span>
        <i className="ri-error-warning-line me-1" aria-hidden="true"></i>
        El pago de tu plan está vencido. El servicio se suspende el {formatDate(data.suspendsAt)} si no se registra un pago.
      </span>
      <Link to="/settings/business" className="btn btn-sm btn-warning">Ver mi plan</Link>
    </div>
  );
}
