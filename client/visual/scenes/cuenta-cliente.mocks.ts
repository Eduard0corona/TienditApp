import { asOption, type DemoData, type SceneOptions } from '../fixtures';
import { uuid } from '../mocks/data.mjs';
import { demoDirectory } from '../mocks/routes-p9.mjs';
import { CUSTOMER_PROFILE } from './vender-pedidos.mocks';
import { deliveryQuote, publicRoutes } from './publico-cuenta-marco.mocks';

type ExtraRoutes = NonNullable<SceneOptions['extraRoutes']>;

/** María Fernanda, the demo customer, with the phone she confirmed: the store writes it for her. */
export const ACCOUNT_PROFILE = { ...CUSTOMER_PROFILE, phoneNumber: '+5215522107765' };

/**
 * The cart the device kept in Café La Esquina twenty minutes ago (an americano and two conchas), as the store saves it
 * (savedCart.ts): Explorar offers to go on with it.
 */
export const PENDING_CART = {
  'cafe-la-esquina': {
    businessName: 'Café La Esquina',
    savedAt: '2026-10-04T19:05:00.000Z',
    lines: [
      { productId: uuid('prod:americano'), optionIds: [], quantity: 1 },
      { productId: uuid('prod:concha'), optionIds: [], quantity: 2 },
    ],
  },
};

/** The account keeps no address yet. */
function noAddresses(data: DemoData) {
  data.accountAddresses = [];
}

/** Taquería El Güero also delivers, but not to Roma or Condesa: its card says it does not reach the postal code. */
function taqueriaDelivers(data: DemoData) {
  data.directory = demoDirectory().map((business: { slug: string }) => (business.slug === 'taqueria-el-guero'
    ? { ...business, deliveryAvailable: true }
    : business));
}

/** The croissant is sold out today: «Pedir otra vez» puts back the rest of the order. */
function croissantSoldOut(data: DemoData) {
  data.products.find((p: { name: string }) => p.name === 'Croissant de mantequilla').isAvailable = false;
}

/**
 * The store with a customer account (PublicOrderReceiptDto since the 24B): an order sent with the session is linked to
 * the account, and its receipt says so. Then the public menu, its quote and the rest of the public answers.
 */
const accountStoreRoutes: ExtraRoutes = (data, helpers) => [
  ['POST', /^\/public\/menu\/([^/]+)\/orders$/, (_match, _query, body: { expectedTotal?: number; delivery?: { postalCode: string } }) => helpers.ok({
    orderNumber: 'ORD-000361', total: body?.expectedTotal ?? 0,
    deliveryFee: body?.delivery ? deliveryQuote(body.delivery.postalCode).fee : null,
    confirmed: false, businessName: data.tenant.name, contactPhone: '5512345678',
    linkedToAccount: data.profile.role === 'Customer',
  })],
  ...publicRoutes(data, helpers),
];

export const SIN_DIRECCIONES = asOption(noAddresses);
export const TAQUERIA_A_DOMICILIO = asOption(taqueriaDelivers);
export const CROISSANT_AGOTADO = asOption(croissantSoldOut);
export const TIENDA_CON_CUENTA_ROUTES = asOption(accountStoreRoutes);
