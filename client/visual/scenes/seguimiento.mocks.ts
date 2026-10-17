import { asOption, type DemoData, type SceneOptions } from '../fixtures';
import { NOW_ISO } from '../mocks/data.mjs';
import { TRACKING_LINK, publicRoutes } from './publico-cuenta-marco.mocks';

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;

// OrderTrackingStep and OrderTrackingStepState as the contract numbers them (Fase 24C).
const RECEIVED = 0;
const CONFIRMED = 1;
const PREPARING = 2;
const READY = 3;
const ON_THE_WAY = 4;
const DELIVERED = 5;
const CANCELLED = 6;
const DONE = 0;
const CURRENT = 1;
const UPCOMING = 2;

/** Minutes before the scenes' now (13:25 in Mexico City). */
const ago = (minutes: number) => new Date(new Date(NOW_ISO).getTime() - minutes * 60_000).toISOString();

const step = (kind: number, label: string, state: number, at: string | null = null) => ({ step: kind, label, state, at });

/** An error as the API answers it (ApiProblem). */
function problem(status: number, detail: string) {
  return { status, json: { status, title: 'Not Found', detail, code: 'not_found', error: detail, traceId: '00-visual-00' } };
}

function productId(data: DemoData, name: string): string {
  return data.products.find((p: { name: string }) => p.name === name)?.id ?? `otro-negocio:${name}`;
}

/**
 * María Fernanda's orders in detail (MyOrderDetailDto): the tortas and aguas of Café La Esquina on their way to her home,
 * with the courier's link, and a latte the business cancelled because the milk ran out.
 */
function myOrder(data: DemoData, id: string) {
  const line = (name: string, quantity: number, amount: number, options: string[] = []) => ({
    productId: productId(data, name), productName: name, quantity, amount, notes: null, options, optionIds: [],
  });
  const business = {
    businessName: 'Café La Esquina', businessSlug: 'cafe-la-esquina', businessContactPhone: '5512345678', canOrderAgain: true,
    orderType: 0, pricesIncludeTax: true, tableNumber: null,
  };
  if (id === 'my-1') {
    return {
      ...business, id, orderNumber: 'ORD-000412', status: READY, createdAt: ago(40), step: ON_THE_WAY, stepLabel: 'En camino',
      steps: [
        step(RECEIVED, 'Recibido', DONE, ago(40)),
        step(CONFIRMED, 'Confirmado', DONE, ago(38)),
        step(PREPARING, 'En preparación', DONE, ago(33)),
        step(READY, 'Listo', DONE, ago(20)),
        step(ON_THE_WAY, 'En camino', CURRENT, ago(12)),
        step(DELIVERED, 'Entregado', UPCOMING),
      ],
      inProgress: true,
      courierTrackingUrl: 'https://paquetenvia.example/rastreo/PQ48160',
      items: [line('Torta de jamón', 2, 140), line('Agua de jamaica', 2, 60)],
      deliveryFee: 35, tax: 32.41, total: 235, paidAmount: 0, cancellationReason: null,
      delivery: { postalCode: '06700', addressLine: 'Colima 145, depto. 3, Roma Norte', references: 'Portón negro, tocar el 3' },
    };
  }
  if (id === 'my-4') {
    return {
      ...business, id, orderNumber: 'ORD-000405', status: 99, createdAt: ago(26 * 60), step: CANCELLED, stepLabel: 'Cancelado',
      steps: [
        step(RECEIVED, 'Recibido', DONE, ago(26 * 60)),
        step(CONFIRMED, 'Confirmado', DONE, ago(26 * 60 - 2)),
        step(CANCELLED, 'Cancelado', CURRENT, ago(26 * 60 - 9)),
      ],
      inProgress: false,
      courierTrackingUrl: null,
      items: [line('Latte', 1, 55, ['Grande', 'Almendra'])],
      deliveryFee: null, tax: 7.59, total: 55, paidAmount: 0,
      cancellationReason: 'Se nos acabó la leche de almendra, perdón.',
      delivery: null,
    };
  }
  return null;
}

/**
 * The guest's order followed with the link of its receipt (PublicOrderTrackingDto): the two conchas and the americano of
 * Café La Esquina, accepted and in preparation, to an address it does not show. Any other link is not found.
 */
function guestOrder(data: DemoData) {
  return {
    orderNumber: 'ORD-000360', businessName: 'Café La Esquina', businessSlug: 'cafe-la-esquina', businessContactPhone: '5512345678',
    orderType: 0, createdAt: ago(9), step: PREPARING, stepLabel: 'En preparación',
    steps: [
      step(RECEIVED, 'Recibido', DONE, ago(9)),
      step(CONFIRMED, 'Confirmado', DONE, ago(7)),
      step(PREPARING, 'En preparación', CURRENT, ago(4)),
      step(READY, 'Listo', UPCOMING),
      step(ON_THE_WAY, 'En camino', UPCOMING),
      step(DELIVERED, 'Entregado', UPCOMING),
    ],
    inProgress: true, toAddress: true, courierTrackingUrl: null,
    items: [
      { productId: productId(data, 'Café americano'), productName: 'Café americano', quantity: 1, amount: 35, notes: null, options: [], optionIds: [] },
      { productId: productId(data, 'Concha de vainilla'), productName: 'Concha de vainilla', quantity: 2, amount: 36, notes: null, options: [], optionIds: [] },
    ],
    deliveryFee: 35, pricesIncludeTax: true, tax: 14.62, total: 106, paidAmount: 0, cancellationReason: null,
  };
}

const trackingRoutes: ExtraRoutes = (data, helpers) => [
  ['GET', /^\/me\/orders\/([^/]+)$/, (match) => {
    const order = myOrder(data, decodeURIComponent(match[1]));
    return order ? helpers.ok(order) : problem(404, 'No encontramos ese pedido en tu cuenta.');
  }],
  ['POST', /^\/public\/orders\/tracking$/, (_match, _query, body: { token?: string }) => (body?.token === TRACKING_LINK
    ? helpers.ok(guestOrder(data))
    : problem(404, 'No encontramos ese pedido. El enlace pudo haber vencido.'))],
  ...publicRoutes(data, helpers),
];

export const SEGUIMIENTO_ROUTES = asOption(trackingRoutes);
