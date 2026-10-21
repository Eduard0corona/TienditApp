import { useState } from 'react';
import { getApiErrorMessage } from '../../api/errors';
import { useInventoryCountLines, type InventoryCountLinesFilter } from '../../api/inventoryCounts';
import DataTable from '../../components/common/DataTable';
import { InventoryCountStatus } from '../../types';
import { formatSignedMoney } from '../../utils/money';
import { formatStockQuantity } from '../../utils/units';
import { formatDifference } from './countEntry';

interface CountDifferencesProps {
  countId: string;
  status: InventoryCountStatus;
  /** Every item, not only those with a difference (a cancelled count). */
  showAll?: boolean;
}

const quantity = (value: number | null | undefined, unit: Parameters<typeof formatStockQuantity>[1]) =>
  value === null || value === undefined ? '—' : formatStockQuantity(value, unit);

/**
 * What each item was counted against and what it ends at: while open, the stock now plus the difference (what was sold
 * after counting is kept); once posted, what the count moved. The API works it out; this only shows it.
 */
export default function CountDifferences({ countId, status, showAll = false }: CountDifferencesProps) {
  const [filter, setFilter] = useState<InventoryCountLinesFilter>({
    search: '', pending: false, withDifference: !showAll, page: 1, pageSize: 50,
  });
  const lines = useInventoryCountLines(countId, filter);
  const posted = status === InventoryCountStatus.Posted;
  const columns = [
    { header: 'Artículo' },
    { header: 'Sistema al contar', className: 'text-end' },
    { header: 'Contado', className: 'text-end' },
    { header: 'Diferencia', className: 'text-end' },
    ...(status === InventoryCountStatus.Cancelled ? [] : [
      { header: posted ? 'Antes' : 'Ahora', className: 'text-end' },
      { header: posted ? 'Quedó en' : 'Quedará en', className: 'text-end' },
      { header: 'Valor', className: 'text-end' },
    ]),
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
          onPageChange: (page) => setFilter((current) => ({ ...current, page })),
          onPageSizeChange: (pageSize) => setFilter((current) => ({ ...current, page: 1, pageSize })),
          onSearchChange: (search) => setFilter((current) => ({ ...current, page: 1, search })),
        }}
        pageSizeOptions={[50, 100, 200]}
        renderRow={(line) => {
          const difference = line.difference ?? null;
          return (
            <>
              <td>
                <div className="fw-semibold">{line.supplyItemName}</div>
                {line.countedBy && <div className="text-muted small">Contó {line.countedBy}</div>}
              </td>
              <td className="text-end">{quantity(line.systemQuantity, line.unit)}</td>
              <td className="text-end">{quantity(line.countedQuantity, line.unit)}</td>
              <td className={`text-end fw-semibold${difference !== null && difference < 0 ? ' text-danger' : ''}`}>
                {difference === null ? 'Sin contar' : formatDifference(difference, line.unit)}
              </td>
              {status !== InventoryCountStatus.Cancelled && (
                <>
                  <td className="text-end">{quantity(line.currentQuantity, line.unit)}</td>
                  <td className="text-end">{quantity(line.resultingQuantity, line.unit)}</td>
                  <td className="text-end">
                    {line.differenceValue === null || line.differenceValue === undefined ? '—' : formatSignedMoney(line.differenceValue)}
                  </td>
                </>
              )}
            </>
          );
        }}
      />
    </>
  );
}
