import { useId, useState } from 'react';
import { Link } from 'react-router-dom';
import { getApiErrorMessage } from '../../api/errors';
import {
  usePlatformReceivables,
  usePlatformSettlements,
  type PlatformReceivable,
  type PlatformSettlement,
  type PlatformSettlementFilters,
} from '../../api/platformSettlements';
import DataTable, { DataTableFilter, type DataTableColumn } from '../../components/common/DataTable';
import PageTitle from '../../components/common/PageTitle';
import { describePeriod } from '../../components/reports/reportPeriods';
import { getSettlementStatusChip, getSettlementStatusLabel } from '../../components/reports/settlementLabels';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { PlatformSettlementStatus } from '../../types';
import { getAccess, type Access } from '../../utils/access';
import { getPlatformName, isDeliveryPlatform, type DeliveryPlatform } from '../../utils/deliveryPlatforms';
import { DELIVERY_PLATFORM_CHANNELS } from '../../utils/labels';
import { formatMoney } from '../../utils/money';
import './PlatformSettlements.css';

// The state goes under the platform: on a phone the first columns are what shows, and a voided settlement must not
// look like the closed one that replaced it.
const COLUMNS: DataTableColumn[] = [
  { header: 'Plataforma' },
  { header: 'Periodo' },
  { header: 'Depositó', className: 'text-end' },
  { header: 'Diferencia', className: 'text-end' },
  { header: 'Acciones', className: 'text-end' },
];

// The API takes up to 100 a page and answers 20 by default, and a search of up to 100 characters.
const PAGE_SIZES = [20, 50, 100];
const MAX_SEARCH_LENGTH = 100;

const STATUSES = [PlatformSettlementStatus.Open, PlatformSettlementStatus.Closed, PlatformSettlementStatus.Voided];

/** "12 oct.": a date of the business in a short line, written as the periods are. */
function shortDay(date: string): string {
  return describePeriod(date, date);
}

/** A sentence that ends in an abbreviation («oct.») takes no second period. */
function sentence(text: string): string {
  return text.endsWith('.') ? text : `${text}.`;
}

function platformOf(settlement: PlatformSettlement): string {
  return isDeliveryPlatform(settlement.channel) ? getPlatformName(settlement.channel) : String(settlement.channel);
}

/**
 * Reportes › Liquidaciones: what each delivery platform still owes for the orders not settled yet, and the settlement of
 * each deposit, the newest period first. Owners and admins register a deposit from the platform's card.
 */
