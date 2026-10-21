import { useState } from 'react';
import { getApiErrorMessage } from '../../api/errors';
import { useInventoryCount, usePostInventoryCount, type InventoryCount } from '../../api/inventoryCounts';
import { useConfirm } from '../../hooks/useConfirm';
import { InventoryCountStatus, type SupplyItemDto } from '../../types';
import { formatMoney } from '../../utils/money';
import AddCountItemsModal from './AddCountItemsModal';
import CancelCountModal from './CancelCountModal';
import CountDifferences from './CountDifferences';
import CountSheet from './CountSheet';
import { formatDateTime, getCountStatusChip, getCountStatusLabel } from './inventoryLabels';

type Mode = 'count' | 'review';

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
 * moved and its adjustments; once cancelled, why.
 */
export default function CountDetail({ countId, supplyOptions, onBack, onOpenMovement, onPosted, onMessage }: CountDetailProps) {
  const query = useInventoryCount(countId);
  const post = usePostInventoryCount(countId);
  const { confirm } = useConfirm();
  const [mode, setMode] = useState<Mode>('count');
  const [dialog, setDialog] = useState<'add' | 'cancel' | null>(null);
  const [error, setError] = useState('');
  const count = query.data;

  const review = () => {
    setError('');
    setMode('review');
    // The review shows the count as it is now: what others counted, and what was sold, too.
    void query.refetch();
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
      setError(getApiErrorMessage(err, 'No se pudo aplicar el conteo.'));
    }
  };

  if (query.isLoading) {
    return <div className="text-center py-4"><div className="spinner-border text-primary" role="status"></div></div>;
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

  const open = count.status === InventoryCountStatus.InProgress;
  return (
    <div>
      <button type="button" className="btn btn-link px-0" onClick={onBack}>
        <i className="ri-arrow-left-line me-1" aria-hidden="true"></i>Conteos
      </button>
      <div className="d-flex flex-wrap align-items-center gap-2 mb-1">
        <h2 className="h5 mb-0">{count.countNumber} · {count.inventoryLocationName}</h2>
        <span className={`ta-chip ${getCountStatusChip(count.status)}`}>{getCountStatusLabel(count.status)}</span>
      </div>
      <p className="text-muted small mb-3">
        Empezó {count.startedBy}, {formatDateTime(count.startedAt)}
        {count.categoryName && ` · Categoría: ${count.categoryName}`}
        {count.status === InventoryCountStatus.Posted && ` · Aplicó ${count.postedBy}, ${formatDateTime(count.postedAt ?? undefined)}`}
        {count.status === InventoryCountStatus.Cancelled && ` · Canceló ${count.cancelledBy}, ${formatDateTime(count.cancelledAt ?? undefined)}`}
      </p>
      {count.notes && <p className="mb-3">{count.notes}</p>}
      {count.status === InventoryCountStatus.Cancelled && (
        <div className="alert alert-secondary" role="status">
          Cancelado sin mover existencias{count.cancelReason ? `: ${count.cancelReason}` : '.'}
        </div>
      )}
      {error && <div className="alert alert-danger" role="alert">{error}</div>}

      {open && (
        <div className="d-flex flex-wrap justify-content-between gap-2 mb-3">
          <div className="btn-group" role="group" aria-label="Qué haces con el conteo">
            <button type="button" className={`btn btn-sm ${mode === 'count' ? 'btn-secondary' : 'btn-outline-secondary'}`}
              aria-pressed={mode === 'count'} onClick={() => setMode('count')}>
              Contar
            </button>
            <button type="button" className={`btn btn-sm ${mode === 'review' ? 'btn-secondary' : 'btn-outline-secondary'}`}
              aria-pressed={mode === 'review'} onClick={review}>
              Revisar diferencias
            </button>
          </div>
          <div className="d-flex gap-2">
            <button type="button" className="btn btn-sm btn-light" onClick={() => setDialog('add')}>
              <i className="ri-add-line me-1" aria-hidden="true"></i>Agregar artículos
            </button>
            <button type="button" className="btn btn-sm btn-outline-danger" onClick={() => setDialog('cancel')}>
              Cancelar conteo
            </button>
          </div>
        </div>
      )}

      {open && mode === 'count' && <CountSheet countId={count.id} />}

      {open && mode === 'review' && (
        <>
          <Summary count={count} />
          {count.countedLineCount < count.lineCount && (
            <p className="text-muted">
              {count.lineCount - count.countedLineCount === 1
                ? 'Falta contar 1 artículo: se queda como está.'
                : `Faltan contar ${count.lineCount - count.countedLineCount} artículos: se quedan como están.`}
            </p>
          )}
          <CountDifferences countId={count.id} status={count.status} />
          <div className="d-flex justify-content-end mt-3">
            <button
              type="button"
              className="btn btn-primary"
              disabled={post.isPending || query.isFetching || count.countedLineCount === 0}
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
                <button type="button" className="btn btn-sm btn-light"
                  aria-label={`Ver el ajuste de entrada ${count.increaseMovementNumber}`}
                  onClick={() => onOpenMovement(count.increaseMovementId!)}>
                  Ver {count.increaseMovementNumber} (+)
                </button>
              )}
              {count.decreaseMovementId && (
                <button type="button" className="btn btn-sm btn-light"
                  aria-label={`Ver el ajuste de salida ${count.decreaseMovementNumber}`}
                  onClick={() => onOpenMovement(count.decreaseMovementId!)}>
                  Ver {count.decreaseMovementNumber} (−)
                </button>
              )}
            </p>
          )}
          <CountDifferences countId={count.id} status={count.status} showAll={count.status === InventoryCountStatus.Cancelled} />
        </>
      )}

      {dialog === 'add' && (
        <AddCountItemsModal
          countId={count.id}
          supplyOptions={supplyOptions}
          onHide={() => setDialog(null)}
          onAdded={(added) => {
            setDialog(null);
            onMessage(added === 1 ? 'Artículo agregado al conteo.' : 'Artículos agregados al conteo.');
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
