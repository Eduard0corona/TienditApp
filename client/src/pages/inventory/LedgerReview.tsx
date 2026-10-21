import { useEffect, useRef, useState } from 'react';
import { getApiErrorMessage } from '../../api/errors';
import {
  INITIAL_RECONCILIATION_FILTER,
  useInventoryReconciliation,
  useReconcileInventoryLedger,
  type InventoryReconciliationFilter,
  type InventoryReconciliationItem,
} from '../../api/inventoryReconciliation';
import DataTable, { DataTableFilter } from '../../components/common/DataTable';
import { useConfirm } from '../../hooks/useConfirm';
import type { InventoryLocationDto } from '../../types';
import { formatStockQuantity } from '../../utils/units';
import LedgerGapModal from './LedgerGapModal';
import { formatSignedQuantity } from './ledgerGaps';

// On a phone the item (with its location), the difference and the actions: the stock and the history from md up.
const WIDE = 'd-none d-md-table-cell';

const COLUMNS = [
  { header: 'Artículo' },
  { header: 'Ubicación', className: WIDE },
  { header: 'Existencia', className: `text-end ${WIDE}` },
  { header: 'El historial suma', className: `text-end ${WIDE}` },
  { header: 'Diferencia', className: 'text-end' },
  { header: 'Acciones', className: 'text-center' },
];

interface LedgerReviewProps {
  locations: InventoryLocationDto[];
  onBack: () => void;
  /** A reconciliation recorded something: the message, and the history is read again. */
  onReconciled: (message: string) => void;
}

/**
 * Revisar que cuadre (Fase 27C, ADR 0039): the stocks that are not what their movements add up to, why, and recording
 * the difference in the history without moving the stock.
 */