export default function PlatformSettlements() {
  const id = useId();
  const { user } = useAuth();
  const { capabilities } = useTenant();
  const access = getAccess(user, capabilities);
  const receivables = usePlatformReceivables();
  const [filters, setFilters] = useState<PlatformSettlementFilters>({ channel: null, status: null, search: '' });
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(PAGE_SIZES[0]);
  const settlements = usePlatformSettlements(filters, page, pageSize);

  const filter = (patch: Partial<PlatformSettlementFilters>) => {
    setFilters((current) => ({ ...current, ...patch }));
    setPage(1);
  };
  const filtered = filters.channel !== null || filters.status !== null;

  return (
    <>
      <PageTitle title="Liquidaciones" />
      <p className="text-muted">
        Registra cada depósito de Rappi, Uber Eats y DiDi Food con los pedidos que paga, y explica con ajustes lo que no cuadre.
        Lo que te retienen de ISR e IVA se revisa en <Link to="/reports/settlements/withholdings">Retenciones</Link>.
      </p>

      <section aria-labelledby={`${id}-owed`}>
        <h2 id={`${id}-owed`} className="h5 mb-3">Por cobrar</h2>
        {receivables.isPending && <p className="text-muted small" aria-busy="true">Cargando…</p>}
        {receivables.isError && (
          <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2" role="alert">
            <span className="me-auto">{getApiErrorMessage(receivables.error, 'No se pudo cargar lo que te deben las plataformas.')}</span>
            <button type="button" className="btn btn-sm btn-light" onClick={() => void receivables.refetch()}>Reintentar</button>
          </div>
        )}
        {receivables.data && (receivables.data.length === 0 ? (
          <div className="alert alert-info">
            No tienes plataformas de reparto activas. Actívalas en{' '}
            {access.businessSettings
              ? <Link to="/settings/business">Configuración › Negocio › Plataformas de reparto</Link>
              : 'Configuración › Negocio › Plataformas de reparto'}
            .
          </div>
        ) : (
          <div className="settlement-receivables">
            {receivables.data.map((receivable) => (
              <ReceivableCard key={receivable.channel} receivable={receivable} access={access} />
            ))}
          </div>
        ))}
      </section>

      <section className="card" aria-labelledby={`${id}-history`}>
        <div className="card-body">
          <h2 id={`${id}-history`} className="h5 mb-3">Liquidaciones</h2>
          {/* Above the list, which stays with its search and filters: what was typed is not lost to the error. */}
          {settlements.isError && (
            <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2" role="alert">
              <span className="me-auto">{getApiErrorMessage(settlements.error, 'No se pudieron cargar las liquidaciones.')}</span>
              <button type="button" className="btn btn-sm btn-light" onClick={() => void settlements.refetch()}>Reintentar</button>
            </div>
          )}
          <DataTable<PlatformSettlement>
            data={settlements.data?.items ?? []}
            columns={COLUMNS}
            keyExtractor={(settlement) => settlement.id}
            loading={settlements.isPending}
            emptyMessage={settlements.isError
              ? 'Sin liquidaciones que mostrar.'
              : filtered ? 'Ninguna liquidación con estos filtros.' : 'Todavía no registras depósitos de las plataformas.'}
            searchPlaceholder="Buscar referencia o notas"
            searchMaxLength={MAX_SEARCH_LENGTH}
            pageSizeOptions={PAGE_SIZES}
            serverSide={{
              totalCount: settlements.data?.totalCount ?? 0,
              page,
              pageSize,
              onPageChange: setPage,
              onPageSizeChange: (size) => {
                setPageSize(size);
                setPage(1);
              },
              onSearchChange: (search) => filter({ search }),
            }}
            filters={(
              <>
                <DataTableFilter label="Plataforma" htmlFor={`${id}-platform`}>
                  <select
                    id={`${id}-platform`}
                    className="form-select form-select-sm"
                    value={filters.channel ?? ''}
                    onChange={(event) => filter({ channel: event.target.value === '' ? null : (Number(event.target.value) as DeliveryPlatform) })}
                  >
                    <option value="">Todas</option>
                    {DELIVERY_PLATFORM_CHANNELS.map((channel) => <option key={channel} value={channel}>{getPlatformName(channel)}</option>)}
                  </select>
                </DataTableFilter>
                <DataTableFilter label="Estado" htmlFor={`${id}-status`}>
                  <select
                    id={`${id}-status`}
                    className="form-select form-select-sm"
                    value={filters.status ?? ''}
                    onChange={(event) => filter({
                      status: event.target.value === '' ? null : (Number(event.target.value) as PlatformSettlementStatus),
                    })}
                  >
                    <option value="">Todos</option>
                    {STATUSES.map((status) => <option key={status} value={status}>{getSettlementStatusLabel(status)}</option>)}
                  </select>
                </DataTableFilter>
              </>
            )}
            renderRow={(settlement) => {
              const period = describePeriod(settlement.periodFrom, settlement.periodTo);
              const writable = access.isAdmin && settlement.status === PlatformSettlementStatus.Open;
              const action = writable ? 'Continuar' : 'Ver';
              const state = getSettlementStatusLabel(settlement.status);
              return (
                <>
                  <td>
                    <div className="fw-semibold">{platformOf(settlement)}</div>
                    <span className={`ta-chip ${getSettlementStatusChip(settlement.status)}`}>{state}</span>
                  </td>
                  <td>
                    {period}
                    <div className="small text-muted">{settlement.orderCount === 1 ? '1 pedido' : `${settlement.orderCount} pedidos`}</div>
                  </td>
                  <td className="text-end">
                    <span className="ta-money">{formatMoney(settlement.depositedAmount)}</span>
                    <div className="small text-muted">el {shortDay(settlement.depositedOn)}</div>
                  </td>
                  <td className="text-end ta-money">{formatMoney(settlement.difference)}</td>
                  <td className="text-end">
                    {/* The state in the name: a voided settlement and the one that replaced it share platform and period. */}
                    <Link
                      className="btn btn-light btn-sm"
                      to={`/reports/settlements/${settlement.id}`}
                      aria-label={`${action} la liquidación ${state.toLowerCase()} de ${platformOf(settlement)} del ${period}`}
                    >
                      {action}
                    </Link>
                  </td>
                </>
              );
            }}
          />
        </div>
      </section>
    </>
  );
}

/** What a platform owes: its pending orders and their expected net, until when it settled, and how to settle it. */
function ReceivableCard({ receivable, access }: { receivable: PlatformReceivable; access: Access }) {
  const titleId = useId();
  if (!isDeliveryPlatform(receivable.channel)) return null;
  const name = getPlatformName(receivable.channel);
  const openId = receivable.openSettlementId;

  return (
    <article className="card mb-0" aria-labelledby={titleId}>
      <div className="card-body d-flex flex-column gap-1">
        <div className="d-flex align-items-center justify-content-between gap-2">
          <h3 id={titleId} className="h6 mb-0">{name}</h3>
          {!receivable.isActive && <span className="badge bg-secondary-subtle text-secondary">Apagada</span>}
        </div>
        <div className="h4 mb-0 ta-money">{formatMoney(receivable.pendingAmount)}</div>
        <div className="small text-muted">
          {receivable.pendingOrders === 0
            ? 'Sin pedidos pendientes.'
            : sentence(`${receivable.pendingOrders === 1 ? '1 pedido pendiente' : `${receivable.pendingOrders} pedidos pendientes`}${receivable.oldestPendingOn ? ` desde el ${shortDay(receivable.oldestPendingOn)}` : ''}`)}
        </div>
        <div className="small text-muted">
          {receivable.settledThrough ? sentence(`Liquidado hasta el ${shortDay(receivable.settledThrough)}`) : 'Todavía sin liquidaciones cerradas.'}
        </div>
        <div className="mt-2">
          {openId ? (
            <Link className="btn btn-light btn-sm" to={`/reports/settlements/${openId}`} aria-label={`${access.isAdmin ? 'Continuar' : 'Ver'} la liquidación abierta de ${name}`}>
              {access.isAdmin ? 'Continuar la liquidación' : 'Ver la liquidación abierta'}
            </Link>
          ) : access.isAdmin && (
            <Link className="btn btn-light btn-sm" to={`/reports/settlements/new?channel=${receivable.channel}`} aria-label={`Registrar depósito de ${name}`}>
              Registrar depósito
            </Link>
          )}
        </div>
      </div>
    </article>
  );
}
