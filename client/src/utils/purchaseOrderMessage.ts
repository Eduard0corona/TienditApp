import type { PurchaseOrderDto } from '../types';
import { PurchaseOrderStatus, UnitType } from '../types';
import { formatDate } from './dates';
import { formatMoney } from './money';
import { allowsFractions, formatStockQuantity } from './units';

// Whole units as the supplier reads them: "1 caja", "3 cajas".
const COUNT_NOUNS: Partial<Record<UnitType, readonly [singular: string, plural: string]>> = {
  [UnitType.Unit]: ['pieza', 'piezas'],
  [UnitType.Pack]: ['paquete', 'paquetes'],
  [UnitType.Box]: ['caja', 'cajas'],
  [UnitType.Pair]: ['par', 'pares'],
  [UnitType.Portion]: ['porción', 'porciones'],
  [UnitType.Slice]: ['rebanada', 'rebanadas'],
  [UnitType.Serving]: ['ración', 'raciones'],
  [UnitType.Roll]: ['rollo', 'rollos'],
  [UnitType.Session]: ['sesión', 'sesiones'],
  [UnitType.Service]: ['servicio', 'servicios'],
};

/** "2.5 kg", "1 caja", "3 cajas": the quantity with the unit the supply is bought in. */
export function formatOrderedQuantity(quantity: number, unit?: UnitType | null): string {
  if (unit === undefined || unit === null || allowsFractions(unit)) return formatStockQuantity(quantity, unit);
  const nouns = COUNT_NOUNS[unit];
  const number = formatStockQuantity(quantity);
  return nouns ? `${number} ${quantity === 1 ? nouns[0] : nouns[1]}` : number;
}

/**
 * The order as the supplier reads it in WhatsApp. Once part of it arrived, only what is still missing:
 * resending it works as a reminder.
 */
export function buildPurchaseOrderMessage(order: PurchaseOrderDto, businessName?: string): string {
  const onlyPending = order.status === PurchaseOrderStatus.PartiallyReceived;
  const lines = onlyPending ? order.lines.filter((line) => line.pendingQuantity > 0) : order.lines;

  const text: string[] = [`*Orden de compra ${order.orderNumber}*${onlyPending ? ' (pendiente)' : ''}`];
  if (businessName) text.push(businessName);
  text.push('', `Hola, ${order.supplierName}. ${onlyPending ? 'Aún falta por entregar:' : 'Te pedimos:'}`);
  lines.forEach((line) => {
    const quantity = onlyPending ? line.pendingQuantity : line.quantity;
    text.push(`• ${line.supplyItemName}: ${formatOrderedQuantity(quantity, line.unit)}`);
    if (line.notes) text.push(`   _${line.notes}_`);
  });

  const details: string[] = [];
  if (order.expectedDeliveryAt) details.push(`Entrega: ${formatDate(order.expectedDeliveryAt)}`);
  if (order.referenceNumber) details.push(`Referencia: ${order.referenceNumber}`);
  if (order.notes) details.push(`Notas: ${order.notes}`);
  if (details.length > 0) text.push('', ...details);

  // The total is the one the API calculated for the whole order; it no longer applies to a remainder.
  if (!onlyPending) text.push('', `Total: ${formatMoney(order.total)}`);
  text.push('', '¡Gracias!');
  return text.join('\n');
}
