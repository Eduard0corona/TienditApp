import { useCallback, useEffect, useRef, useState } from 'react';
import PageTitle from '../../components/common/PageTitle';
import DataTable, { DataTableFilter } from '../../components/common/DataTable';
import { auditApi } from '../../api/auditApi';
import { getApiErrorMessage } from '../../api/errors';
import type { AuditLogDto, PaginatedList } from '../../types';
import { formatInBusinessZone, businessDayEnd, businessDayStart } from '../../utils/dates';

const COLUMNS = [
  { header: 'Fecha' },
  { header: 'Entidad' },
  { header: 'Acción' },
  { header: 'Id entidad' },
  { header: 'Usuario' },
];

// The entity and the action are matched whole on the server: they are asked for once typing stops, not on each key.
const TYPED_FILTER_DELAY_MS = 300;

const PAGE_SIZES = [20, 50, 100];

const EMPTY_PAGE: PaginatedList<AuditLogDto> = {
  items: [],
  pageNumber: 1,
  totalPages: 0,
  totalCount: 0,
  hasPreviousPage: false,
  hasNextPage: false,
};

function toLocalDateTime(value: string) {
  return formatInBusinessZone(value, {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
}

/**
 * The business's activity: who changed what. The list is read on the server with its filters and search; only the last
 * reading asked for is shown, so one that arrives late after a filter changed does not replace it.
 */
export default function AuditLogs() {
  const [data, setData] = useState<PaginatedList<AuditLogDto>>(EMPTY_PAGE);
  // What is typed in Entidad and Acción, and what was asked for after the pause.
  const [entityInput, setEntityInput] = useState('');
  const [actionInput, setActionInput] = useState('');
  const [entityName, setEntityName] = useState('');
  const [action, setAction] = useState('');
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(PAGE_SIZES[0]);
  const [search, setSearch] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [failed, setFailed] = useState(false);
  const latestRead = useRef(0);

  useEffect(() => {
    const entity = entityInput.trim();
    const typedAction = actionInput.trim();
    if (entity === entityName && typedAction === action) return;
    const timeoutId = window.setTimeout(() => {
      setEntityName(entity);
      setAction(typedAction);
      setPageNumber(1);
    }, TYPED_FILTER_DELAY_MS);
    return () => window.clearTimeout(timeoutId);
  }, [action, actionInput, entityInput, entityName]);

  const loadData = useCallback(async () => {
    const read = ++latestRead.current;
    setLoading(true);
    setError('');
    try {
      const { data: response } = await auditApi.getLogs({
        entityName: entityName || undefined,
        action: action || undefined,
        from: fromDate ? businessDayStart(fromDate) : undefined,
        to: toDate ? businessDayEnd(toDate) : undefined,
        search: search || undefined,
        pageNumber,
        pageSize,
      });
      if (read !== latestRead.current) return;
      setData(response);
      setFailed(false);
    } catch (err: unknown) {
      if (read !== latestRead.current) return;
      setError(getApiErrorMessage(err, 'No se pudo cargar la actividad del negocio.'));
      setFailed(true);
      // What was read before is of other filters or another page: it is not left under these.
      setData(EMPTY_PAGE);
    } finally {
      if (read === latestRead.current) setLoading(false);
    }
  }, [action, entityName, fromDate, pageNumber, pageSize, search, toDate]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  return (
    <>
      <PageTitle title="Seguridad y actividad" />

      <div className="card">
        <div className="card-body">
          {error && (
            <div className="alert alert-danger alert-dismissible fade show d-flex flex-wrap align-items-center gap-2" role="alert">
              <span className="me-auto">{error}</span>
              {/* Reading again takes the notice away while it loads. */}
              <button type="button" className="btn btn-sm btn-light" onClick={() => void loadData()}>
                Reintentar
              </button>
              <button type="button" className="btn-close" aria-label="Cerrar" onClick={() => setError('')}></button>
            </div>
          )}

          <DataTable
            data={data.items}
            columns={COLUMNS}
            keyExtractor={(item) => item.id}
            loading={loading}
            failedMessage={failed ? 'No se pudo leer la actividad.' : undefined}
            emptyMessage={entityName || action || fromDate || toDate || search
              ? 'No hay actividad registrada con estos filtros.'
              : 'Todavía no hay actividad registrada.'}
            pageSizeOptions={PAGE_SIZES}
            filterValues={[entityInput.trim(), actionInput.trim(), fromDate, toDate]}
            filters={(
              <>
                <DataTableFilter label="Entidad" htmlFor="auditlogs-entidad">
                  <input
                    id="auditlogs-entidad"
                    type="text"
                    className="form-control form-control-sm"
                    placeholder="Nombre exacto"
                    value={entityInput}
                    onChange={(e) => setEntityInput(e.target.value)}
                  />
                </DataTableFilter>
                <DataTableFilter label="Acción" htmlFor="auditlogs-accion">
                  <input
                    id="auditlogs-accion"
                    type="text"
                    className="form-control form-control-sm"
                    placeholder="Nombre exacto"
                    value={actionInput}
                    onChange={(e) => setActionInput(e.target.value)}
                  />
                </DataTableFilter>
                <DataTableFilter label="Desde" htmlFor="auditlogs-desde">
                  <input
                    id="auditlogs-desde"
                    type="date"
                    className="form-control form-control-sm"
                    value={fromDate}
                    onChange={(e) => {
                      setFromDate(e.target.value);
                      setPageNumber(1);
                    }}
                  />
                </DataTableFilter>
                <DataTableFilter label="Hasta" htmlFor="auditlogs-hasta">
                  <input
                    id="auditlogs-hasta"
                    type="date"
                    className="form-control form-control-sm"
                    value={toDate}
                    onChange={(e) => {
                      setToDate(e.target.value);
                      setPageNumber(1);
                    }}
                  />
                </DataTableFilter>
                <button
                  type="button"
                  className="btn btn-light btn-sm"
                  onClick={() => {
                    setEntityInput('');
                    setActionInput('');
                    setEntityName('');
                    setAction('');
                    setFromDate('');
                    setToDate('');
                    setPageNumber(1);
                  }}
                >
                  Limpiar filtros
                </button>
              </>
            )}
            serverSide={{
              totalCount: data.totalCount,
              page: pageNumber,
              pageSize,
              onPageChange: setPageNumber,
              onPageSizeChange: (size) => {
                setPageSize(size);
                setPageNumber(1);
              },
              onSearchChange: (term) => {
                setSearch(term);
                setPageNumber(1);
              },
            }}
            searchPlaceholder="Qué cambió, acción o quién"

            renderRow={(item) => (
              <>
                <td>{toLocalDateTime(item.timestamp)}</td>
                <td>{item.entityName}</td>
                <td>{item.action}</td>
                <td className="text-muted">{item.entityId}</td>
                <td>{item.performedBy}</td>
              </>
            )}
          />
        </div>
      </div>
    </>
  );
}
