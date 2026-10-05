import { Link } from 'react-router-dom';
import { useCurrentCashSession } from '../../api/queries';
import { formatMoney } from '../../utils/money';

/** Whether the person's drawer is open and what it should hold, always in sight; it leads to Caja. */
export default function CashStatusChip() {
  const session = useCurrentCashSession();
  if (session.isLoading || session.isError) return null;

  const open = session.data;
  return (
    <Link
      to="/cash"
      className={`btn btn-sm cash-status-chip ${open ? 'cash-status-open' : 'cash-status-closed'}`}
      title={open ? `Tu caja está abierta y debe tener ${formatMoney(open.expectedBalance)} en efectivo` : 'Tu caja está cerrada: ábrela en Caja'}
    >
      <i className={`${open ? 'ri-safe-2-line' : 'ri-lock-line'} me-1`} aria-hidden="true"></i>
      {open ? <>Caja <span className="ta-money">{formatMoney(open.expectedBalance)}</span></> : 'Caja cerrada'}
    </Link>
  );
}
