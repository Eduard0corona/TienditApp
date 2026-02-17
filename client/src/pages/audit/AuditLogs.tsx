import { useCallback, useEffect, useState } from 'react';
import PageTitle from '../../components/common/PageTitle';
import DataTable from '../../components/common/DataTable';
import { auditApi } from '../../api/auditApi';
import type { AuditLogDto, PaginatedList } from '../../types';

const COLUMNS = [
  { header: 'Fecha' },
  { header: 'Entidad' },
  { header: 'Accion' },
  { header: 'Id entidad' },
  { header: 'Usuario' },
];

function toLocalDateTime(value: string) {
  return new Date(value).toLocaleString('es-MX', {
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
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const loadData = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const { data: response } = await auditApi.getLogs({
        entityName: entityName || undefined,
        action: action || undefined,
        from: fromDate ? new Date(`${fromDate}T00:00:00`).toISOString() : undefined,
        to: toDate ? new Date(`${toDate}T23:59:59`).toISOString() : undefined,
        pageNumber,
        pageSize,
      });
      setData(response);
    } catch {
      setError('No se pudieron cargar los registros de auditoria.');
    } finally {
      setLoading(false);
    }
  }, [action, entityName, fromDate, pageNumber, pageSize, toDate]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  return (
    <>
      <PageTitle title="Auditoria" subtitle="Seguridad" />

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
              <label className="form-label">Accion</label>
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
            emptyMessage="No hay registros de auditoria."
            searchFilter={(item, search) =>
              item.entityName.toLowerCase().includes(search)
              || item.action.toLowerCase().includes(search)
              || item.performedBy.toLowerCase().includes(search)
              || item.entityId.toLowerCase().includes(search)
            }
            serverSide={{
              totalCount: data.totalCount,
              page: pageNumber,
              pageSize,
              onPageChange: setPageNumber,
              onPageSizeChange: (size) => {
                setPageSize(size);
                setPageNumber(1);
              },
            }}
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
