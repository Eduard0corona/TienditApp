import type { Dispatch, SetStateAction } from 'react';
import { getApiErrorMessage } from '../../api/errors';
import { useInventoryTransfers, type InventoryTransfersFilter } from '../../api/inventoryTransfers';
import DataTable, { DataTableFilter } from '../../components/common/DataTable';
import type { InventoryLocationDto } from '../../types';
import { formatMoney } from '../../utils/money';
import { formatDateTime } from './inventoryLabels';

const COLUMNS = [
  { header: 'Folio' },
  { header: 'Origen y destino' },
  { header: 'Artículos', className: 'text-center' },
  { header: 'Valor', className: 'text-end' },
  { header: 'Cuándo' },
  { header: 'Acciones', className: 'text-center' },
];

interface TransfersTabProps {
  locations: InventoryLocationDto[];
  /** Kept by the page, so opening a transfer and coming back finds the list as it was. */
  filter: InventoryTransfersFilter;
  onFilterChange: Dispatch<SetStateAction<InventoryTransfersFilter>>;
  onOpen: (transferId: string) => void;
}

/** The transfers between locations of the business (Fase 27B), the newest first. */
export default function TransfersTab({ locations, filter, onFilterChange: setFilter, onOpen }: TransfersTabProps) {
  const transfers = useInventoryTransfers(filter);
  const filtered = filter.locationId !== '' || filter.search.trim() !== '';
  const change = (patch: Partial<InventoryTransfersFilter>) => setFilter((current) => ({ ...current, page: 1, ...patch }));

  return (
    <>
      {transfers.isError && (
        <div className="alert alert-danger" role="alert">{getApiErrorMessage(transfers.error, 'Error al cargar las transferencias.')}</div>
      )}
      <DataTable
        data={transfers.data?.items ?? []}
        columns={COLUMNS}
        keyExtractor={(item) => item.id}
        loading={transfers.isLoading}
        emptyMessage={filtered
          ? 'Ninguna transferencia coincide con los filtros.'
          : 'Todavía no hay transferencias. Haz una para llevar artículos de una ubicación a otra.'}
        searchPlaceholder="Buscar por folio o ubicación"
        filters={(
          <DataTableFilter label="Ubicación" htmlFor="transferencias-ubicacion">
            <select
              id="transferencias-ubicacion"
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
        )}
        serverSide={{
          totalCount: transfers.data?.totalCount ?? 0,
          page: filter.page,
          pageSize: filter.pageSize,
          onPageChange: (page) => setFilter((current) => ({ ...current, page })),
          onPageSizeChange: (pageSize) => change({ pageSize }),
          onSearchChange: (search) => change({ search }),
        }}
        initialSearch={filter.search}
        renderRow={(item) => (
          <>
            <td className="fw-semibold text-nowrap">{item.transferNumber}</td>
            <td>
              <div className="text-wrap">{item.fromLocationName}</div>
              <div className="text-muted small text-wrap">a {item.toLocationName}</div>
            </td>
            <td className="text-center">{item.lineCount}</td>
            <td className="text-end">{formatMoney(item.totalCost)}</td>
            <td>
              <div>{formatDateTime(item.transferredAt)}</div>
              <div className="text-muted small">{item.transferredBy}</div>
            </td>
            <td className="text-center">
              <button
                type="button"
                className="btn btn-sm btn-light"
                aria-label={`Ver la transferencia ${item.transferNumber}`}
                onClick={() => onOpen(item.id)}
              >
                Ver
              </button>
            </td>
          </>
        )}
      />
    </>
  );
}