export default function LedgerReview({ locations, onBack, onReconciled }: LedgerReviewProps) {
  const [filter, setFilter] = useState<InventoryReconciliationFilter>(INITIAL_RECONCILIATION_FILTER);
  const [openItem, setOpenItem] = useState<InventoryReconciliationItem | null>(null);
  const [error, setError] = useState('');
  const review = useInventoryReconciliation(filter);
  const reconcile = useReconcileInventoryLedger();
  const { confirm } = useConfirm();
  const headingRef = useRef<HTMLHeadingElement>(null);
  const items = review.data?.mismatches.items ?? [];
  const location = locations.find((l) => l.id === filter.locationId);
  const change = (patch: Partial<InventoryReconciliationFilter>) => setFilter((current) => ({ ...current, page: 1, ...patch }));

  // The screen changes under the button that opened it: the focus goes to the review.
  useEffect(() => {
    headingRef.current?.focus();
  }, []);

  const run = async (toReconcile: InventoryReconciliationItem[]) => {
    const [first] = toReconcile;
    const single = toReconcile.length === 1;
    const accepted = await confirm({
      title: 'Cuadrar el historial',
      message: single
        ? `Se anota ${formatSignedQuantity(first.difference, first.unit)} en el historial de ${first.supplyItemName} en ${first.inventoryLocationName}, para que sume ${formatStockQuantity(first.onHandQuantity, first.unit)}. La existencia no cambia: si no sabes cuánto hay de verdad, cuéntalo en «Conteos».`
        : `Se anota en el historial de estos ${toReconcile.length} artículos de ${first.inventoryLocationName} lo que le falta para sumar su existencia. Las existencias no cambian: si no sabes cuánto hay de verdad, cuéntalo en «Conteos».`,
      confirmLabel: 'Cuadrar',
    });
    if (!accepted) return;

    setError('');
    try {
      const result = await reconcile.mutateAsync({
        inventoryLocationId: first.inventoryLocationId,
        lines: toReconcile.map((item) => ({ supplyItemId: item.supplyItemId, expectedDifference: item.difference })),
      });
      setOpenItem(null);
      onReconciled(result.reconciledCount === 0
        ? 'Ya cuadraba: no se anotó nada.'
        : `Listo: el historial de ${result.reconciledCount === 1 ? first.supplyItemName : `${result.reconciledCount} artículos`} ya suma su existencia (${result.movements.map((m) => m.movementNumber).join(' y ')}).`);
    } catch (err: unknown) {
      // What does not add up changed since it was read (409), or the request failed: the review is read again.
      setError(getApiErrorMessage(err, 'No se pudo cuadrar el historial. Vuelve a intentarlo.'));
    }
  };

  const total = review.data?.mismatches.totalCount ?? 0;
  const checked = review.data?.checkedCount ?? 0;
  const filtered = filter.search.trim() !== '';

  return (
    <div>
      <button type="button" className="btn btn-link px-0" onClick={onBack}>
        <i className="ri-arrow-left-line me-1" aria-hidden="true"></i>Historial por insumo
      </button>
      <h2 className="h5 mb-1" ref={headingRef} tabIndex={-1}>¿Cuadra el historial con las existencias?</h2>
      <p className="text-muted small mb-3">
        Cada existencia debe ser lo que suman sus entradas y salidas. Si algo la cambió sin un movimiento, aquí aparece con
        la diferencia y por qué. Cuadrar anota esa diferencia en el historial; la existencia no cambia.
      </p>
      {error && <div className="alert alert-danger" role="alert">{error}</div>}
      {review.isError && (
        <div className="alert alert-danger" role="alert">{getApiErrorMessage(review.error, 'No se pudo revisar el historial.')}</div>
      )}
      {review.isSuccess && (
        total === 0 && !filtered ? (
          <div className="alert alert-success" role="status">
            Todo cuadra: {checked === 1 ? 'la existencia revisada es' : `las ${checked.toLocaleString('es-MX')} existencias revisadas son`} lo que suman sus movimientos.
          </div>
        ) : (
          <p className="mb-2" role="status">
            Se revisaron {checked.toLocaleString('es-MX')} existencias{location ? ` de ${location.name}` : ''}:{' '}
            {total === 1 ? '1 no cuadra' : `${total.toLocaleString('es-MX')} no cuadran`} con su historial.
          </p>
        )
      )}
      <DataTable
        data={items}
        columns={COLUMNS}
        keyExtractor={(item) => `${item.supplyItemId}:${item.inventoryLocationId}`}
        loading={review.isLoading}
        emptyMessage={filtered ? 'Nada que no cuadre coincide con la búsqueda.' : 'Todo cuadra.'}
        searchPlaceholder="Buscar artículo o ubicación"
        filters={(
          <DataTableFilter label="Ubicación" htmlFor="cuadre-ubicacion">
            <select
              id="cuadre-ubicacion"
              className="form-select form-select-sm"
              value={filter.locationId}
              onChange={(e) => change({ locationId: e.target.value })}
            >
              <option value="">Todas</option>
              {locations.map((l) => (
                <option key={l.id} value={l.id}>{l.name}</option>
              ))}
            </select>
          </DataTableFilter>
        )}
        toolbarEnd={location && items.length > 1 ? (
          <button type="button" className="btn btn-sm btn-primary text-nowrap" onClick={() => run(items)} disabled={reconcile.isPending}>
            Cuadrar las {items.length} que se ven
          </button>
        ) : undefined}
        serverSide={{
          totalCount: total,
          page: filter.page,
          pageSize: filter.pageSize,
          onPageChange: (page) => setFilter((current) => ({ ...current, page })),
          onPageSizeChange: (pageSize) => change({ pageSize }),
          onSearchChange: (search) => change({ search }),
        }}
        initialSearch={filter.search}
        renderRow={(item) => (
          <>
            <td>
              <div className="text-wrap">
                {item.supplyItemName}
                {!item.isActive && <span className="badge bg-secondary ms-1">Inactivo</span>}
              </div>
              <div className="text-muted small text-wrap d-md-none">{item.inventoryLocationName}</div>
            </td>
            <td className={`text-wrap ${WIDE}`}>{item.inventoryLocationName}</td>
            <td className={`text-end text-nowrap ${WIDE}`}>{formatStockQuantity(item.onHandQuantity, item.unit)}</td>
            <td className={`text-end text-nowrap ${WIDE}`}>{formatStockQuantity(item.historyQuantity, item.unit)}</td>
            <td className="text-end text-nowrap fw-semibold">{formatSignedQuantity(item.difference, item.unit)}</td>
            <td className="text-center">
              <div className="d-flex flex-wrap justify-content-center gap-1">
                <button
                  type="button"
                  className="btn btn-sm btn-light text-nowrap"
                  aria-label={`Por qué no cuadra ${item.supplyItemName} en ${item.inventoryLocationName}`}
                  onClick={() => setOpenItem(item)}
                >
                  Por qué
                </button>
                <button
                  type="button"
                  className="btn btn-sm btn-light text-nowrap"
                  aria-label={`Cuadrar ${item.supplyItemName} en ${item.inventoryLocationName}`}
                  onClick={() => run([item])}
                  disabled={reconcile.isPending}
                >
                  Cuadrar
                </button>
              </div>
            </td>
          </>
        )}
      />
      {openItem && (
        <LedgerGapModal
          key={`${openItem.supplyItemId}:${openItem.inventoryLocationId}`}
          item={openItem}
          reconciling={reconcile.isPending}
          onHide={() => setOpenItem(null)}
          onReconcile={(item) => run([item])}
        />
      )}
    </div>
  );
}
