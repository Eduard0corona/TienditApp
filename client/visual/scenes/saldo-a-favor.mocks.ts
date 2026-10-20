import { asOption, type DemoData } from '../fixtures';
import { at } from '../mocks/data.mjs';
import { withCredit } from './fiado.mocks';
import { createSale } from './vender-pedidos.mocks';

// CustomerBalanceEntryKind, OrderPaymentMethod and OrderStatus as the contract numbers them.
const DEPOSIT = 0;
const RETURNED = 3;
const CASH = 0;
const TRANSFER = 2;
const DELIVERED = 6;

const MAFER = '+525541238890';
const ALTAVISTA = '+525552089100';
const SOFIA = '+525561903327';

/**
 * Who sells on credit at Café La Esquina (fiado.mocks), with what some of them left in their favor (Fase 26D): María
 * Fernanda left the $200 that were over a payment and took $50 back, the offices of Grupo Altavista sent $500 ahead of
 * Friday's order, and Sofía, who owes nothing, left $300 for her birthday cake. Sofía also bought a latte and a croissant
 * a minute ago, paid in cash: the sale the refund to her balance starts from.
 */
function withBalances(data: DemoData) {
  withCredit(data);
  data.balanceEntries = [
    { id: 'saldo-mafer-1', phone: MAFER, kind: DEPOSIT, amount: 200, method: CASH, notes: 'Lo que sobró de un abono', occurredAt: at(18, 10, -5) },
    { id: 'saldo-mafer-2', phone: MAFER, kind: RETURNED, amount: -50, method: CASH, notes: 'Lo pidió para el camión', occurredAt: at(11, 30, -2) },
    {
      id: 'saldo-altavista-1', phone: ALTAVISTA, kind: DEPOSIT, amount: 500, method: TRANSFER, reference: 'SPEI 4471',
      notes: 'Anticipo del pedido del viernes', occurredAt: at(10, 5, -1),
    },
    { id: 'saldo-sofia-1', phone: SOFIA, kind: DEPOSIT, amount: 300, method: CASH, notes: 'Anticipo del pastel de cumpleaños', occurredAt: at(17, 45, -2) },
  ];

  const product = (name: string) => data.products.find((candidate: { name: string }) => candidate.name === name).id;
  const sale = createSale(data, {
    customerName: 'Sofía Ramírez Cruz',
    customerPhone: SOFIA,
    items: [{ productId: product('Latte'), quantity: 1 }, { productId: product('Croissant de mantequilla'), quantity: 1 }],
  });
  const paidAt = at(13, 24);
  Object.assign(sale, {
    createdAt: paidAt, status: DELIVERED, paidAmount: sale.total, netPaidAmount: sale.total, balance: 0, canCancel: false, allowedTransitions: [],
    payments: [{
      id: `${sale.id}-pago-1`, amount: sale.total, method: CASH, paidAt, reference: null, notes: null, tip: 0, cashReceived: sale.total, change: 0,
    }],
  });
}

export const CON_SALDO_A_FAVOR = asOption(withBalances);
