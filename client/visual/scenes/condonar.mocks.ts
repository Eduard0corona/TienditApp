import { asOption, type DemoData } from '../fixtures';
import { at } from '../mocks/data.mjs';
import { withCredit } from './fiado.mocks';

// OrderWriteOffReason as the contract numbers it.
const RECORDING_ERROR = 2;
const COURTESY = 3;

/** Forgives part of an order as the server records it (Order.WriteOff): a record apart, and a lower balance. */
function forgive(data: DemoData, orderNumber: string, amount: number, reason: number, notes: string, writtenOffAt: string) {
  const order = data.orders.find((candidate: { orderNumber: string }) => candidate.orderNumber === orderNumber);
  order.writeOffs = [...(order.writeOffs ?? []), {
    id: `condonado-${orderNumber.toLowerCase()}`, amount, reason, notes, writtenOffAt, writtenOffBy: data.OWNER_EMAIL,
  }];
  order.writtenOffAmount = (order.writtenOffAmount ?? 0) + amount;
  order.balance = Math.round((order.total - order.paidAmount - order.writtenOffAmount) * 100) / 100;
}

/**
 * Who Café La Esquina sells on credit (fiado.mocks) with a term of 7 days (Fase 26B): what the offices of Grupo Altavista
 * bought on 21 September and what Roberto bought on the 26th are overdue. The owner forgave $100 of the offices' oldest
 * order as a courtesy and $20 of María Fernanda's sale of today, recorded with an extra agua de jamaica.
 */
function withTermAndWriteOffs(data: DemoData) {
  withCredit(data);
  data.creditTermDays = 7;
  forgive(data, 'ORD-000262', 100, COURTESY, 'Descuento por el pedido de la semana', at(17, 30, -2));
  forgive(data, 'ORD-000349', 20, RECORDING_ERROR, 'Se cobró un agua de jamaica de más', at(13, 40, 0));
}

export const CON_PLAZO = asOption(withTermAndWriteOffs);
