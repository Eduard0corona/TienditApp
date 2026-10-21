import type { CreateInventoryTransferBody } from '../../api/inventoryTransfers';
import type { UnitType } from '../../types';
import { formatStockQuantity, parseQuantity } from '../../utils/units';
import type { FormResult } from './inventoryForms';

/** The most items a transfer takes, as the API. */
export const MAX_TRANSFER_LINES = 200;

/** An item to transfer, with what the origin had of it when it was added. */
export interface TransferLineForm {
  supplyItemId: string;
  name: string;
  unit: UnitType;
  onHand: number;
  quantity: string;
}

export interface TransferForm {
  fromLocationId: string;
  toLocationId: string;
  notes: string;
  lines: TransferLineForm[];
}

/**
 * The transfer to send, or the first problem to show. The API checks it all again against what the origin has right
 * now; this only spares a trip for what is plainly wrong.
 */
export function toTransferRequest(form: TransferForm, idempotencyKey: string, originName: string): FormResult<CreateInventoryTransferBody> {
  if (!form.fromLocationId) return { error: 'Elige de qué ubicación salen los artículos.' };
  if (!form.toLocationId) return { error: 'Elige a qué ubicación van los artículos.' };
  if (form.fromLocationId === form.toLocationId) return { error: 'Elige una ubicación de destino distinta a la de origen.' };
  if (form.lines.length === 0) return { error: 'Agrega al menos un artículo.' };
  if (form.lines.length > MAX_TRANSFER_LINES) return { error: `Una transferencia lleva como máximo ${MAX_TRANSFER_LINES} artículos.` };

  const lines = [];
  for (const line of form.lines) {
    const quantity = parseQuantity(line.quantity, line.unit);
    if (quantity === null) {
      return { error: `Escribe cuánto de ${line.name}: más de 0, con máximo 3 decimales y entero si va en piezas.` };
    }
    if (quantity > line.onHand) {
      return { error: `En ${originName} solo hay ${formatStockQuantity(line.onHand, line.unit)} de ${line.name}.` };
    }
    lines.push({ supplyItemId: line.supplyItemId, quantity });
  }

  return {
    request: {
      idempotencyKey,
      fromLocationId: form.fromLocationId,
      toLocationId: form.toLocationId,
      notes: form.notes.trim() || null,
      lines,
    },
  };
}
