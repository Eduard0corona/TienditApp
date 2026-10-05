import { useCallback, useEffect, useState } from 'react';
import PageTitle from '../../components/common/PageTitle';
import DataTable from '../../components/common/DataTable';
import { auditApi } from '../../api/auditApi';
import type { AuditLogDto, PaginatedList } from '../../types';
import { formatInBusinessZone, businessDayEnd, businessDayStart } from '../../utils/dates';

const COLUMNS = [
  { header: 'Fecha' },
  { header: 'Entidad' },
  { header: 'Acción' },
  { header: 'Id entidad' },
  { header: 'Usuario' },
];

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

export default function AuditLogs() {
  const [data, setData] = useState<PaginatedList<AuditLogDto>>({
    items: [],
    pageNumber: 1,
    totalPages: 0,
    totalCount: 0,
    hasPreviousPage: false,
    hasNextPage: false,
  });
  const [entityName, setEntityName] = useState('');
  const [action, setAction] = useState('');
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [search, setSearch] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const loadData = useCallback(async () => {
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
      setData(response);
    } catch {
      setError('No se pudo cargar la actividad del negocio.');
    } finally {
      setLoading(false);
    }
  }, [action, entityName, fromDate, pageNumber, pageSize, search, toDate]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  return (
    <>
      <PageTitle title="Seguridad y actividad" subtitle="Configuración" />

      <div className="card">
        <div className="card-body">
          <div className="row g-2 mb-3">
            <div className="col-md-3">
              <label className="form-label">Entidad</label>
              <input
                type="text"
                className="form-control form-control-sm"
                value={entityName}
                onChange={(e) => {
                  setEntityName(e.target.value);
                  setPageNumber(1);
                }}
              />
            </div>
            <div className="col-md-3">
              <label className="form-label">Acción</label>
              <input
                type="text"
                className="form-control form-control-sm"
                value={action}
                onChange={(e) => {
                  setAction(e.target.value);
                  setPageNumber(1);
                }}
              />
            </div>
            <div className="col-md-2">
              <label className="form-label">Desde</label>
              <input
                type="date"
                className="form-control form-control-sm"
                value={fromDate}
                onChange={(e) => {
                  setFromDate(e.target.value);
                  setPageNumber(1);
                }}
              />
            </div>
            <div className="col-md-2">
              <label className="form-label">Hasta</label>
              <input
                type="date"
                className="form-control form-control-sm"
                value={toDate}
                onChange={(e) => {
                  setToDate(e.target.value);
                  setPageNumber(1);
                }}
              />
            </div>
            <div className="col-md-2 d-flex align-items-end">
              <button
                type="button"
                className="btn btn-light btn-sm w-100"
                onClick={() => {
                  setEntityName('');
                  setAction('');
                  setFromDate('');
                  setToDate('');
                  setPageNumber(1);
                }}
              >
                Limpiar
              </button>
            </div>
          </div>

          {error && (
            <div className="alert alert-danger alert-dismissible fade show">
              {error}
              <button type="button" className="btn-close" onClick={() => setError('')}></button>
            </div>
          )}

          <DataTable
            data={data.items}
            columns={COLUMNS}
            keyExtractor={(item) => item.id}
            loading={loading}
            emptyMessage="No hay actividad registrada con estos filtros."
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
