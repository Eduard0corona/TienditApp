import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { getApiErrorMessage } from '../../api/errors';
import {
  useInventoryCountLines,
  useRecordInventoryCount,
  type InventoryCountLine,
  type InventoryCountLinesFilter,
} from '../../api/inventoryCounts';
import DataTable, { DataTableFilter } from '../../components/common/DataTable';
import { getUnitLabel } from '../../utils/units';
import { parseCountedQuantity } from './countEntry';

// Two columns, so the box fits beside the item on a phone: the unit goes under the name.
const COLUMNS = [
  { header: 'Artículo' },
  { header: 'Contado', width: '9rem' },
];

type LineState = { kind: 'saving' } | { kind: 'saved' } | { kind: 'error'; message: string };

function without<T>(record: Record<string, T>, key: string): Record<string, T> {
  const rest = { ...record };
  delete rest[key];
  return rest;
}

interface CountSheetProps {
  countId: string;
}

/**
 * The sheet of an open count (Fase 27A): each item with a box for what is on the shelf, saved when the box is left or
 * with Enter (which goes to the next one). It does not show what the system has: counting blind, nobody copies it. A
 * scanned code that matches one item puts the cursor in its box.
 */
export default function CountSheet({ countId }: CountSheetProps) {
  const [filter, setFilter] = useState<InventoryCountLinesFilter>({ search: '', pending: false, withDifference: false, page: 1, pageSize: 50 });
  const lines = useInventoryCountLines(countId, filter);
  const record = useRecordInventoryCount(countId);
  // What was typed and not saved yet, what the API answered for each saved item, and how each save went.
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [saved, setSaved] = useState<Record<string, number | null>>({});
  const [states, setStates] = useState<Record<string, LineState>>({});
  const tableRef = useRef<HTMLDivElement>(null);
  const items = useMemo(() => lines.data?.items ?? [], [lines.data]);

  const counted = (line: InventoryCountLine) => (line.id in saved ? saved[line.id] : line.countedQuantity ?? null);
  const textOf = (line: InventoryCountLine) => {
    if (line.id in drafts) return drafts[line.id];
    const value = counted(line);
    return value === null ? '' : String(value);
  };

  // A scanned code that is exactly one item's: straight to its box.
  useEffect(() => {
    const term = filter.search.trim().toLowerCase();
    if (!term || items.length !== 1) return;
    const [line] = items;
    if (![line.supplyCode, line.supplySku, line.supplyBarcode].some((code) => code?.toLowerCase() === term)) return;
    tableRef.current?.querySelector<HTMLInputElement>(`[data-count-line="${line.id}"]`)?.focus();
  }, [filter.search, items]);

  const save = async (line: InventoryCountLine) => {
    if (!(line.id in drafts)) return;
    const parsed = parseCountedQuantity(drafts[line.id], line.unit);
    if ('error' in parsed) {
      setStates((current) => ({ ...current, [line.id]: { kind: 'error', message: parsed.error } }));
      return;
    }
    if (parsed.value === counted(line)) {
      setDrafts((current) => without(current, line.id));
      setStates((current) => without(current, line.id));
      return;
    }

    setStates((current) => ({ ...current, [line.id]: { kind: 'saving' } }));
    try {
      const result = await record.mutateAsync([{ lineId: line.id, countedQuantity: parsed.value }]);
      const answered = result.lines.find((item) => item.id === line.id);
      setSaved((current) => ({ ...current, [line.id]: answered?.countedQuantity ?? null }));
      setDrafts((current) => without(current, line.id));
      setStates((current) => ({ ...current, [line.id]: { kind: 'saved' } }));
    } catch (err: unknown) {
      setStates((current) => ({ ...current, [line.id]: { kind: 'error', message: getApiErrorMessage(err, 'No se pudo guardar.') } }));
    }
  };

  const next = (event: KeyboardEvent<HTMLInputElement>, line: InventoryCountLine) => {
    if (event.key !== 'Enter') return;
    event.preventDefault();
    void save(line);
    const boxes = [...(tableRef.current?.querySelectorAll<HTMLInputElement>('[data-count-line]') ?? [])];
    boxes[boxes.indexOf(event.currentTarget) + 1]?.focus();
  };

  return (
    <div ref={tableRef}>
      {lines.isError && (
        <div className="alert alert-danger" role="alert">{getApiErrorMessage(lines.error, 'Error al cargar los artículos del conteo.')}</div>
      )}
      <DataTable
        data={items}
        columns={COLUMNS}
        keyExtractor={(item) => item.id}
        loading={lines.isLoading}
        emptyMessage={filter.pending ? 'Ya contaste todos los artículos.' : 'El conteo no tiene artículos.'}
        searchPlaceholder="Buscar o escanear un código"
        filters={(
          <DataTableFilter label="Mostrar" htmlFor="conteo-mostrar">
            <select
              id="conteo-mostrar"
              className="form-select form-select-sm"
              value={filter.pending ? 'pending' : 'all'}
              onChange={(e) => setFilter((current) => ({ ...current, page: 1, pending: e.target.value === 'pending' }))}
            >
              <option value="all">Todos</option>
              <option value="pending">Sin contar</option>
            </select>
          </DataTableFilter>
        )}
        serverSide={{
          totalCount: lines.data?.totalCount ?? 0,
          page: filter.page,
          pageSize: filter.pageSize,
          onPageChange: (page) => setFilter((current) => ({ ...current, page })),
          onPageSizeChange: (pageSize) => setFilter((current) => ({ ...current, page: 1, pageSize })),
          onSearchChange: (search) => setFilter((current) => ({ ...current, page: 1, search })),
        }}
        pageSizeOptions={[50, 100, 200]}
        initialSearch={filter.search}
        renderRow={(line) => {
          const state = states[line.id];
          const inputId = `conteo-cantidad-${line.id}`;
          return (
            <>
              <td>
                <label htmlFor={inputId} className="fw-semibold mb-0 text-wrap">{line.supplyItemName}</label>
                <div className="text-muted small text-wrap">
                  {[getUnitLabel(line.unit), line.supplyCode, line.supplyBarcode, line.categoryName].filter(Boolean).join(' · ')}
                  {!line.isSupplyActive && ' · Inactivo'}
                </div>
              </td>
              <td>
                <input
                  id={inputId}
                  data-count-line={line.id}
                  type="text"
                  inputMode="decimal"
                  autoComplete="off"
                  className={`form-control form-control-sm${state?.kind === 'error' ? ' is-invalid' : ''}`}
                  placeholder="Sin contar"
                  value={textOf(line)}
                  aria-invalid={state?.kind === 'error'}
                  aria-describedby={state ? `${inputId}-estado` : undefined}
                  onChange={(e) => {
                    const { value } = e.target;
                    setDrafts((current) => ({ ...current, [line.id]: value }));
                  }}
                  onBlur={() => void save(line)}
                  onKeyDown={(e) => next(e, line)}
                />
                {state && (
                  <div
                    id={`${inputId}-estado`}
                    className={`small mt-1 ${state.kind === 'error' ? 'text-danger' : 'text-muted'}`}
                    role={state.kind === 'error' ? 'alert' : 'status'}
                  >
                    {state.kind === 'saving' && 'Guardando…'}
                    {state.kind === 'saved' && (
                      <><i className="ri-check-line me-1" aria-hidden="true"></i>Guardado</>
                    )}
                    {state.kind === 'error' && state.message}
                  </div>
                )}
              </td>
            </>
          );
        }}
      />
    </div>
  );
}
