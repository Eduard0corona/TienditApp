import { useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import PageTitle from '../../components/common/PageTitle';
import { getApiErrorMessage } from '../../api/errors';
import {
  useCompleteOnlinePaymentConnection,
  useDisconnectOnlinePayments,
  useOnlinePaymentSettings,
  useSetOnlinePaymentsAccepted,
  useStartOnlinePaymentConnection,
} from '../../api/onlinePayments';
import { useConfirm } from '../../hooks/useConfirm';
import { useToast } from '../../hooks/useToast';
import { formatDate, formatDateTime } from '../../utils/dates';
import { goToExternalPage } from '../../utils/externalNavigation';

/**
 * Configuración › Pagos en línea (Fase 24I, ADR 0031), for owners and admins: the business connects its own Mercado Pago
 * account, where its customers pay the orders of the online menu, and turns paying online on. Mercado Pago sends the owner
 * back here with ?code=…&state=…, and this page finishes the connection with the owner's session.
 */
export default function OnlinePayments() {
  const settings = useOnlinePaymentSettings();
  const start = useStartOnlinePaymentConnection();
  const complete = useCompleteOnlinePaymentConnection();
  const accept = useSetOnlinePaymentsAccepted();
  const disconnect = useDisconnectOnlinePayments();
  const { confirm } = useConfirm();
  const toast = useToast();
  const [searchParams, setSearchParams] = useSearchParams();
  // Back from Mercado Pago: what it sent, read once; the code is used once and the address forgets it right away.
  const [returned] = useState(() => ({
    code: searchParams.get('code'),
    state: searchParams.get('state'),
    denied: searchParams.has('error'),
  }));
  const finishing = useRef(false);
  useEffect(() => {
    if (finishing.current || (!returned.code && !returned.denied)) return;
    finishing.current = true;
    setSearchParams({}, { replace: true });
    if (returned.denied || !returned.code || !returned.state) return;
    complete.mutate({ state: returned.state, code: returned.code }, {
      onSuccess: () => toast.success('Cuenta de Mercado Pago conectada.'),
    });
  }, [returned, complete, setSearchParams, toast]);
  const returnError = returned.denied || (returned.code && !returned.state)
    ? 'No se conectó la cuenta: la autorización en Mercado Pago no se completó.'
    : complete.isError ? getApiErrorMessage(complete.error, 'No se pudo conectar la cuenta de Mercado Pago.') : '';

  const connect = async () => {
    try {
      const started = await start.mutateAsync();
      goToExternalPage(started.authorizationUrl);
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo abrir Mercado Pago.'));
    }
  };

  const toggle = async (accepts: boolean) => {
    try {
      await accept.mutateAsync(accepts);
      toast.success(accepts ? 'Tu menú ya ofrece pagar en línea.' : 'Tu menú ya no ofrece pagar en línea.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo cambiar el pago en línea.'));
    }
  };

  const remove = async () => {
    const accepted = await confirm({
      title: 'Desconectar Mercado Pago',
      message: 'TienditApp borra las llaves de tu cuenta y ya no puede cobrar en ella. Lo que ya cobraste sigue en tu cuenta de Mercado Pago.',
      confirmLabel: 'Desconectar',
      danger: true,
    });
    if (!accepted) return;
    try {
      await disconnect.mutateAsync();
      toast.success('Cuenta de Mercado Pago desconectada.');
    } catch (err: unknown) {
      toast.error(getApiErrorMessage(err, 'No se pudo desconectar la cuenta.'));
    }
  };

  const data = settings.data;
  const inCourse = (data?.checkoutsInCourse ?? 0) + (data?.refundsInCourse ?? 0);

  return (
    <>
      <PageTitle title="Pagos en línea" />

      {returnError && <div className="alert alert-danger" role="alert">{returnError}</div>}
      {complete.isPending && <div className="alert alert-info" role="status">Conectando tu cuenta de Mercado Pago…</div>}

      {settings.isPending ? (
        <p className="text-muted" aria-busy="true">Cargando…</p>
      ) : settings.isError || !data ? (
        <div className="alert alert-danger" role="alert">{getApiErrorMessage(settings.error, 'No se pudo cargar el pago en línea.')}</div>
      ) : !data.available ? (
        <div className="card">
          <div className="card-body">
            <h2 className="h5">El pago en línea no está disponible por ahora</h2>
            <p className="text-muted mb-0">Cuando TienditApp lo active, aquí conectas tu cuenta de Mercado Pago.</p>
          </div>
        </div>
      ) : (
        <div className="row g-3">
          <div className="col-lg-7">
            <div className="card h-100 mb-0">
              <div className="card-body">
                <div className="d-flex align-items-center gap-2 mb-2">
                  <h2 className="h5 mb-0">Mercado Pago</h2>
                  {data.sandbox && <span className="badge text-bg-warning">Modo de pruebas</span>}
                  {data.connected && !data.needsReconnect && <span className="badge text-bg-success">Conectada</span>}
                </div>

                {!data.connected ? (
                  <>
                    <p>
                      Conecta la cuenta de Mercado Pago de tu negocio para que tus clientes paguen sus pedidos del menú en línea al
                      hacerlos. El dinero llega directo a tu cuenta.
                    </p>
                    <button type="button" className="btn btn-primary" onClick={() => void connect()} disabled={start.isPending || complete.isPending}>
                      {start.isPending ? 'Abriendo Mercado Pago…' : 'Conectar Mercado Pago'}
                    </button>
                  </>
                ) : (
                  <>
                    <dl className="row small mb-3">
                      <dt className="col-sm-5">Cuenta</dt>
                      <dd className="col-sm-7 ta-money">{data.accountId}</dd>
                      <dt className="col-sm-5">Conectada el</dt>
                      <dd className="col-sm-7">{formatDate(data.connectedAt)}</dd>
                      <dt className="col-sm-5">Revisada hasta</dt>
                      <dd className="col-sm-7">{data.paymentsReviewedUntil ? formatDateTime(data.paymentsReviewedUntil) : 'Todavía no'}</dd>
                    </dl>
                    {data.needsReconnect && (
                      <div className="alert alert-warning">
                        La cuenta de Mercado Pago ya no responde: vuelve a conectarla para seguir recibiendo pagos en línea.
                        <div className="mt-2">
                          <button type="button" className="btn btn-sm btn-warning" onClick={() => void connect()} disabled={start.isPending || complete.isPending}>
                            Volver a conectar
                          </button>
                        </div>
                      </div>
                    )}
                    <div className="form-check form-switch mb-2">
                      <input
                        id="online-payments-accepts"
                        type="checkbox"
                        role="switch"
                        className="form-check-input"
                        checked={data.acceptsPayments}
                        disabled={accept.isPending || (data.needsReconnect && !data.acceptsPayments)}
                        onChange={(event) => void toggle(event.target.checked)}
                      />
                      <label className="form-check-label" htmlFor="online-payments-accepts">Ofrecer pagar en línea en mi menú</label>
                    </div>
                    {!data.menuTakesOrders && (
                      <p className="small text-muted">
                        Tu menú en línea no está recibiendo pedidos: el pago en línea aparece cuando lo actives en{' '}
                        <Link to="/settings/business">Configuración › Negocio</Link>.
                      </p>
                    )}
                    {data.refundsRefused > 0 && (
                      <div className="alert alert-danger small">
                        {data.refundsRefused === 1
                          ? 'Mercado Pago no hizo una devolución: devuélvela desde tu cuenta de Mercado Pago. La ves en el pedido.'
                          : `Mercado Pago no hizo ${data.refundsRefused} devoluciones: devuélvelas desde tu cuenta de Mercado Pago. Las ves en cada pedido.`}
                      </div>
                    )}
                    {data.disputesOpen > 0 && (
                      <div className="alert alert-warning small">
                        {data.disputesOpen === 1
                          ? 'Un pago está en disputa: Mercado Pago retiene el dinero mientras se resuelve. Atiéndela en tu cuenta de Mercado Pago, que te dice de qué pago es.'
                          : `${data.disputesOpen} pagos están en disputa: Mercado Pago retiene el dinero mientras se resuelven. Atiéndelas en tu cuenta de Mercado Pago, que te dice de qué pago es cada una.`}
                      </div>
                    )}
                    {inCourse > 0 && (
                      <p className="small text-muted">
                        Hay {inCourse === 1 ? 'un pago o devolución en curso' : `${inCourse} pagos o devoluciones en curso`}: para
                        cambiar o desconectar la cuenta, espera a que terminen (unos 45 minutos).
                      </p>
                    )}
                    {data.acceptsPayments && (
                      <p className="small text-muted">Para desconectar o cambiar la cuenta, primero apaga el pago en línea.</p>
                    )}
                    <button
                      type="button"
                      className="btn btn-outline-danger btn-sm"
                      onClick={() => void remove()}
                      disabled={disconnect.isPending || data.acceptsPayments || inCourse > 0}
                    >
                      Desconectar
                    </button>
                  </>
                )}
              </div>
            </div>
          </div>
          <div className="col-lg-5">
            <div className="card h-100 mb-0">
              <div className="card-body small">
                <h2 className="h6">Cómo funciona</h2>
                <ul className="mb-0 ps-3">
                  <li>El cliente elige pagar en línea al pedir para recoger o a domicilio, y paga en Mercado Pago con tarjeta o su saldo.</li>
                  <li>El pedido te llega cuando Mercado Pago aprueba el pago, ya pagado. Si ya no puedes tomarlo, el pago se le devuelve solo.</li>
                  <li>El dinero llega a tu cuenta de Mercado Pago, que cobra su comisión. TienditApp no cobra nada extra ni ve las tarjetas.</li>
                  <li>Lo cobrado en línea no entra a tu caja: lo ves aparte en Inicio y en tus reportes.</li>
                  <li>Para devolver, haz el reembolso del pedido con «En línea» y Mercado Pago se lo regresa al cliente.</li>
                  <li>
                    Mercado Pago le avisa a TienditApp al momento y, además, TienditApp revisa tu cuenta cada 15 minutos: una
                    devolución hecha ahí, una reclamación o un contracargo quedan anotados en su pedido.
                  </li>
                </ul>
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
