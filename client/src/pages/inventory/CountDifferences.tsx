import type { UseQueryResult } from '@tanstack/react-query';
import type { Dispatch, SetStateAction } from 'react';
import { getApiErrorMessage } from '../../api/errors';
import type { InventoryCountLine, InventoryCountLinesFilter, InventoryCountLinesPage } from '../../api/inventoryCounts';
import DataTable from '../../components/common/DataTable';
import { InventoryCountStatus } from '../../types';
import { formatSignedMoney } from '../../utils/money';
import { formatStockQuantity } from '../../utils/units';
import { formatDifference } from './countEntry';

// On a phone the item, what was counted and the difference: the rest from md up. The action of an open count goes under
// the item there.
const WIDE = 'd-none d-md-table-cell';

interface CountDifferencesProps {
  status: InventoryCountStatus;
  lines: UseQueryResult<InventoryCountLinesPage>;
  filter: InventoryCountLinesFilter;
  onFilterChange: Dispatch<SetStateAction<InventoryCountLinesFilter>>;
  /** Every item, not only those with a difference (a cancelled count). */
  showAll?: boolean;
  /** While open: clears what was counted of an item, so it is counted again against the system of then. */
  onRecount?: (line: InventoryCountLine) => void;
  /** The item being cleared. */
  recountingId?: string;
}

const quantity = (value: number | null | undefined, unit: Parameters<typeof formatStockQuantity>[1]) =>
  value === null || value === undefined ? '—' : formatStockQuantity(value, unit);

/**
 * What each item was counted against and what it ends at: while open, the stock now plus the difference (what was sold
 * after counting is kept); once posted, what the count moved. The API works it out; this only shows it.
 */
export default function CountDifferences({ status, lines, filter, onFilterChange, showAll = false, onRecount, recountingId }: CountDifferencesProps) {
  const posted = status === InventoryCountStatus.Posted;
  const cancelled = status === InventoryCountStatus.Cancelled;
  const columns = [
    { header: 'Artículo' },
    { header: 'Sistema al contar', className: `text-end ${WIDE}` },
    { header: 'Contado', className: 'text-end' },
    { header: 'Diferencia', className: 'text-end' },
    ...(cancelled ? [] : [
      { header: posted ? 'Antes' : 'Ahora', className: `text-end ${WIDE}` },
      { header: posted ? 'Quedó en' : 'Quedará en', className: `text-end ${WIDE}` },
      { header: 'Valor', className: `text-end ${WIDE}` },
    ]),
    ...(onRecount ? [{ header: 'Acciones', className: `text-center ${WIDE}` }] : []),
  ];

  return (
    <>
      {lines.isError && (
        <div className="alert alert-danger" role="alert">{getApiErrorMessage(lines.error, 'Error al cargar las diferencias.')}</div>
      )}
      <DataTable
        data={lines.data?.items ?? []}
        columns={columns}
        keyExtractor={(item) => item.id}
        loading={lines.isLoading}
        emptyMessage={showAll ? 'El conteo no tiene artículos.' : 'Ningún artículo contado tiene diferencia.'}
        searchPlaceholder="Buscar artículo"
        serverSide={{
          totalCount: lines.data?.totalCount ?? 0,
          page: filter.page,
          pageSize: filter.pageSize,
          onPageChange: (page) => onFilterChange((current) => ({ ...current, page })),
          onPageSizeChange: (pageSize) => onFilterChange((current) => ({ ...current, page: 1, pageSize })),
          onSearchChange: (search) => onFilterChange((current) => ({ ...current, page: 1, search })),
        }}
        pageSizeOptions={[50, 100, 200]}
        initialSearch={filter.search}
        renderRow={(line) => {
          const difference = line.difference ?? null;
          const recountButton = (className: string) => onRecount && (
            <button
              type="button"
              className={`btn btn-sm ${className}`}
              disabled={recountingId !== undefined}
              aria-label={`Volver a contar ${line.supplyItemName}`}
              onClick={() => onRecount(line)}
            >
              {recountingId === line.id ? 'Borrando…' : 'Volver a contar'}
            </button>
          );
          return (
            <>
              <td>
                <div className="fw-semibold text-wrap">{line.supplyItemName}</div>
                {line.countedBy && <div className="text-muted small text-wrap text-break">Contó {line.countedBy}</div>}
                {/* On a phone a link that wraps, so the row fits beside what was counted and its difference. */}
                {recountButton('btn-link p-0 mt-1 text-start d-md-none')}
              </td>
              <td className={`text-end ${WIDE}`}>{quantity(line.systemQuantity, line.unit)}</td>
              <td className="text-end">{quantity(line.countedQuantity, line.unit)}</td>
              <td className={`text-end fw-semibold${difference !== null && difference < 0 ? ' text-danger' : ''}`}>
                {difference === null ? 'Sin contar' : formatDifference(difference, line.unit)}
              </td>
              {!cancelled && (
                <>
                  <td className={`text-end ${WIDE}`}>{quantity(line.currentQuantity, line.unit)}</td>
                  <td className={`text-end ${WIDE}`}>{quantity(line.resultingQuantity, line.unit)}</td>
                  <td className={`text-end ${WIDE}`}>
                    {line.differenceValue === null || line.differenceValue === undefined ? '—' : formatSignedMoney(line.differenceValue)}
                  </td>
                </>
              )}
              {onRecount && <td className={`text-center ${WIDE}`}>{recountButton('btn-light text-nowrap')}</td>}
            </>
          );
        }}
      />
    </>
  );
}
