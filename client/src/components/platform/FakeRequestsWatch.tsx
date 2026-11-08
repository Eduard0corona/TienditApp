import { useQuery } from '@tanstack/react-query';
import { getApiErrorMessage } from '../../api/errors';
import { platformApi } from '../../api/platformApi';
import { formatDate } from '../../utils/dates';

/**
 * Fase 30A (ADR 0081): what the platform watches of the requests its businesses mark as fake, in the last 90 days. The
 * businesses that mark the most, with their requests online to compare, and the phones two businesses or more marked,
 * said only by their last four digits.
 */
export default function FakeRequestsWatch() {
  const watch = useQuery({ queryKey: ['platform', 'fake-requests'], queryFn: platformApi.getFakeRequests });
  const data = watch.data;

  return (
    <section className="card mt-3" aria-labelledby="fake-requests-title">
      <div className="card-body">
        <h2 id="fake-requests-title" className="h5">Solicitudes marcadas como falsas</h2>
        <p className="text-muted small">
          De los últimos 90 días. Compara cuántas marca cada negocio con sus solicitudes en línea, y qué teléfonos marcaron dos
          negocios o más. Los teléfonos van con sus últimos cuatro dígitos.
        </p>
        {watch.isPending && <p className="text-muted small mb-0" role="status">Cargando…</p>}
        {watch.isError && (
          <div className="alert alert-danger" role="alert">{getApiErrorMessage(watch.error, 'No se pudieron cargar las solicitudes falsas.')}</div>
        )}
        {data && (
          <div className="row g-3">
            <div className="col-xl-7">
              <h3 className="h6">Negocios que más marcan</h3>
              {data.businesses.length === 0 ? (
                <p className="text-muted small mb-0">Ningún negocio marcó solicitudes como falsas.</p>
              ) : (
                <div className="table-responsive">
                  <table className="table table-sm align-middle mb-0">
                    <thead>
                      <tr>
                        <th scope="col">Negocio</th>
                        <th scope="col" className="text-end">Marcadas</th>
                        <th scope="col" className="text-end">Les quitó la marca</th>
                        <th scope="col" className="text-end">Solicitudes</th>
                        <th scope="col">Última</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.businesses.map((business) => (
                        <tr key={business.tenantId}>
                          <td>
                            <span className="fw-semibold">{business.name}</span>
                            <span className="d-block small text-muted">{business.slug}</span>
                          </td>
                          <td className="text-end">{business.marked}</td>
                          <td className="text-end">{business.takenBack}</td>
                          <td className="text-end">{business.requests}</td>
                          <td className="text-nowrap">{formatDate(business.lastMarkedAt)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
            <div className="col-xl-5">
              <h3 className="h6">Teléfonos que marcaron varios negocios</h3>
              {data.phones.length === 0 ? (
                <p className="text-muted small mb-0">Ningún teléfono lo marcaron dos negocios o más.</p>
              ) : (
                <ul className="list-unstyled mb-0">
                  {/* Two phones may end alike: each is its own row, in the server's order. */}
                  {data.phones.map((phone, index) => (
                    <li key={index} className="border-bottom py-2">
                      <span className="fw-semibold">
                        <span aria-hidden="true">•••• </span>
                        <span className="visually-hidden">Termina en </span>
                        {phone.ending}
                      </span>
                      <span className="small text-muted"> · {phone.businessNames.length} negocios · último {formatDate(phone.lastMarkedAt)}</span>
                      <span className="d-block small">{phone.businessNames.join(', ')}</span>
                    </li>
                  ))}
                </ul>
              )}
            </div>
            {(data.businesses.length === data.top || data.phones.length === data.top) && (
              <p className="col-12 small text-muted mb-0">Cada lista muestra los {data.top} primeros.</p>
            )}
          </div>
        )}
      </div>
    </section>
  );
}
