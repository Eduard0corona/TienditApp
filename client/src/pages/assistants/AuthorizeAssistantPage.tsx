import { useMemo } from 'react';
import { Link, useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { useMutation, useQuery } from '@tanstack/react-query';
import { assistantsApi, authorizationRequestOf } from '../../api/assistantsApi';
import { getApiErrorMessage } from '../../api/errors';
import BrandLogo from '../../components/common/BrandLogo';
import { useAuth } from '../../hooks/useAuth';
import { useDocumentTitle } from '../../hooks/useDocumentTitle';
import { getAccess } from '../../utils/access';
import { returnToAssistant } from '../../utils/externalNavigation';

const CAN = [
  'Ver tus direcciones guardadas y su código postal.',
  'Ver tus pedidos y cómo van.',
  'Ver tus negocios favoritos.',
  'Prepararte pedidos que tú revisas y envías desde la página del negocio.',
];

/**
 * The permission an AI assistant asks to use the customer's account (Fase 32E, ADR 0097), at /autorizar, where
 * /oauth/authorize sends the browser. It shows who asks, what it will see and the site it returns to, and the customer
 * allows it or not; the API checks the request again either way. Only a customer account connects an assistant.
 */
export default function AuthorizeAssistantPage() {
  useDocumentTitle('Conectar un asistente · TienditApp');
  const [search] = useSearchParams();
  const location = useLocation();
  const navigate = useNavigate();
  const { user, logout } = useAuth();
  const request = useMemo(() => authorizationRequestOf(search), [search]);
  const check = useQuery({
    queryKey: ['assistant-authorization', search.toString()],
    queryFn: ({ signal }) => assistantsApi.check(request, signal),
    retry: false,
  });
  const answer = useMutation({
    mutationFn: (allow: boolean) => assistantsApi.answer(request, allow),
    onSuccess: ({ redirectUrl }) => returnToAssistant(redirectUrl),
  });
  const customer = getAccess(user, null).myOrders;
  const name = check.data?.clientName ?? 'Un asistente';

  const switchAccount = async () => {
    await logout();
    navigate('/login', { replace: true, state: { from: location } });
  };

  let body: React.ReactNode;
  if (check.isLoading) {
    body = <p className="text-muted mb-0" aria-busy="true">Revisando la solicitud del asistente…</p>;
  } else if (check.isError) {
    body = (
      <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2 mb-0" role="alert">
        <span className="me-auto">{getApiErrorMessage(check.error, 'No se pudo revisar la solicitud del asistente.')}</span>
        <button type="button" className="btn btn-sm btn-light" onClick={() => void check.refetch()}>Reintentar</button>
      </div>
    );
  } else if (check.data && !check.data.valid) {
    body = (
      <>
        <h1 className="h5">No se puede conectar este asistente</h1>
        <p className="mb-3">{check.data.problem ?? 'La solicitud del asistente no es válida. Vuelve a intentarlo desde tu asistente.'}</p>
        {check.data.errorRedirectUrl ? (
          <button type="button" className="btn btn-light" onClick={() => returnToAssistant(check.data.errorRedirectUrl!)}>
            Volver al asistente
          </button>
        ) : (
          <Link to="/asistentes">Cómo conectar tu asistente</Link>
        )}
      </>
    );
  } else if (!customer) {
    body = (
      <>
        <h1 className="h5">Inicia sesión con tu cuenta de cliente</h1>
        <p className="mb-3">
          Solo una cuenta de cliente conecta un asistente: la de un negocio tiene permisos que un asistente no debe tener.
        </p>
        <button type="button" className="btn btn-primary" onClick={() => void switchAccount()}>Cambiar de cuenta</button>
      </>
    );
  } else if (check.data) {
    body = (
      <>
        <h1 className="h5">{name} quiere usar tu cuenta de TienditApp</h1>
        <p className="small text-muted">Con tu cuenta {user?.email}. Podrá:</p>
        <ul className="small">
          {CAN.map((item) => <li key={item}>{item}</li>)}
        </ul>
        <p className="small">No podrá enviar pedidos, pagar por ti ni cambiar tu cuenta.</p>
        <p className="small mb-1">
          Al permitirlo regresas a <strong>{check.data.redirectSite}</strong>.
        </p>
        {check.data.redirectsToThisDevice && (
          <div className="alert alert-warning small" role="note">
            Regresa a una aplicación de esta computadora. Permítelo solo si tú acabas de conectar el asistente.
          </div>
        )}
        {answer.isError && (
          <div className="alert alert-danger small" role="alert">
            {getApiErrorMessage(answer.error, 'No se pudo responder al asistente.')}
          </div>
        )}
        <div className="d-flex flex-wrap gap-2 mt-3">
          <button type="button" className="btn btn-primary" disabled={answer.isPending} onClick={() => answer.mutate(true)}>
            Permitir
          </button>
          <button type="button" className="btn btn-light" disabled={answer.isPending} onClick={() => answer.mutate(false)}>
            No permitir
          </button>
        </div>
        <p className="small text-muted mt-3 mb-0">
          Lo desconectas cuando quieras en <Link to="/cuenta">Mi cuenta</Link>.
        </p>
      </>
    );
  }

  return (
    <div className="pt-2 pt-sm-5 pb-4 pb-sm-5">
      <div className="container">
        <div className="row justify-content-center">
          <div className="col-xxl-4 col-lg-5 col-md-7">
            <div className="card">
              <div className="card-header py-4 text-center bg-primary">
                <Link to="/explorar" aria-label="TienditApp">
                  <BrandLogo tone="light" />
                </Link>
              </div>
              <div className="card-body p-4">{body}</div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
