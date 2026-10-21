import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { getApiErrorCode, getApiErrorMessage } from '../../api/errors';
import {
  countKeys,
  useInventoryCount,
  useInventoryCountLines,
  usePostInventoryCount,
  useRecordInventoryCount,
  useSavingInventoryCount,
  type InventoryCount,
  type InventoryCountLine,
  type InventoryCountLinesFilter,
} from '../../api/inventoryCounts';
import { useConfirm } from '../../hooks/useConfirm';
import { InventoryCountStatus, type SupplyItemDto } from '../../types';
import { formatMoney } from '../../utils/money';
import AddCountItemsModal from './AddCountItemsModal';
import CancelCountModal from './CancelCountModal';
import CountDifferences from './CountDifferences';
import CountSheet from './CountSheet';
import { formatDateTime, getCountStatusChip, getCountStatusLabel } from './inventoryLabels';

type Mode = 'count' | 'review';

const CHANGED_MESSAGE = 'El conteo cambió mientras lo revisabas: ya se leyó otra vez, revísalo y vuelve a aplicarlo.';

interface CountDetailProps {
  countId: string;
  supplyOptions: SupplyItemDto[];
  onBack: () => void;
  onOpenMovement: (movementId: string) => void;
  /** The count moved the stock: the lists that show it are read again. */
  onPosted: (message: string) => void;
  onMessage: (message: string) => void;
}

function Summary({ count }: { count: InventoryCount }) {
  const tiles = [
    { label: 'Contados', value: `${count.countedLineCount} de ${count.lineCount}` },
    { label: 'Con diferencia', value: String(count.differenceLineCount) },
    ...(count.status === InventoryCountStatus.Cancelled ? [] : [
      { label: 'Sobrante', value: formatMoney(count.surplusValue ?? 0) },
      { label: 'Faltante', value: formatMoney(count.shortageValue ?? 0) },
    ]),
  ];
  return (
    <div className="row g-2 mb-3">
      {tiles.map((tile) => (
        <div key={tile.label} className="col-6 col-md-3">
          <div className="border rounded p-2 h-100">
            <div className="text-muted small">{tile.label}</div>
            <div className="fs-5 fw-semibold">{tile.value}</div>
          </div>
        </div>
      ))}
    </div>
  );
}

/**
 * A count (Fase 27A): while open, its sheet to count and the review of the differences to post it; once posted, what it
 * moved and its adjustments; once cancelled, why. The sheet stays mounted while reviewing, so its search, page and any
 * box left with an error are there on the way back.
 */
