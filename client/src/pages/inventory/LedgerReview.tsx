import { useEffect, useRef, useState, type Dispatch, type SetStateAction } from 'react';
import { getApiErrorMessage } from '../../api/errors';
import {
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
  /** Kept by the page, so leaving the tab and coming back finds the review as it was. */
  filter: InventoryReconciliationFilter;
  onFilterChange: Dispatch<SetStateAction<InventoryReconciliationFilter>>;
  onBack: () => void;
  /** A reconciliation starts: the message of the previous one goes. */
  onStart: () => void;
  /** A reconciliation recorded something: the message, and the history is read again. */
  onReconciled: (message: string) => void;
}

const count = (n: number) => n.toLocaleString('es-MX');

/**
 * Revisar que cuadre (Fase 27C, ADR 0039): the stocks that are not what their movements add up to, why, and recording
 * the difference in the history without moving the stock.
 */
export default function LedgerReview({ locations, filter, onFilterChange: setFilter, onBack, onStart, onReconciled }: LedgerReviewProps) {
  const [openItem, setOpenItem] = useState<InventoryReconciliationItem | null>(null);
  const [error, setError] = useState('');
  const review = useInventoryReconciliation(filter);
  const reconcile = useReconcileInventoryLedger();
  const { confirm } = useConfirm();
  const headingRef = useRef<HTMLHeadingElement>(null);
  const items = review.data?.mismatches.items ?? [];
  const location = locations.find((l) => l.id === filter.locationId);
  const change = (patch: Partial<InventoryReconciliationFilter>) => {
    setError('');
    setFilter((current) => ({ ...current, page: 1, ...patch }));
  };
  // Only what this location shows now: never the rows of the previous filter while the new one loads.
  const bulk = location && !review.isPlaceholderData && !review.isFetching && items.length > 1
    && items.every((item) => item.inventoryLocationId === location.id)
    ? items
    : null;

  // The screen changes under the button that opened it: the focus goes to the review. Also after reconciling, once the
  // review is read again: the row of the button that was pressed is gone.
  const focusReview = useRef(true);
  useEffect(() => {
    if (!focusReview.current || review.isFetching) return;
    focusReview.current = false;
    headingRef.current?.focus();
  }, [review.isFetching]);

  const open = (item: InventoryReconciliationItem) => {
    setError('');
    setOpenItem(item);
  };

  const run = async (toReconcile: InventoryReconciliationItem[], locationId: string) => {
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
    onStart();
    try {
      const result = await reconcile.mutateAsync({
        inventoryLocationId: locationId,
        lines: toReconcile.map((item) => ({ supplyItemId: item.supplyItemId, expectedDifference: item.difference })),
      });
      setOpenItem(null);
      // What was reconciled leaves the list: back to its first page, with the focus on the review.
      setFilter((current) => ({ ...current, page: 1 }));
      focusReview.current = true;
      const recorded = result.reconciledCount;
      const what = single ? first.supplyItemName : recorded === 1 ? '1 artículo' : `${recorded} artículos`;
      onReconciled(recorded === 0
        ? (single ? 'Ya cuadraba: no se anotó nada.' : 'Ya cuadraban: no se anotó nada.')
        : `Listo: el historial de ${what} ya suma su existencia (${result.movements.map((m) => m.movementNumber).join(' y ')}).`);
    } catch (err: unknown) {
      // What does not add up changed since it was read (409), or the request failed: the review is read again. Said
      // where the person is: in the detail when it is open.
      setError(getApiErrorMessage(err, 'No se pudo cuadrar el historial. Vuelve a intentarlo.'));
    }
  };

  const total = review.data?.mismatches.totalCount ?? 0;
  const checked = review.data?.checkedCount ?? 0;
  const filtered = filter.search.trim() !== '';
  const where = location ? ` de ${location.name}` : '';

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
      {error && !openItem && <div className="alert alert-danger" role="alert">{error}</div>}
      {review.isError && (
        <div className="alert alert-danger" role="alert">{getApiErrorMessage(review.error, 'No se pudo revisar el historial.')}</div>
      )}
      {review.isSuccess && (
        checked === 0 ? (
          <p className="mb-2" role="status">No hay existencias que revisar{where}.</p>
        ) : filtered ? (
          <p className="mb-2" role="status">
            {total === 1 ? '1 que no cuadra coincide' : `${count(total)} que no cuadran coinciden`} con la búsqueda.
          </p>
        ) : total === 0 ? (
          <div className="alert alert-success" role="status">
            Todo cuadra: {checked === 1 ? 'la existencia revisada es' : `las ${count(checked)} existencias revisadas son`} lo que suman sus movimientos.
          </div>
        ) : (
          <p className="mb-2" role="status">
            {checked === 1 ? 'Se revisó 1 existencia' : `Se revisaron ${count(checked)} existencias`}{where}:{' '}
            {total === 1 ? '1 no cuadra' : `${count(total)} no cuadran`} con su historial.
          </p>
        )
      )}
      <DataTable
        data={items}
        columns={COLUMNS}
        keyExtractor={(item) => `${item.supplyItemId}:${item.inventoryLocationId}`}
        loading={review.isLoading}
        emptyMessage={review.isError ? 'No se pudo revisar el historial.' : filtered ? 'Nada que no cuadre coincide con la búsqueda.' : 'Todo cuadra.'}
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
        toolbarEnd={bulk && location ? (
          <button type="button" className="btn btn-sm btn-primary text-nowrap" onClick={() => run(bulk, location.id)} disabled={reconcile.isPending}>
            Cuadrar las {bulk.length} que se ven
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
                  onClick={() => open(item)}
                >
                  Por qué
                </button>
                <button
                  type="button"
                  className="btn btn-sm btn-light text-nowrap"
                  aria-label={`Cuadrar ${item.supplyItemName} en ${item.inventoryLocationName}`}
                  onClick={() => run([item], item.inventoryLocationId)}
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
          error={error}
          onHide={() => {
            setError('');
            setOpenItem(null);
          }}
          onReconcile={(item) => run([item], item.inventoryLocationId)}
        />
      )}
    </div>
  );
}
