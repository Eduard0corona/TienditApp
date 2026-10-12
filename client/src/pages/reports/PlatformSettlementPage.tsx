import { useEffect, useId, useMemo, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { getApiErrorMessage, getApiErrorStatus } from '../../api/errors';
import {
  useClosePlatformSettlement,
  useCreatePlatformSettlement,
  usePendingPlatformOrders,
  usePlatformSettlement,
  useUpdatePlatformSettlement,
  useVoidPlatformSettlement,
  type PlatformSettlementDetail,
} from '../../api/platformSettlements';
import PageTitle from '../../components/common/PageTitle';
import SettlementAdjustments, { SettlementAdjustmentList } from '../../components/reports/SettlementAdjustments';
import SettlementOrders from '../../components/reports/SettlementOrders';
import SettlementSummary from '../../components/reports/SettlementSummary';
import { describePeriod } from '../../components/reports/reportPeriods';
import { getSettlementStatusChip, getSettlementStatusLabel } from '../../components/reports/settlementLabels';
import {
  MAX_ADJUSTMENT_AMOUNT,
  MAX_ADJUSTMENTS,
  MAX_NOTES_LENGTH,
  MAX_PERIOD_DAYS,
  MAX_REFERENCE_LENGTH,
  MAX_VOID_REASON_LENGTH,
  buildBody,
  changedSinceSaved,
  depositProblems,
  differenceAdjustment,
  firstProblem,
  formOf,
  isPicked,
  mergeOrders,
  newSettlementForm,
  sameBody,
  savedBody,
  savedTotals,
  totalsOf,
  type SettlementForm,
} from '../../components/reports/settlementEdits';
import { useAuth } from '../../hooks/useAuth';
import { useConfirm } from '../../hooks/useConfirm';
import { useTenant } from '../../hooks/useTenant';
import { useToast } from '../../hooks/useToast';
import { PlatformSettlementStatus, type OrderChannel } from '../../types';
import { getAccess } from '../../utils/access';
import { calendarDaysBetween, formatCalendarDate, formatDateTime, getBusinessToday } from '../../utils/dates';
import { getPlatformName, isDeliveryPlatform, type DeliveryPlatform } from '../../utils/deliveryPlatforms';
import { formatMoney } from '../../utils/money';
import './PlatformSettlements.css';

const PARENT = { label: 'Liquidaciones', to: '/reports/settlements' };

/**
 * Reportes › Liquidaciones › una liquidación: the deposit of a platform with the orders it pays and the adjustments that
 * explain the rest. Owners and admins write it while it is open and close it once its difference is $0.00; whoever reads
 * the reports sees it as it is. `new?channel=3` opens one of that platform.
 */
export default function PlatformSettlementPage() {
  const { id } = useParams<{ id: string }>();
  const [searchParams] = useSearchParams();
  const { user } = useAuth();
  const { capabilities } = useTenant();
  if (id && id !== 'new') return <SavedSettlement id={id} />;

  // Whatever the link says, only Rappi, Uber Eats or DiDi Food open a settlement.
  const channel = Number(searchParams.get('channel')) as OrderChannel;
  const platform = isDeliveryPlatform(channel) ? channel : null;
  const reason = !getAccess(user, capabilities).isAdmin
    ? 'Solo el dueño o un administrador del negocio puede registrar los depósitos de las plataformas.'
    : platform === null ? 'Elige la plataforma del depósito en Liquidaciones.' : '';
  if (reason || platform === null) {
    return (
      <>
        <PageTitle title="Nueva liquidación" parent={PARENT} />
        <div className="alert alert-warning">
          {reason} <Link to={PARENT.to}>Volver a Liquidaciones</Link>
        </div>
      </>
    );
  }
  return <SettlementScreen key={`new-${platform}`} platform={platform} detail={null} />;
}

function SavedSettlement({ id }: { id: string }) {
  const settlement = usePlatformSettlement(id);
  // What the last action found (someone else saved, or a figure changed): it stays on screen after reading it again.
  const [notice, setNotice] = useState('');

  if (settlement.isPending) {
    return (
      <>
        <PageTitle title="Liquidación" parent={PARENT} />
        <p className="text-muted" aria-busy="true">Cargando…</p>
      </>
    );
  }
  if (settlement.isError) {
    const missing = getApiErrorStatus(settlement.error) === 404;
    return (
      <>
        <PageTitle title="Liquidación" parent={PARENT} />
        <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2" role="alert">
          <span className="me-auto">
            {missing ? 'No encontramos esta liquidación.' : getApiErrorMessage(settlement.error, 'No se pudo cargar la liquidación.')}
          </span>
          {!missing && <button type="button" className="btn btn-sm btn-light" onClick={() => void settlement.refetch()}>Reintentar</button>}
        </div>
      </>
    );
  }

  const detail = settlement.data;
  const platform = detail.settlement.channel;
  // The server keeps only Rappi, Uber Eats and DiDi Food; anything else would be a contract the screen does not know.
  if (!isDeliveryPlatform(platform)) return null;
  return (
    <SettlementScreen
      // Each revision opens the screen again with what the server answered.
      key={`${detail.settlement.id}-${detail.settlement.revision}`}
      platform={platform}
      detail={detail}
      notice={notice}
      onNotice={setNotice}
      onReload={async () => {
        await settlement.refetch();
      }}
    />
  );
}

interface SettlementScreenProps {
  platform: DeliveryPlatform;
  /** The settlement as saved, or null for a new one. */
  detail: PlatformSettlementDetail | null;
  notice?: string;
  onNotice?: (notice: string) => void;
  /** Reads the settlement again (someone else saved it, or the server counted it again). */
  onReload?: () => Promise<void>;
}

function SettlementScreen({ platform, detail, notice = '', onNotice, onReload }: SettlementScreenProps) {
  const id = useId();
  const navigate = useNavigate();
  const toast = useToast();
  const { confirm, prompt } = useConfirm();
  const { user } = useAuth();
  const { capabilities } = useTenant();
  const canManage = getAccess(user, capabilities).isAdmin;
  const name = getPlatformName(platform);
  const today = getBusinessToday();
  const settlement = detail?.settlement ?? null;
  const status = settlement?.status ?? PlatformSettlementStatus.Open;
  const editing = canManage && status === PlatformSettlementStatus.Open;

  const [form, setForm] = useState<SettlementForm>(() => (detail ? formOf(detail) : newSettlementForm(today)));
  const [initial] = useState(() => JSON.stringify(form));
  // Every problem in red once the person tried to save; before, only those of what they already wrote.
  const [revealed, setRevealed] = useState(false);
  const [error, setError] = useState('');
  const [conflict, setConflict] = useState(false);
  const create = useCreatePlatformSettlement();
  const update = useUpdatePlatformSettlement();
  const close = useClosePlatformSettlement();
  const voiding = useVoidPlatformSettlement();
  const busy = create.isPending || update.isPending || close.isPending || voiding.isPending;

  // The pending orders of the period: those the server sent with the settlement, or those of the period being written.
  const periodChanged = !settlement || form.from !== settlement.periodFrom || form.to !== settlement.periodTo;
  const periodReadable = !!form.from && !!form.to && form.to >= form.from && calendarDaysBetween(form.from, form.to) < MAX_PERIOD_DAYS;
  const pending = usePendingPlatformOrders(platform, form.from, form.to, editing && periodChanged && periodReadable);
  const saved = useMemo(() => new Set(detail?.orders.map((order) => order.orderId) ?? []), [detail]);
  const orders = useMemo(() => {
    if (!editing) return detail?.orders ?? [];
    const candidates = periodChanged ? (periodReadable ? pending.data?.orders ?? [] : []) : detail?.candidates ?? [];
    return mergeOrders(detail?.orders ?? [], candidates);
  }, [editing, detail, periodChanged, periodReadable, pending.data]);
  const truncated = editing && (periodChanged ? periodReadable && !!pending.data?.truncated : !!detail?.candidatesTruncated);
  // Another settlement of the platform still open: the new one would be refused.
  const openElsewhere = !settlement && periodReadable ? pending.data?.openSettlementId ?? null : null;

  const picked = useMemo(() => orders.filter((order) => isPicked(order, form, saved, !settlement)), [orders, form, saved, settlement]);
  const totals = editing || !settlement ? totalsOf(picked, form.adjustments, form.depositedText) : savedTotals(settlement);
  const body = buildBody(form, picked.map((order) => order.orderId));
  const changedNets = detail ? changedSinceSaved(detail) : [];
  const edited = detail ? !sameBody(body, savedBody(detail)) : JSON.stringify(form) !== initial;
  const needsSave = editing && (!settlement || edited || changedNets.length > 0);
  const problem = editing ? firstProblem(form, picked.length, today) : '';
  const problems = depositProblems(form, today);
  const showDeposited = revealed || form.depositedText.trim() !== '';
  const differenceCents = totals.differenceCents;
  const canExplain = editing && differenceCents !== undefined && differenceCents !== 0
    && Math.abs(differenceCents) <= MAX_ADJUSTMENT_AMOUNT * 100 && form.adjustments.length < MAX_ADJUSTMENTS;
  const canClose = editing && !!settlement && !needsSave && settlement.difference === 0
    && (settlement.orderCount > 0 || (detail?.adjustments.length ?? 0) > 0);

  // A reload or a closed tab would lose what was written: the browser asks first.
  useEffect(() => {
    if (!editing || !edited) return;
    const keep = (event: BeforeUnloadEvent) => {
      event.preventDefault();
    };
    window.addEventListener('beforeunload', keep);
    return () => window.removeEventListener('beforeunload', keep);
  }, [editing, edited]);

  const change = (patch: Partial<SettlementForm>) => setForm((current) => ({ ...current, ...patch }));
  const pick = (orderIds: string[], value: boolean) =>
    setForm((current) => ({ ...current, picks: { ...current.picks, ...Object.fromEntries(orderIds.map((orderId) => [orderId, value])) } }));

  const fail = (err: unknown, fallback: string) => {
    setConflict(getApiErrorStatus(err) === 409);
    setError(getApiErrorMessage(err, fallback));
  };

  const save = async () => {
    if (problem) {
      setRevealed(true);
      return;
    }
    if (openElsewhere) return;
    setError('');
    setConflict(false);
    onNotice?.('');
    try {
      if (!settlement) {
        const created = await create.mutateAsync({ channel: platform, ...body });
        toast.success(`Liquidación de ${name} guardada.`);
        navigate(`/reports/settlements/${created.settlement.id}`, { replace: true });
        return;
      }
      await update.mutateAsync({ id: settlement.id, body: { revision: settlement.revision, ...body } });
      // The answer replaced the settlement read: the screen opens again with it.
      toast.success('Liquidación guardada.');
    } catch (err: unknown) {
      fail(err, 'No se pudo guardar la liquidación. Intenta de nuevo.');
    }
  };

  const discard = async () => {
    if (!detail) return;
    const accepted = await confirm({
      title: 'Descartar cambios',
      message: 'Se pierde lo que cambiaste desde que guardaste.',
      confirmLabel: 'Descartar',
      danger: true,
    });
    if (!accepted) return;
    setForm(formOf(detail));
    setRevealed(false);
    setError('');
    setConflict(false);
  };

  // Someone else saved meanwhile: their version replaces this one, after asking if something was written.
  const reload = async () => {
    if (!settlement) {
      setError('');
      setConflict(false);
      await pending.refetch();
      return;
    }
    const accepted = !edited || await confirm({
      title: 'Ver la versión guardada',
      message: 'Se pierde lo que cambiaste y verás la liquidación como la guardó la otra persona.',
      confirmLabel: 'Ver la versión guardada',
      danger: true,
    });
    if (accepted) await onReload?.();
  };

  // A 409 on closing or voiding: someone else saved, or the server counted the orders again and saved the new figures.
  const readAgain = async (err: unknown, fallback: string) => {
    if (getApiErrorStatus(err) !== 409) {
      fail(err, fallback);
      return;
    }
    onNotice?.(getApiErrorMessage(err, fallback));
    await onReload?.();
  };

  const closeIt = async () => {
    if (!settlement) return;
    const accepted = await confirm({
      title: 'Cerrar liquidación',
      message: `La liquidación de ${name} del ${describePeriod(settlement.periodFrom, settlement.periodTo)} ya no cambiará. Si después encuentras un error, anúlala y registra otra.`,
      confirmLabel: 'Cerrar liquidación',
    });
    if (!accepted) return;
    setError('');
    onNotice?.('');
    try {
      await close.mutateAsync({ id: settlement.id, revision: settlement.revision });
      toast.success(`Liquidación de ${name} cerrada.`);
    } catch (err: unknown) {
      await readAgain(err, 'No se pudo cerrar la liquidación. Intenta de nuevo.');
    }
  };

  const voidIt = async () => {
    if (!settlement) return;
    const reason = await prompt({
      title: 'Anular liquidación',
      message: settlement.orderCount === 1
        ? 'Su pedido vuelve a quedar pendiente. La liquidación se queda como historia: no se borra.'
        : `Sus ${settlement.orderCount} pedidos vuelven a quedar pendientes. La liquidación se queda como historia: no se borra.`,
      inputLabel: 'Motivo',
      placeholder: 'Por ejemplo, capturé el depósito de otra semana',
      required: true,
      maxLength: MAX_VOID_REASON_LENGTH,
      multiline: true,
      confirmLabel: 'Anular liquidación',
      danger: true,
    });
    if (reason === null) return;
    setError('');
    onNotice?.('');
    try {
      await voiding.mutateAsync({ id: settlement.id, revision: settlement.revision, reason });
      toast.success(`Liquidación de ${name} anulada.`);
    } catch (err: unknown) {
      await readAgain(err, 'No se pudo anular la liquidación. Intenta de nuevo.');
    }
  };

  const leave = async () => {
    const accepted = !edited || await confirm({
      title: 'Descartar liquidación',
      message: 'Se pierde lo que escribiste.',
      confirmLabel: 'Descartar',
      danger: true,
    });
    if (accepted) navigate(PARENT.to);
  };

  const statusText = (() => {
    if (revealed && problem) return problem;
    if (openElsewhere) return `Ya hay una liquidación abierta de ${name}.`;
    if (!settlement) return 'Sin guardar.';
    if (edited) return 'Cambios sin guardar.';
    if (changedNets.length > 0) return 'Guarda para tomar las cifras nuevas.';
    if (settlement.difference !== 0) return 'Explica la diferencia con un ajuste para cerrar.';
    return canClose ? 'Cuadra: ya puedes cerrarla.' : 'Agrega al menos un pedido o un ajuste para cerrar.';
  })();

  return (
    <>
      <PageTitle title={settlement ? `Liquidación de ${name}` : `Nueva liquidación de ${name}`} parent={PARENT} />

      {settlement && (
        <div className="d-flex flex-wrap align-items-center gap-2 mb-3">
          <span className={`ta-chip ${getSettlementStatusChip(settlement.status)}`}>
            {getSettlementStatusLabel(settlement.status)}
          </span>
          <span className="small text-muted">
            Del {describePeriod(settlement.periodFrom, settlement.periodTo)}
            {settlement.closedAt && ` · Cerrada el ${formatDateTime(settlement.closedAt)}`}
            {settlement.voidedAt && ` · Anulada el ${formatDateTime(settlement.voidedAt)}`}
          </span>
        </div>
      )}
      {settlement?.voidReason && (
        <div className="alert alert-secondary">
          <div><strong>Motivo de la anulación:</strong> {settlement.voidReason}</div>
          <div className="small">Sus pedidos volvieron a quedar pendientes.</div>
        </div>
      )}
      {!canManage && status === PlatformSettlementStatus.Open && (
        <p className="small text-muted">Solo el dueño o un administrador del negocio puede cambiarla y cerrarla.</p>
      )}
      {notice && (
        <div className="alert alert-warning alert-dismissible" role="alert">
          {notice}
          <button type="button" className="btn-close" aria-label="Cerrar" onClick={() => onNotice?.('')}></button>
        </div>
      )}
      {openElsewhere && (
        <div className="alert alert-warning d-flex flex-wrap align-items-center gap-2" role="alert">
          <span className="me-auto">Ya hay una liquidación abierta de {name}: termínala o anúlala antes de abrir otra.</span>
          <Link className="btn btn-sm btn-light" to={`/reports/settlements/${openElsewhere}`}>Ver la liquidación abierta</Link>
        </div>
      )}

      <div className="settlement-layout">
        <div>
          <section className="card mb-3" aria-labelledby={`${id}-deposit`}>
            <div className="card-body">
              <h2 id={`${id}-deposit`} className="h5 mb-3">Depósito</h2>
              {editing ? (
                <div className="row g-3">
                  <div className="col-6 col-lg-3">
                    <label className="form-label" htmlFor={`${id}-from`}>Desde</label>
                    <input
                      id={`${id}-from`}
                      type="date"
                      className={`form-control ${problems.period ? 'is-invalid' : ''}`}
                      value={form.from}
                      max={form.to || today}
                      disabled={busy}
                      aria-describedby={problems.period ? `${id}-period-problem` : undefined}
                      onChange={(event) => change({ from: event.target.value })}
                    />
                  </div>
                  <div className="col-6 col-lg-3">
                    <label className="form-label" htmlFor={`${id}-to`}>Hasta</label>
                    <input
                      id={`${id}-to`}
                      type="date"
                      className={`form-control ${problems.period ? 'is-invalid' : ''}`}
                      value={form.to}
                      min={form.from || undefined}
                      max={today}
                      disabled={busy}
                      aria-describedby={problems.period ? `${id}-period-problem` : undefined}
                      onChange={(event) => change({ to: event.target.value })}
                    />
                  </div>
                  <div className="col-6 col-lg-3">
                    <label className="form-label" htmlFor={`${id}-deposited-on`}>Fecha del depósito</label>
                    <input
                      id={`${id}-deposited-on`}
                      type="date"
                      className={`form-control ${problems.depositedOn ? 'is-invalid' : ''}`}
                      value={form.depositedOn}
                      min={form.from || undefined}
                      max={today}
                      disabled={busy}
                      aria-describedby={problems.depositedOn ? `${id}-deposited-on-problem` : undefined}
                      onChange={(event) => change({ depositedOn: event.target.value })}
                    />
                    {problems.depositedOn && <div id={`${id}-deposited-on-problem`} className="invalid-feedback d-block">{problems.depositedOn}</div>}
                  </div>
                  <div className="col-6 col-lg-3">
                    <label className="form-label" htmlFor={`${id}-deposited`}>Lo que depositó</label>
                    {/* No decimal keyboard: on an iPhone it has no minus, and a platform may leave a negative balance. */}
                    <input
                      id={`${id}-deposited`}
                      className={`form-control text-end ${showDeposited && problems.deposited ? 'is-invalid' : ''}`}
                      autoComplete="off"
                      placeholder="0.00"
                      value={form.depositedText}
                      disabled={busy}
                      aria-describedby={`${id}-deposited-help`}
                      onChange={(event) => change({ depositedText: event.target.value })}
                    />
                    <div id={`${id}-deposited-help`} className={showDeposited && problems.deposited ? 'invalid-feedback d-block' : 'form-text'}>
                      {showDeposited && problems.deposited ? problems.deposited : 'Como dice el estado de cuenta; con «-» si quedó en contra.'}
                    </div>
                  </div>
                  {problems.period && (
                    <div className="col-12 mt-1">
                      <div id={`${id}-period-problem`} className="invalid-feedback d-block">{problems.period}</div>
                    </div>
                  )}
                  <div className="col-12 col-lg-4">
                    <label className="form-label" htmlFor={`${id}-reference`}>Referencia (opcional)</label>
                    <input
                      id={`${id}-reference`}
                      className={`form-control ${problems.reference ? 'is-invalid' : ''}`}
                      maxLength={MAX_REFERENCE_LENGTH}
                      autoComplete="off"
                      placeholder="Folio de la transferencia o del estado de cuenta"
                      value={form.reference}
                      disabled={busy}
                      onChange={(event) => change({ reference: event.target.value })}
                    />
                  </div>
                  <div className="col-12 col-lg-8">
                    <label className="form-label" htmlFor={`${id}-notes`}>Notas (opcional)</label>
                    <textarea
                      id={`${id}-notes`}
                      className={`form-control ${problems.notes ? 'is-invalid' : ''}`}
                      rows={1}
                      maxLength={MAX_NOTES_LENGTH}
                      value={form.notes}
                      disabled={busy}
                      onChange={(event) => change({ notes: event.target.value })}
                    />
                  </div>
                </div>
              ) : settlement && (
                <dl className="settlement-deposit mb-0">
                  <dt>Periodo</dt>
                  <dd>{formatCalendarDate(settlement.periodFrom)} al {formatCalendarDate(settlement.periodTo)}</dd>
                  <dt>Fecha del depósito</dt>
                  <dd>{formatCalendarDate(settlement.depositedOn)}</dd>
                  <dt>Lo que depositó</dt>
                  <dd className="ta-money">{formatMoney(settlement.depositedAmount)}</dd>
                  <dt>Referencia</dt>
                  <dd>{settlement.reference || <span className="text-muted">—</span>}</dd>
                  {settlement.notes && (
                    <>
                      <dt>Notas</dt>
                      <dd>{settlement.notes}</dd>
                    </>
                  )}
                </dl>
              )}
            </div>
          </section>

          <section className="card mb-3" aria-labelledby={`${id}-orders`}>
            <div className="card-body">
              <h2 id={`${id}-orders`} className="h5 mb-1">Pedidos</h2>
              <p className="small text-muted">
                {editing
                  ? `Los pedidos de ${name} pendientes hasta el final del periodo. Van incluidos los del periodo, y sin incluir los de antes que el estado de cuenta traiga tarde. El neto esperado de cada uno es el mismo de Ventas por canal.`
                  : 'Los pedidos que paga este depósito, con lo que se esperaba de cada uno al guardarla.'}
              </p>
              {truncated && (
                <div className="alert alert-warning small">Hay más de 2,000 pedidos pendientes hasta el final del periodo: elige un periodo más corto.</div>
              )}
              {editing && periodChanged && periodReadable && pending.isError && (
                <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2" role="alert">
                  <span className="me-auto">{getApiErrorMessage(pending.error, `No se pudieron cargar los pedidos pendientes de ${name}.`)}</span>
                  <button type="button" className="btn btn-sm btn-light" onClick={() => void pending.refetch()}>Reintentar</button>
                </div>
              )}
              {editing && changedNets.length > 0 && (
                <div className="alert alert-warning small">
                  {changedNets.length === 1
                    ? 'Cambió lo que se espera de un pedido desde que guardaste (por una devolución, por ejemplo).'
                    : `Cambió lo que se espera de ${changedNets.length} pedidos desde que guardaste (por una devolución, por ejemplo).`}
                  {' '}Guarda para tomar las cifras nuevas.
                </div>
              )}
              <SettlementOrders
                platform={platform}
                orders={orders}
                from={form.from}
                to={form.to}
                saved={saved}
                picking={editing
                  ? { isPicked: (order) => isPicked(order, form, saved, !settlement), onPick: pick, disabled: busy }
                  : undefined}
                loading={editing && periodChanged && periodReadable && pending.isPending}
              />
            </div>
          </section>

          <section className="card mb-3" aria-labelledby={`${id}-adjustments`}>
            <div className="card-body">
              <h2 id={`${id}-adjustments`} className="h5 mb-3">Ajustes</h2>
              {editing ? (
                <SettlementAdjustments
                  rows={form.adjustments}
                  onChange={(adjustments) => change({ adjustments })}
                  disabled={busy}
                  revealed={revealed}
                />
              ) : (
                <SettlementAdjustmentList adjustments={detail?.adjustments ?? []} />
              )}
            </div>
          </section>
        </div>

        <aside className="settlement-aside">
          <SettlementSummary
            {...totals}
            onExplainDifference={canExplain
              ? () => change({ adjustments: [...form.adjustments, differenceAdjustment(differenceCents ?? 0)] })
              : undefined}
          />

          {error && (
            <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2" role="alert">
              <span className="me-auto">{error}</span>
              {conflict && (
                <button type="button" className="btn btn-sm btn-light" onClick={() => void reload()}>
                  {settlement ? 'Ver la versión guardada' : 'Recargar'}
                </button>
              )}
            </div>
          )}

          {canManage && status !== PlatformSettlementStatus.Voided && (
            <div className="settlement-bar">
              {editing && (
                <span className={`small me-auto ${revealed && problem ? 'text-danger' : 'text-muted'}`} role="status">{statusText}</span>
              )}
              {needsSave ? (
                <>
                  {settlement
                    ? edited && <button type="button" className="btn btn-light" onClick={() => void discard()} disabled={busy}>Descartar</button>
                    : <button type="button" className="btn btn-light" onClick={() => void leave()} disabled={busy}>Cancelar</button>}
                  <button type="button" className="btn btn-primary" onClick={() => void save()} disabled={busy || !!openElsewhere}>
                    {create.isPending || update.isPending ? 'Guardando…' : 'Guardar'}
                  </button>
                </>
              ) : (
                <>
                  <button type="button" className="btn btn-light" onClick={() => void voidIt()} disabled={busy}>
                    {voiding.isPending ? 'Anulando…' : 'Anular'}
                  </button>
                  {editing && (
                    <button type="button" className="btn btn-primary" onClick={() => void closeIt()} disabled={busy || !canClose}>
                      {close.isPending ? 'Cerrando…' : 'Cerrar liquidación'}
                    </button>
                  )}
                </>
              )}
            </div>
          )}
        </aside>
      </div>
    </>
  );
}
