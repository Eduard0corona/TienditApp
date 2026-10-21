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

// How long «Guardado» stays under a box.
const SAVED_FOR_MS = 4000;

type LineState = { kind: 'saving' } | { kind: 'saved' } | { kind: 'error'; message: string };

function without<T>(record: Record<string, T>, key: string): Record<string, T> {
  const rest = { ...record };
  delete rest[key];
  return rest;
}

const isCode = (line: InventoryCountLine, term: string) =>
  [line.supplyCode, line.supplySku, line.supplyBarcode].some((code) => code?.toLowerCase() === term);

interface CountSheetProps {
  countId: string;
}

/**
 * The sheet of an open count (Fase 27A): each item with a box for what is on the shelf, saved when the box is left or
 * with Enter (which goes to the next one, or back to the search after a scan). It does not show what the system has:
 * counting blind, nobody copies it. A scanned code that is one item's puts the cursor in its box.
 */
export default function CountSheet({ countId }: CountSheetProps) {
  const [filter, setFilter] = useState<InventoryCountLinesFilter>({ search: '', pending: false, withDifference: false, page: 1, pageSize: 50 });
  const lines = useInventoryCountLines(countId, filter);
  const record = useRecordInventoryCount(countId);
  // What was typed and not saved yet, and how each save went. What was saved is in the items themselves.
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [states, setStates] = useState<Record<string, LineState>>({});
  // The text each item is saving: leaving the box after Enter does not save it twice.
  const saving = useRef<Record<string, string>>({});
  const timers = useRef<Record<string, number>>({});
  // Something was counted while showing what is left: the next page is read from the first one.
  const countedWhilePending = useRef(false);
  const tableRef = useRef<HTMLDivElement>(null);
  const items = useMemo(() => lines.data?.items ?? [], [lines.data]);

  useEffect(() => {
    const pending = timers.current;
    return () => Object.values(pending).forEach((timer) => window.clearTimeout(timer));
  }, []);

  const textOf = (line: InventoryCountLine) => {
    if (line.id in drafts) return drafts[line.id];
    return line.countedQuantity === null || line.countedQuantity === undefined ? '' : String(line.countedQuantity);
  };

  // A scanned code that is exactly one item's: straight to its box, once per code (saving it changes the items too).
  const scanned = useRef('');
  useEffect(() => {
    const term = filter.search.trim().toLowerCase();
    if (!term || term === scanned.current) return;
    const found = items.filter((line) => isCode(line, term));
    if (found.length !== 1) return;
    scanned.current = term;
    tableRef.current?.querySelector<HTMLInputElement>(`[data-count-line="${found[0].id}"]`)?.focus();
  }, [filter.search, items]);

  const showSaved = (lineId: string) => {
    window.clearTimeout(timers.current[lineId]);
    setStates((current) => ({ ...current, [lineId]: { kind: 'saved' } }));
    timers.current[lineId] = window.setTimeout(() => {
      setStates((current) => (current[lineId]?.kind === 'saved' ? without(current, lineId) : current));
    }, SAVED_FOR_MS);
  };

  /** Checks what was typed; a quantity the unit does not take stays in its box with why. */
  const check = (line: InventoryCountLine) => {
    if (!(line.id in drafts)) return true;
    const parsed = parseCountedQuantity(drafts[line.id], line.unit);
    if ('error' in parsed) {
      setStates((current) => ({ ...current, [line.id]: { kind: 'error', message: parsed.error } }));
      return false;
    }
    return true;
  };

  const save = async (line: InventoryCountLine) => {
    if (!(line.id in drafts)) return;
    const text = drafts[line.id];
    const parsed = parseCountedQuantity(text, line.unit);
    if ('error' in parsed) {
      setStates((current) => ({ ...current, [line.id]: { kind: 'error', message: parsed.error } }));
      return;
    }
    if (parsed.value === (line.countedQuantity ?? null)) {
      setDrafts((current) => (current[line.id] === text ? without(current, line.id) : current));
      setStates((current) => without(current, line.id));
      return;
    }
    if (saving.current[line.id] === text) return;

    saving.current[line.id] = text;
    setStates((current) => ({ ...current, [line.id]: { kind: 'saving' } }));
    try {
      await record.mutateAsync([{ lineId: line.id, countedQuantity: parsed.value }]);
      if (filter.pending) countedWhilePending.current = true;
      // What was typed while it saved stays in the box, to be saved too.
      setDrafts((current) => (current[line.id] === text ? without(current, line.id) : current));
      showSaved(line.id);
    } catch (err: unknown) {
      setStates((current) => ({ ...current, [line.id]: { kind: 'error', message: getApiErrorMessage(err, 'No se pudo guardar.') } }));
    } finally {
      if (saving.current[line.id] === text) delete saving.current[line.id];
    }
  };

  const next = (event: KeyboardEvent<HTMLInputElement>, line: InventoryCountLine) => {
    if (event.key !== 'Enter') return;
    event.preventDefault();
    if (!check(line)) return;

    // Leaving the box saves it. After a scan, back to the search, ready for the next code; otherwise the next box.
    const search = tableRef.current?.querySelector<HTMLInputElement>('input[type="search"]');
    const boxes = [...(tableRef.current?.querySelectorAll<HTMLInputElement>('[data-count-line]') ?? [])];
    const following = boxes[boxes.indexOf(event.currentTarget) + 1];
    if (filter.search.trim() && search) {
      search.focus();
      search.select();
    } else if (following) {
      following.focus();
    } else {
      void save(line);
    }
  };

  const goToPage = (page: number) => {
    // What was counted left the pages of what is left: the items that come next are on the first one now.
    if (filter.pending && countedWhilePending.current) {
      countedWhilePending.current = false;
      if (filter.page === 1) void lines.refetch();
      setFilter((current) => ({ ...current, page: 1 }));
      return;
    }
    setFilter((current) => ({ ...current, page }));
  };


  return (
    <div ref={tableRef}>
      {lines.isError && (
        <div className="alert alert-danger" role="alert">{getApiErrorMessage(lines.error, 'Error al cargar los artículos del conteo.')}</div>
      )}
      {!lines.isLoading && items.length === 0 && filter.search.trim() && (
        <p className="text-muted small mb-2">Si apareció en el anaquel y no está en el conteo, súmalo con «Agregar artículos».</p>
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
              onChange={(e) => {
                countedWhilePending.current = false;
                setFilter((current) => ({ ...current, page: 1, pending: e.target.value === 'pending' }));
              }}
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
          onPageChange: goToPage,
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
                  enterKeyHint="next"
                  autoComplete="off"
                  className={`form-control form-control-sm${state?.kind === 'error' ? ' is-invalid' : ''}`}
                  placeholder="Sin contar"
                  value={textOf(line)}
                  aria-invalid={state?.kind === 'error'}
                  aria-describedby={state ? `${inputId}-estado` : undefined}
                  onChange={(e) => {
                    const { value } = e.target;
                    setDrafts((current) => ({ ...current, [line.id]: value }));
                    if (state?.kind === 'error') setStates((current) => without(current, line.id));
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