export default function CountDetail({ countId, supplyOptions, onBack, onOpenMovement, onPosted, onMessage }: CountDetailProps) {
  const client = useQueryClient();
  const query = useInventoryCount(countId);
  const post = usePostInventoryCount(countId);
  const recount = useRecordInventoryCount(countId);
  const saving = useSavingInventoryCount(countId);
  const { confirm } = useConfirm();
  const [mode, setMode] = useState<Mode>('count');
  const [dialog, setDialog] = useState<'add' | 'cancel' | null>(null);
  const [error, setError] = useState('');
  const [recountingId, setRecountingId] = useState<string>();
  const [reviewFilter, setReviewFilter] = useState<InventoryCountLinesFilter>({ search: '', pending: false, withDifference: true, page: 1, pageSize: 50 });
  const sheetRef = useRef<HTMLDivElement>(null);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const count = query.data;
  const open = count?.status === InventoryCountStatus.InProgress;
  const cancelled = count?.status === InventoryCountStatus.Cancelled;
  const reviewing = open && mode === 'review';
  const lines = useInventoryCountLines(countId, { ...reviewFilter, withDifference: !cancelled }, !!count && (reviewing || !open));
  const page = lines.data;
  // What is shown is what gets posted: the items reviewed must be those of the revision of the count.
  const current = !!count && !!page && page.revision === count.revision;
  const settled = saving === 0 && !query.isFetching && !lines.isFetching;

  // Opening a count, posting or cancelling it: the screen changes under the button, so the focus goes to the count.
  const loaded = !!count;
  useEffect(() => {
    if (loaded) headingRef.current?.focus();
  }, [loaded, count?.status]);

  // Reviewing reads the count as it is now (what others counted, what was sold), once what was typed is saved.
  useEffect(() => {
    if (!reviewing || saving > 0) return;
    void client.invalidateQueries({ queryKey: countKeys.count(countId) });
    void client.invalidateQueries({ queryKey: countKeys.linesOf(countId) });
  }, [reviewing, saving, client, countId]);

  // Whichever of the two is behind is read again, until they show the same revision.
  useEffect(() => {
    if (!reviewing || !settled || !count || !page || current) return;
    if (page.revision < count.revision) void lines.refetch();
    else void query.refetch();
  }, [reviewing, settled, count, page, current, lines, query]);

  const review = () => {
    // A box left with a quantity its unit does not take: fix it before reviewing.
    const invalid = sheetRef.current?.querySelector<HTMLInputElement>('[aria-invalid="true"]');
    if (invalid) {
      invalid.focus();
      return;
    }
    setError('');
    setMode('review');
  };

  const backToCount = () => {
    setMode('count');
    // The sheet shows what others counted meanwhile too.
    void client.invalidateQueries({ queryKey: countKeys.linesOf(countId) });
  };

  const recountLine = async (line: InventoryCountLine) => {
    setError('');
    setRecountingId(line.id);
    try {
      await recount.mutateAsync([{ lineId: line.id, countedQuantity: null }]);
      onMessage(`Se borró lo contado de ${line.supplyItemName}: cuéntalo otra vez en «Contar» (está en «Sin contar»).`);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err, 'No se pudo borrar lo contado.'));
    } finally {
      setRecountingId(undefined);
    }
  };

  const apply = async (reviewed: InventoryCount) => {
    setError('');
    const accepted = await confirm({
      title: `Aplicar el conteo ${reviewed.countNumber}`,
      message: reviewed.differenceLineCount === 0
        ? 'Ningún artículo contado tiene diferencia: las existencias no cambian y el conteo queda cerrado.'
        : `Se ajustan ${reviewed.differenceLineCount === 1 ? '1 artículo' : `${reviewed.differenceLineCount} artículos`}: `
          + `sobrante ${formatMoney(reviewed.surplusValue ?? 0)} y faltante ${formatMoney(reviewed.shortageValue ?? 0)}. `
          + 'Lo que no contaste se queda como está, y el conteo ya no se podrá cambiar.',
      confirmLabel: 'Aplicar conteo',
    });
    if (!accepted) return;

    try {
      const posted = await post.mutateAsync(reviewed.revision);
      onPosted(`Conteo ${posted.countNumber} aplicado.`);
    } catch (err: unknown) {
      setError(getApiErrorCode(err) === 'concurrency_conflict' ? CHANGED_MESSAGE : getApiErrorMessage(err, 'No se pudo aplicar el conteo.'));
    }
  };

  if (query.isLoading) {
    return (
      <div className="text-center py-4">
        <div className="spinner-border text-primary" role="status"><span className="visually-hidden">Cargando el conteo…</span></div>
      </div>
    );
  }
  if (!count) {
    return (
      <div>
        <button type="button" className="btn btn-link px-0" onClick={onBack}>
          <i className="ri-arrow-left-line me-1" aria-hidden="true"></i>Conteos
        </button>
        <div className="alert alert-danger" role="alert">{getApiErrorMessage(query.error, 'No se pudo cargar el conteo.')}</div>
      </div>
    );
  }

  const uncounted = count.lineCount - count.countedLineCount;
  return (
    <div>
      <button type="button" className="btn btn-link px-0" onClick={onBack}>
        <i className="ri-arrow-left-line me-1" aria-hidden="true"></i>Conteos
      </button>
      <div className="d-flex flex-wrap align-items-center gap-2 mb-1">
        <h2 className="h5 mb-0" ref={headingRef} tabIndex={-1}>{count.countNumber} · {count.inventoryLocationName}</h2>
        <span className={`ta-chip ${getCountStatusChip(count.status)}`}>{getCountStatusLabel(count.status)}</span>
      </div>
      <p className="text-muted small mb-3">
        Empezó {count.startedBy}, {formatDateTime(count.startedAt)}
        {count.categoryName && ` · Categoría: ${count.categoryName}`}
        {count.status === InventoryCountStatus.Posted && ` · Aplicó ${count.postedBy}, ${formatDateTime(count.postedAt ?? undefined)}`}
        {cancelled && ` · Canceló ${count.cancelledBy}, ${formatDateTime(count.cancelledAt ?? undefined)}`}
      </p>
      {count.notes && <p className="mb-3">{count.notes}</p>}
      {cancelled && (
        <div className="alert alert-secondary" role="status">
          Cancelado sin mover existencias{count.cancelReason ? `: ${count.cancelReason}` : '.'}
        </div>
      )}
      {error && <div className="alert alert-danger" role="alert">{error}</div>}

      {open && (
        <div className="d-flex flex-wrap justify-content-between gap-2 mb-3">
          <div className="btn-group" role="group" aria-label="Qué haces con el conteo">
            <button type="button" className={`btn btn-sm ${mode === 'count' ? 'btn-secondary' : 'btn-outline-secondary'}`}
              aria-pressed={mode === 'count'} onClick={backToCount}>
              Contar
            </button>
            <button type="button" className={`btn btn-sm ${mode === 'review' ? 'btn-secondary' : 'btn-outline-secondary'}`}
              aria-pressed={mode === 'review'} onClick={review}>
              Revisar diferencias
            </button>
          </div>
          <div className="d-flex flex-wrap gap-2">
            <button type="button" className="btn btn-sm btn-light text-nowrap" onClick={() => setDialog('add')}>
              <i className="ri-add-line me-1" aria-hidden="true"></i>Agregar artículos
            </button>
            <button type="button" className="btn btn-sm btn-outline-danger text-nowrap" onClick={() => setDialog('cancel')}>
              Cancelar conteo
            </button>
          </div>
        </div>
      )}

      {open && (
        <div ref={sheetRef} className={mode === 'count' ? undefined : 'd-none'}>
          <CountSheet countId={count.id} />
        </div>
      )}

      {reviewing && (
        <>
          <Summary count={count} />
          {uncounted > 0 && (
            <p className="text-muted">
              {uncounted === 1
                ? 'Falta por contar 1 artículo: se queda como está.'
                : `Faltan por contar ${uncounted} artículos: se quedan como están.`}
            </p>
          )}
          {saving > 0 && <p className="text-muted" role="status">Guardando lo contado…</p>}
          <CountDifferences
            status={count.status}
            lines={lines}
            filter={reviewFilter}
            onFilterChange={setReviewFilter}
            onRecount={(line) => void recountLine(line)}
            recountingId={recountingId}
          />
          <div className="d-flex justify-content-end mt-3">
            <button
              type="button"
              className="btn btn-primary"
              disabled={post.isPending || !settled || !current || count.countedLineCount === 0}
              onClick={() => void apply(count)}
            >
              {post.isPending ? 'Aplicando…' : 'Aplicar conteo'}
            </button>
          </div>
        </>
      )}

      {!open && (
        <>
          <Summary count={count} />
          {count.status === InventoryCountStatus.Posted && (count.increaseMovementId || count.decreaseMovementId) && (
            <p className="d-flex flex-wrap align-items-center gap-2">
              <span>Ajustes:</span>
              {count.increaseMovementId && (
                <button type="button" className="btn btn-sm btn-light text-nowrap" onClick={() => onOpenMovement(count.increaseMovementId!)}>
                  Ver entrada {count.increaseMovementNumber}
                </button>
              )}
              {count.decreaseMovementId && (
                <button type="button" className="btn btn-sm btn-light text-nowrap" onClick={() => onOpenMovement(count.decreaseMovementId!)}>
                  Ver salida {count.decreaseMovementNumber}
                </button>
              )}
            </p>
          )}
          <CountDifferences status={count.status} lines={lines} filter={reviewFilter} onFilterChange={setReviewFilter} showAll={cancelled} />
        </>
      )}

      {dialog === 'add' && (
        <AddCountItemsModal
          countId={count.id}
          lineCount={count.lineCount}
          supplyOptions={supplyOptions}
          onHide={() => setDialog(null)}
          onAdded={(added) => {
            setDialog(null);
            onMessage(added === 0
              ? 'Esos artículos ya estaban en el conteo.'
              : added === 1 ? 'Se agregó 1 artículo al conteo.' : `Se agregaron ${added} artículos al conteo.`);
          }}
        />
      )}
      {dialog === 'cancel' && (
        <CancelCountModal
          countId={count.id}
          countNumber={count.countNumber}
          onHide={() => setDialog(null)}
          onCancelled={() => {
            setDialog(null);
            onMessage(`Conteo ${count.countNumber} cancelado.`);
          }}
        />
      )}
    </div>
  );
}
