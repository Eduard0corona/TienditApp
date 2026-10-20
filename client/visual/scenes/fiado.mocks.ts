import { asOption, type DemoData } from '../fixtures';
import { at } from '../mocks/data.mjs';
import { createSale } from './vender-pedidos.mocks';

// OrderStatus and OrderPaymentMethod as the contract numbers them.
const DELIVERED = 6;
const CASH = 0;

/** Antonia, a neighbour without a customer record: what she owes is under the phone of her order. */
export const ANTONIA_PHONE = '+525547120983';

interface PastSale {
  number: number;
  /** The hour and minute of the business's day, and how many days before today. */
  when: [number, number, number];
  customer: { name: string; phone: string };
  items: [string, number][];
  onCredit: boolean;
  /** Paid in cash when it was sold. */
  paid?: number;
}

/**
 * A counter sale of the past days, as Vender recorded it (createSale, which adds it to the orders), with its own folio
 * and date. The sale of a scene keeps the next folio after today's last one.
 */
function pastSale(data: DemoData, sale: PastSale) {
  const product = (name: string) => data.products.find((candidate: { name: string }) => candidate.name === name).id;
  const order = createSale(data, {
    customerName: sale.customer.name,
    customerPhone: sale.customer.phone,
    items: sale.items.map(([name, quantity]) => ({ productId: product(name), quantity })),
    payments: sale.paid ? [{ amount: sale.paid, method: CASH, cashReceived: sale.paid }] : [],
    onCredit: sale.onCredit,
  });
  data.vpSales -= 1;
  const orderNumber = `ORD-${String(sale.number).padStart(6, '0')}`;
  const createdAt = at(...sale.when);
  Object.assign(order, {
    id: `venta-${orderNumber.toLowerCase()}`, orderNumber, createdAt, status: DELIVERED, canCancel: false, allowedTransitions: [],
  });
  for (const payment of order.payments) payment.paidAt = createdAt;
}

/**
 * Who Café La Esquina sells on credit (Fase 26A): María Fernanda up to $800 (she owes $316 of three sales, one of them
 * today), the offices of Grupo Altavista up to $5,000 (two weekly orders, $1,260), Roberto up to $300 although he owes
 * $360 (his limit was lowered) and Sofía up to $500 (she owes nothing). Juan Carlos owes the rest of this morning's
 * order without fiado, and Antonia, without a customer record, a sale delivered unpaid on Wednesday.
 */
export function withCredit(data: DemoData) {
  const customer = (name: string) => data.customers.find((candidate: { name: string }) => candidate.name === name);
  const limits: Record<string, number> = {
    'María Fernanda López': 800, 'Oficinas Grupo Altavista': 5000, 'Roberto Sánchez Vega': 300, 'Sofía Ramírez Cruz': 500,
  };
  for (const [name, limit] of Object.entries(limits)) customer(name).creditLimit = limit;

  const of = (name: string) => ({ name, phone: `+52${customer(name).phone.replace(/\s+/g, '')}` });
  const mafer = of('María Fernanda López');
  const altavista = of('Oficinas Grupo Altavista');
  const roberto = of('Roberto Sánchez Vega');
  pastSale(data, { number: 262, when: [12, 15, -13], customer: altavista, items: [['Café americano', 10], ['Concha de vainilla', 10]], onCredit: true });
  pastSale(data, { number: 275, when: [17, 40, -8], customer: roberto, items: [['Café de Chiapas 250 g', 2]], onCredit: true });
  pastSale(data, { number: 281, when: [12, 20, -6], customer: altavista, items: [['Café americano', 10], ['Croissant de mantequilla', 10]], onCredit: true });
  pastSale(data, { number: 288, when: [9, 10, -6], customer: mafer, items: [['Latte', 2], ['Concha de vainilla', 2]], onCredit: true });
  pastSale(data, {
    number: 294, when: [19, 5, -4], customer: { name: 'Antonia Reyes', phone: ANTONIA_PHONE },
    items: [['Molletes con pico de gallo', 2], ['Agua de jamaica', 2]], onCredit: false,
  });
  pastSale(data, { number: 301, when: [8, 40, -3], customer: mafer, items: [['Chilaquiles verdes', 1], ['Café americano', 1]], onCredit: true, paid: 50 });
  pastSale(data, { number: 349, when: [13, 23, 0], customer: mafer, items: [['Torta de jamón', 1], ['Agua de jamaica', 1]], onCredit: true });
  // Today's sale on credit is the latest folio: a sale of the scenes takes the next one.
  data.vpSales = 1;
  data.orders.sort((a: { createdAt: string }, b: { createdAt: string }) => b.createdAt.localeCompare(a.createdAt));
}

export const CON_FIADO = asOption(withCredit);
