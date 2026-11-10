import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import PageTitle from '../../components/common/PageTitle';
import TabPanel from '../../components/dining/TabPanel';
import TablesConfigModal from '../../components/dining/TablesConfigModal';
import { getApiErrorMessage } from '../../api/errors';
import { tablesApi, type DiningTable, type OpenTabSummary } from '../../api/tablesApi';
import { useAuth } from '../../hooks/useAuth';
import { useConfirm } from '../../hooks/useConfirm';
import { useOrderSignalR } from '../../hooks/useOrderSignalR';
import { useTenant } from '../../hooks/useTenant';
import { getAccess } from '../../utils/access';
import { formatMoney } from '../../utils/money';
import './TablesPage.css';

function minutesSince(value: string, now: number): number {
  return Math.max(0, Math.floor((now - new Date(value).getTime()) / 60_000));
}

function formatOpenFor(minutes: number): string {
  if (minutes < 60) return `${minutes} min`;
  return `${Math.floor(minutes / 60)} h ${minutes % 60} min`;
}

const plural = (count: number, one: string, many: string) => `${count} ${count === 1 ? one : many}`;

/** What a card of an open tab shows, said in its name: what is left to charge first, since it is what is asked for. */
function describeTab(name: string, tab: OpenTabSummary, now: number, busy = '') {
  return [
    name,
    busy,
    `${formatMoney(tab.balance)} por cobrar de ${formatMoney(tab.total)}`,
    `abierta hace ${formatOpenFor(minutesSince(tab.openedAt, now))}`,
    plural(tab.rounds, 'ronda', 'rondas'),
    tab.roundsReady > 0 ? plural(tab.roundsReady, 'lista', 'listas') : '',
    tab.roundsInKitchen > 0 ? `${tab.roundsInKitchen} en cocina` : '',
  ].filter(Boolean).join(', ');
}

/**
 * Mesas: every table with its open tab (total, time, rounds in the kitchen or ready to take). A free table
 * opens a tab in Vender; a busy one shows its bill to add a round, charge, move or close it.
 */
