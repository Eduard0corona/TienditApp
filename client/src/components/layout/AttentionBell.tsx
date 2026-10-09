import { Link } from 'react-router-dom';
import { useAttention } from '../../api/queries';
import { useMySubscription } from '../../hooks/useMySubscription';
import { describeAttentionItems, describePlanNotice } from '../../utils/attention';

const BADGE_LIMIT = 9;

/**
 * The bell: what needs someone now, with a link to where it is handled. Nothing pending, no badge. A plan past due
 * goes first, for the owners and admins who read it.
 */
export default function AttentionBell() {
  const attention = useAttention();
  const plan = describePlanNotice(useMySubscription().data);
  const notices = [...(plan ? [plan] : []), ...describeAttentionItems(attention.data?.items ?? [])];
  const total = (attention.data?.items ?? []).reduce((sum, item) => sum + item.count, 0) + (plan ? 1 : 0);
  const urgent = notices.some((notice) => notice.tone === 'danger');
  const label = total === 0 ? 'Avisos: nada pendiente' : `Avisos: ${total} pendiente${total === 1 ? '' : 's'}`;

  return (
    <div className="dropdown">
      <button
        type="button"
        className="nav-link border-0 bg-transparent position-relative attention-bell"
        data-bs-toggle="dropdown"
        aria-expanded="false"
        aria-label={label}
        title={label}
      >
        <i className="ri-notification-3-line font-22" aria-hidden="true"></i>
        {total > 0 && (
          <span className={`attention-badge ${urgent ? 'attention-badge-urgent' : ''}`} aria-hidden="true">
            {total > BADGE_LIMIT ? `${BADGE_LIMIT}+` : total}
          </span>
        )}
      </button>
      <div className="dropdown-menu dropdown-menu-end attention-menu">
        <h6 className="dropdown-header">Requiere atención</h6>
        {attention.isLoading && <div className="dropdown-item-text text-muted small">Revisando…</div>}
        {attention.isError && <div className="dropdown-item-text text-muted small">No se pudieron revisar los avisos.</div>}
        {attention.isSuccess && notices.length === 0 && (
          <div className="dropdown-item-text text-muted small">
            <i className="ri-check-double-line me-1" aria-hidden="true"></i>Nada pendiente por ahora.
          </div>
        )}
        {notices.map((notice) => (
          <Link key={notice.key} to={notice.path} className={`dropdown-item attention-item attention-${notice.tone}`}>
            <i className={`${notice.icon} me-2`} aria-hidden="true"></i>
            <span>{notice.text}</span>
          </Link>
        ))}
      </div>
    </div>
  );
}
