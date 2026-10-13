import { useState } from 'react';
import { getApiErrorMessage } from '../../api/errors';
import { useVisitRoute, type VisitRouteStaff } from '../../api/homeVisits';
import { DataTableFilter } from '../../components/common/DataTable';
import type { EmployeeDto } from '../../types';
import { formatInBusinessZone, getBusinessToday } from '../../utils/dates';
import { MAX_ROUTE_STOPS, describeVisitFee, mapsPlaceUrl, mapsRouteUrl, telUrl } from '../../utils/homeVisits';
import { formatMoney } from '../../utils/money';
import { canBeCharged, statusBadge, statusLabel } from './appointmentStatus';

interface VisitRouteProps {
  /** The active team, to see the route of one person. */
  staff: EmployeeDto[];
  pricesIncludeTax: boolean;
  /** «Cobrar» opens Vender with the appointment: only for whoever sells. */
  canCharge: boolean;
  onCharge: (appointmentId: string) => void;
}

const time = (value: string) => formatInBusinessZone(value, { hour: '2-digit', minute: '2-digit' });

/**
 * Ruta del día: the visits at the customers' addresses of a day by team member, in the order of their time (the order is
 * the time: the person picks the way in the map). The address leaves the device only when the person opens a map link.
 */
export default function VisitRoute({ staff, pricesIncludeTax, canCharge, onCharge }: VisitRouteProps) {
  const [date, setDate] = useState(() => getBusinessToday());
  const [staffUserId, setStaffUserId] = useState('');
  const route = useVisitRoute(date, staffUserId || undefined);
  const people = route.data?.staff ?? [];

  return (
    <div>
      <div className="d-flex flex-wrap align-items-end gap-2 mb-3">
        <DataTableFilter label="Día" htmlFor="route-date">
          <input
            id="route-date"
            type="date"
            className="form-control form-control-sm"
            value={date}
            onChange={(e) => setDate(e.target.value)}
          />
        </DataTableFilter>
        <DataTableFilter label="Atiende" htmlFor="route-staff">
          <select id="route-staff" className="form-select form-select-sm" value={staffUserId} onChange={(e) => setStaffUserId(e.target.value)}>
            <option value="">Todo el equipo</option>
            {staff.map((member) => (
              <option key={member.userId} value={member.userId}>{member.firstName} {member.lastName}</option>
            ))}
          </select>
        </DataTableFilter>
      </div>

      {route.isPending && <p className="text-muted mb-0" role="status">Cargando la ruta…</p>}
      {route.isError && (
        <div className="alert alert-danger d-flex flex-wrap align-items-center justify-content-between gap-2 mb-0" role="alert">
          {getApiErrorMessage(route.error, 'No se pudo cargar la ruta.')}
          <button type="button" className="btn btn-sm btn-light" onClick={() => void route.refetch()}>Reintentar</button>
        </div>
      )}
      {route.isSuccess && people.length === 0 && (
        <p className="text-muted mb-0">No hay visitas a domicilio ese día.</p>
      )}
      {people.map((person) => (
        <PersonRoute
          key={person.staffUserId ?? person.staffName ?? 'nadie'}
          person={person}
          pricesIncludeTax={pricesIncludeTax}
          canCharge={canCharge}
          onCharge={onCharge}
        />
      ))}
    </div>
  );
}

function PersonRoute({ person, pricesIncludeTax, canCharge, onCharge }: { person: VisitRouteStaff } & Omit<VisitRouteProps, 'staff'>) {
  const name = person.staffName ?? 'Sin asignar';
  const routeUrl = mapsRouteUrl(person.stops);

  return (
    <section className="border rounded p-2 mb-3" aria-label={`Ruta de ${name}`}>
      <div className="d-flex flex-wrap align-items-center justify-content-between gap-2 mb-2">
        <div>
          <h3 className="h6 mb-0">{name}</h3>
          <div className="small text-muted">
            {person.stops.length} {person.stops.length === 1 ? 'visita' : 'visitas'}
            {person.feeTotal > 0 && <> · cargos por visita <span className="ta-money">{formatMoney(person.feeTotal)}</span></>}
          </div>
        </div>
        {routeUrl && (
          <a className="btn btn-sm btn-light" href={routeUrl} target="_blank" rel="noopener noreferrer" aria-label={`Ruta en Maps de ${name}`}>
            <i className="ri-route-line me-1" aria-hidden="true"></i>Ruta en Maps
          </a>
        )}
      </div>
      {person.stops.length > MAX_ROUTE_STOPS && (
        <p className="small text-muted mb-2">La ruta en Maps lleva las primeras {MAX_ROUTE_STOPS} visitas.</p>
      )}
      <ol className="list-unstyled mb-0">
        {person.stops.map((stop) => (
          <li key={stop.appointmentId} className="border-top pt-2 mt-2">
            <div className="d-flex flex-wrap align-items-center gap-2">
              <span className="fw-semibold">{time(stop.startAt)} a {time(stop.endAt)}</span>
              <span className={`badge ${statusBadge(stop.status)}`}>{statusLabel(stop.status)}</span>
              {stop.saleOrderId && <span className="small text-muted">Cobrada</span>}
            </div>
            <div>{stop.customerName} · {stop.serviceName}</div>
            <div className="small">{stop.addressLine}</div>
            {stop.references && <div className="small text-muted">Ref.: {stop.references}</div>}
            <div className="small text-muted">
              CP {stop.postalCode} · {stop.zoneName} · {describeVisitFee(stop.fee, pricesIncludeTax)}
            </div>
            <div className="d-flex flex-wrap gap-1 mt-1">
              <a
                className="btn btn-sm btn-light"
                href={mapsPlaceUrl(stop)}
                target="_blank"
                rel="noopener noreferrer"
                aria-label={`Abrir en Maps la visita de ${stop.customerName}`}
              >
                Abrir en Maps
              </a>
              <a className="btn btn-sm btn-light" href={telUrl(stop.customerPhone)} aria-label={`Llamar a ${stop.customerName}`}>
                Llamar
              </a>
              {canCharge && canBeCharged(stop) && (
                <button
                  type="button"
                  className="btn btn-sm btn-light"
                  onClick={() => onCharge(stop.appointmentId)}
                  aria-label={`Cobrar la cita de ${stop.customerName}`}
                >
                  Cobrar
                </button>
              )}
            </div>
          </li>
        ))}
      </ol>
    </section>
  );
}
