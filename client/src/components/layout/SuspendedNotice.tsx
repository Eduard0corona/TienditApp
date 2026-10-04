import { useMySubscription } from '../../hooks/useMySubscription';
import { formatDate } from '../../utils/dates';

/**
 * Shown instead of the screens while the business is suspended or cancelled: the API answers nothing else until
 * the platform reactivates it. Owners and admins see why and since when.
 */
export default function SuspendedNotice({ businessName }: { businessName?: string }) {
  const subscription = useMySubscription();
  const data = subscription.data;
  return (
    <div className="row justify-content-center py-5">
      <div className="col-lg-7">
        <div className="card border-danger">
          <div className="card-body text-center py-4">
            <i className="ri-lock-line display-5 text-danger" aria-hidden="true"></i>
            <h1 className="h4 mt-2">{businessName ? `${businessName} está suspendido` : 'Este negocio está suspendido'}</h1>
            <p className="text-muted mb-2">
              Mientras esté suspendido nadie del equipo puede vender ni consultar la información. Tus datos se conservan.
            </p>
            {data?.statusReason && <p className="mb-2"><strong>Motivo:</strong> {data.statusReason}</p>}
            {data?.paidThrough && <p className="small text-muted mb-2">Último periodo pagado: hasta el {formatDate(data.paidThrough)}.</p>}
            <p className="mb-0">Para reactivarlo, contacta a soporte de TienditApp.</p>
          </div>
        </div>
      </div>
    </div>
  );
}
