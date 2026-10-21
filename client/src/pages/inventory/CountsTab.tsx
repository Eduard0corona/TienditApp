import type { Dispatch, SetStateAction } from 'react';
import { getApiErrorMessage } from '../../api/errors';
import { useInventoryCounts, type InventoryCountsFilter } from '../../api/inventoryCounts';
import DataTable, { DataTableFilter } from '../../components/common/DataTable';
import { InventoryCountStatus, type InventoryLocationDto } from '../../types';
import { COUNT_STATUS_OPTIONS, formatDateTime, getCountStatusChip, getCountStatusLabel } from './inventoryLabels';

const COLUMNS = [
  { header: 'Folio' },
  { header: 'Ubicación' },
  { header: 'Estado', className: 'text-center' },
  { header: 'Contados', className: 'text-center' },
  { header: 'Empezó' },
  { header: 'Acciones', className: 'text-center' },
];

interface CountsTabProps {
  locations: InventoryLocationDto[];
  /** Kept by the page, so opening a count and coming back finds the list as it was. */
  filter: InventoryCountsFilter;
  onFilterChange: Dispatch<SetStateAction<InventoryCountsFilter>>;
  onOpen: (countId: string) => void;
}

/** The physical counts of the business (Fase 27A): open ones to keep counting, and those posted or cancelled. */
export default function CountsTab({ locations, filter, onFilterChange: setFilter, onOpen }: CountsTabProps) {
  const counts = useInventoryCounts(filter);
  const filtered = filter.status !== '' || filter.locationId !== '' || filter.search.trim() !== '';
  const change = (patch: Partial<InventoryCountsFilter>) => setFilter((current) => ({ ...current, page: 1, ...patch }));

  return (
    <>
      {counts.isError && (
        <div className="alert alert-danger" role="alert">{getApiErrorMessage(counts.error, 'Error al cargar los conteos.')}</div>
      )}
      <DataTable
        data={counts.data?.items ?? []}
        columns={COLUMNS}
        keyExtractor={(item) => item.id}
        loading={counts.isLoading}
        emptyMessage={filtered
          ? 'Ningún conteo coincide con los filtros.'
          : 'Todavía no hay conteos. Empieza uno para cuadrar lo que hay en el anaquel con lo que dice el sistema.'}
        searchPlaceholder="Buscar por folio o ubicación"
        filters={(
          <>
            <DataTableFilter label="Estado" htmlFor="conteos-estado">
              <select
                id="conteos-estado"
                className="form-select form-select-sm"
                value={filter.status}
                onChange={(e) => change({ status: e.target.value === '' ? '' : Number(e.target.value) as InventoryCountStatus })}
              >
                <option value="">Todos</option>
                {COUNT_STATUS_OPTIONS.map((option) => (
                  <option key={option.value} value={option.value}>{option.label}</option>
                ))}
              </select>
            </DataTableFilter>
            <DataTableFilter label="Ubicación" htmlFor="conteos-ubicacion">
              <select
                id="conteos-ubicacion"
                className="form-select form-select-sm"
                value={filter.locationId}
                onChange={(e) => change({ locationId: e.target.value })}
              >
                <option value="">Todas</option>
                {locations.map((location) => (
                  <option key={location.id} value={location.id}>{location.name}</option>
                ))}
              </select>
            </DataTableFilter>
          </>
        )}
        serverSide={{
          totalCount: counts.data?.totalCount ?? 0,
          page: filter.page,
          pageSize: filter.pageSize,
          onPageChange: (page) => setFilter((current) => ({ ...current, page })),
          onPageSizeChange: (pageSize) => change({ pageSize }),
          onSearchChange: (search) => change({ search }),
        }}
        initialSearch={filter.search}
        renderRow={(item) => {
          const open = item.status === InventoryCountStatus.InProgress;
          return (
            <>
              <td className="fw-semibold">{item.countNumber}</td>
              <td>
                <div>{item.inventoryLocationName}</div>
                {item.categoryName && <div className="text-muted small">{item.categoryName}</div>}
              </td>
              <td className="text-center">
                <span className={`ta-chip ${getCountStatusChip(item.status)}`}>{getCountStatusLabel(item.status)}</span>
              </td>
              <td className="text-center">{item.countedLineCount} de {item.lineCount}</td>
              <td>
                <div>{formatDateTime(item.startedAt)}</div>
                <div className="text-muted small">{item.startedBy}</div>
              </td>
              <td className="text-center">
                <button
                  type="button"
                  className="btn btn-sm btn-light"
                  aria-label={`${open ? 'Contar' : 'Ver'} el conteo ${item.countNumber}`}
                  onClick={() => onOpen(item.id)}
                >
                  {open ? 'Contar' : 'Ver'}
                </button>
              </td>
            </>
          );
        }}
      />
    </>
  );
}