export default function TablesPage() {
  const navigate = useNavigate();
  const { prompt } = useConfirm();
  const queryClient = useQueryClient();
  const { user } = useAuth();
  const { tenant, capabilities } = useTenant();
  const access = getAccess(user, capabilities);
  const [openTabId, setOpenTabId] = useState<string | null>(null);
  const [configuring, setConfiguring] = useState(false);
  const [clock, setClock] = useState(() => Date.now());
  const { onOrderCreated, onOrderStatusChanged, onOrderCancelled, onKitchenTicketsChanged, onConnected } = useOrderSignalR();

  const room = useQuery({
    queryKey: ['tables', 'room'],
    queryFn: () => tablesApi.getDiningRoom(),
    // Realtime events refresh it; polling covers a dropped connection.
    refetchInterval: 30_000,
  });

  useEffect(() => {
    // A change of an order reads the tables again, and the tab open in its panel too (a round ready, a charge).
    const refresh = () => {
      void queryClient.invalidateQueries({ queryKey: ['tables'] });
      void queryClient.invalidateQueries({ queryKey: ['orders'] });
    };
    const ticking = window.setInterval(() => setClock(Date.now()), 30_000);
    const unsubscribers = [
      onOrderCreated(refresh),
      onOrderStatusChanged(refresh),
      onOrderCancelled(refresh),
      onKitchenTicketsChanged(refresh),
      onConnected(refresh),
    ];
    return () => {
      window.clearInterval(ticking);
      unsubscribers.forEach((unsubscribe) => unsubscribe());
    };
  }, [queryClient, onOrderCreated, onOrderStatusChanged, onOrderCancelled, onKitchenTicketsChanged, onConnected]);

  // How long each tab has been open counts from the last tick of the clock or the last reading, whichever is later.
  const now = Math.max(clock, room.dataUpdatedAt);
  const tables = useMemo(() => room.data?.tables ?? [], [room.data]);
  const byArea = useMemo(() => {
    const groups = new Map<string, DiningTable[]>();
    for (const table of tables) {
      const area = table.area?.trim() || 'Salón';
      groups.set(area, [...(groups.get(area) ?? []), table]);
    }
    return [...groups.entries()];
  }, [tables]);
  const freeTables = tables.filter((table) => !table.openTab);
  const tabsWithoutTable = room.data?.tabsWithoutTable ?? [];

  const openNamedTab = async () => {
    const name = await prompt({
      title: 'Cuenta sin mesa',
      message: 'La cuenta se abre a nombre de alguien (barra, para llevar) y recibe sus rondas como una mesa.',
      inputLabel: '¿A nombre de quién va la cuenta?',
      placeholder: 'Por ejemplo "Barra - Juan"',
      confirmLabel: 'Abrir cuenta',
      required: true,
      maxLength: 50,
    });
    if (name) navigate(`/sell?label=${encodeURIComponent(name.slice(0, 50))}`);
  };

  const tabBadges = (tab: OpenTabSummary) => (
    <div className="d-flex flex-wrap gap-1 mt-1">
      {tab.roundsReady > 0 && <span className="badge bg-success">{tab.roundsReady} lista{tab.roundsReady === 1 ? '' : 's'}</span>}
      {tab.roundsInKitchen > 0 && <span className="badge bg-warning text-dark">{tab.roundsInKitchen} en cocina</span>}
    </div>
  );

  return (
    <>
      <PageTitle title="Mesas" />
      <div className="d-flex flex-wrap align-items-center justify-content-between gap-2 mb-3">
        <div className="text-muted small">
          {tables.length > 0 && `${tables.length - freeTables.length} de ${tables.length} ocupadas`}
        </div>
        <div className="d-flex gap-2">
          <button type="button" className="btn btn-sm btn-light" onClick={() => void openNamedTab()}>
            <i className="ri-user-add-line me-1" aria-hidden="true"></i>Cuenta sin mesa
          </button>
          {access.catalog && (
            <button type="button" className="btn btn-sm btn-light" onClick={() => setConfiguring(true)}>
              <i className="ri-settings-3-line me-1" aria-hidden="true"></i>Configurar mesas
            </button>
          )}
        </div>
      </div>

      {room.isError && (
        <div className="alert alert-danger d-flex flex-wrap align-items-center gap-2" role="alert">
          <span className="me-auto">{getApiErrorMessage(room.error, 'No se pudieron cargar las mesas.')}</span>
          <button type="button" className="btn btn-sm btn-light" disabled={room.isFetching} onClick={() => void room.refetch()}>
            Reintentar
          </button>
        </div>
      )}
      <p className="visually-hidden" role="status">{room.isPending ? 'Cargando las mesas…' : ''}</p>
      {room.isPending ? (
        <div className="tables-grid" aria-busy="true">
          {Array.from({ length: 6 }, (_, index) => <div key={index} className="table-card table-card-skeleton" />)}
        </div>
      ) : !room.data ? null : tables.length === 0 ? (
        <div className="card">
          <div className="card-body text-center py-5">
            <i className="ri-restaurant-line fs-1 text-muted" aria-hidden="true"></i>
            <p className="mt-2 mb-3">
              {access.catalog
                ? 'Agrega tus mesas para abrir cuentas por mesa y mandar cada ronda a la cocina.'
                : 'Todavía no hay mesas. Quien administra el catálogo puede agregarlas.'}
            </p>
            {access.catalog && <button type="button" className="btn btn-primary" onClick={() => setConfiguring(true)}>Agregar mesas</button>}
          </div>
        </div>
      ) : (
        byArea.map(([area, areaTables]) => (
          <section key={area} className="mb-3" aria-label={area}>
            {byArea.length > 1 && <h2 className="h6 text-muted text-uppercase mb-2">{area}</h2>}
            <div className="tables-grid">
              {areaTables.map((table) => {
                const tab = table.openTab;
                return (
                  <button
                    key={table.id}
                    type="button"
                    className={`table-card ${tab ? 'busy' : 'free'}`}
                    onClick={() => (tab
                      ? setOpenTabId(tab.orderId)
                      : navigate(`/sell?table=${table.id}&label=${encodeURIComponent(table.name)}`))}
                    aria-label={tab
                      ? describeTab(table.name, tab, now, 'ocupada')
                      : [table.name, 'libre', table.seats ? `${table.seats} lugares` : '', 'abrir cuenta'].filter(Boolean).join(', ')}
                  >
                    <span className="table-card-name">{table.name}</span>
                    {tab ? (
                      <>
                        <span className="table-card-total ta-money">{formatMoney(tab.total)}</span>
                        <span className="small text-muted">{formatOpenFor(minutesSince(tab.openedAt, now))} · {tab.rounds} ronda{tab.rounds === 1 ? '' : 's'}</span>
                        {tabBadges(tab)}
                      </>
                    ) : (
                      <span className="small text-success">Libre{table.seats ? ` · ${table.seats} lugares` : ''}</span>
                    )}
                  </button>
                );
              })}
            </div>
          </section>
        ))
      )}

      {tabsWithoutTable.length > 0 && (
        <section className="mb-3" aria-label="Cuentas sin mesa">
          <h2 className="h6 text-muted text-uppercase mb-2">Cuentas sin mesa</h2>
          <div className="tables-grid">
            {tabsWithoutTable.map((tab) => (
              <button
                key={tab.orderId}
                type="button"
                className="table-card busy"
                onClick={() => setOpenTabId(tab.orderId)}
                aria-label={describeTab(tab.tableNumber || tab.customerName, tab, now)}
              >
                <span className="table-card-name">{tab.tableNumber || tab.customerName}</span>
                <span className="table-card-total ta-money">{formatMoney(tab.total)}</span>
                <span className="small text-muted">{formatOpenFor(minutesSince(tab.openedAt, now))}</span>
                {tabBadges(tab)}
              </button>
            ))}
          </div>
        </section>
      )}

      {openTabId && (
        <TabPanel
          orderId={openTabId}
          freeTables={freeTables}
          businessName={tenant?.name ?? 'TienditApp'}
          logoUrl={tenant?.logoImages?.small ?? tenant?.logoUrl}
          onClose={() => setOpenTabId(null)}
        />
      )}
      {configuring && <TablesConfigModal onClose={() => setConfiguring(false)} />}
    </>
  );
}
