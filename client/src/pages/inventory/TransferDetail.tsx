import { useEffect, useRef } from 'react';
import { getApiErrorMessage } from '../../api/errors';
import { useInventoryTransfer } from '../../api/inventoryTransfers';
import DataTable from '../../components/common/DataTable';
import { formatMoney, formatUnitCost } from '../../utils/money';
import { formatStockQuantity } from '../../utils/units';
import { formatDateTime } from './inventoryLabels';

// On a phone the item, how much moved and its value: what each location had from md up.
const WIDE = 'd-none d-md-table-cell';

const COLUMNS = [
  { header: 'Artículo' },
  { header: 'Cantidad', className: 'text-end' },
  { header: 'Costo', className: `text-end ${WIDE}` },
  { header: 'Valor', className: 'text-end' },
  { header: 'Origen', className: `text-end ${WIDE}` },
  { header: 'Destino', className: `text-end ${WIDE}` },
];

interface TransferDetailProps {
  transferId: string;
  onBack: () => void;
  onOpenMovement: (movementId: string) => void;
}

/** A transfer (Fase 27B): what moved, what it was worth, what each location had before and after, and its two movements. */
export default function TransferDetail({ transferId, onBack, onOpenMovement }: TransferDetailProps) {
  const query = useInventoryTransfer(transferId);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const transfer = query.data;

  // The screen changes under the button that opened it: the focus goes to the transfer.
  const loaded = !!transfer;
  useEffect(() => {
    if (loaded) headingRef.current?.focus();
  }, [loaded]);

  const back = (
    <button type="button" className="btn btn-link px-0" onClick={onBack}>
      <i className="ri-arrow-left-line me-1" aria-hidden="true"></i>Transferencias
    </button>
  );

  if (query.isLoading) {
    return (
      <div className="text-center py-4">
        <div className="spinner-border text-primary" role="status"><span className="visually-hidden">Cargando la transferencia…</span></div>
      </div>
    );
  }
  if (!transfer) {
    return (
      <div>
        {back}
        <div className="alert alert-danger" role="alert">{getApiErrorMessage(query.error, 'No se pudo cargar la transferencia.')}</div>
      </div>
    );
  }

  return (
    <div>
      {back}
      <h2 className="h5 mb-1" ref={headingRef} tabIndex={-1}>
        {transfer.transferNumber} · de {transfer.fromLocationName} a {transfer.toLocationName}
      </h2>
      <p className="text-muted small mb-3">
        Transfirió {transfer.transferredBy}, {formatDateTime(transfer.transferredAt)}
        {' · '}{transfer.lineCount === 1 ? '1 artículo' : `${transfer.lineCount} artículos`}
        {' · '}Valor {formatMoney(transfer.totalCost)}
      </p>
      {transfer.notes && <p className="mb-3">{transfer.notes}</p>}
      <p className="d-flex flex-wrap align-items-center gap-2">
        <span>Movimientos:</span>
        <button type="button" className="btn btn-sm btn-light text-nowrap" onClick={() => onOpenMovement(transfer.outMovementId)}>
          Ver salida {transfer.outMovementNumber}
        </button>
        <button type="button" className="btn btn-sm btn-light text-nowrap" onClick={() => onOpenMovement(transfer.inMovementId)}>
          Ver entrada {transfer.inMovementNumber}
        </button>
      </p>
      <DataTable
        data={transfer.lines}
        columns={COLUMNS}
        keyExtractor={(line) => line.supplyItemId}
        searchPlaceholder="Buscar artículo"
        searchFilter={(line, search) => line.supplyItemName.toLowerCase().includes(search.toLowerCase())}
        pageSizeOptions={[50, 100, 200]}
        renderRow={(line) => (
          <>
            <td className="text-wrap">{line.supplyItemName}</td>
            <td className="text-end">{formatStockQuantity(line.quantity, line.unit)}</td>
            <td className={`text-end ${WIDE}`}>{line.unitCost === null || line.unitCost === undefined ? '—' : formatUnitCost(line.unitCost)}</td>
            <td className="text-end">{line.totalCost === null || line.totalCost === undefined ? '—' : formatMoney(line.totalCost)}</td>
            <td className={`text-end text-nowrap ${WIDE}`}>
              {formatStockQuantity(line.fromQuantityBefore, line.unit)} → {formatStockQuantity(line.fromQuantityAfter, line.unit)}
            </td>
            <td className={`text-end text-nowrap ${WIDE}`}>
              {formatStockQuantity(line.toQuantityBefore, line.unit)} → {formatStockQuantity(line.toQuantityAfter, line.unit)}
            </td>
          </>
        )}
      />
    </div>
  );
}
