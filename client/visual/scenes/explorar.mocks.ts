import { asOption, type DemoData } from '../fixtures';

// OnlineMenuMode as the contract numbers it.
const ORDERS = 2;

/** No business published its menu yet. */
function emptyDirectory(data: DemoData) {
  data.directory = [];
}

/** The demo business takes orders online, so Explorar lists it. */
function menuWithOrders(data: DemoData) {
  data.tenant.onlineMenuMode = ORDERS;
}

export const SIN_NEGOCIOS = asOption(emptyDirectory);
export const MENU_CON_PEDIDOS = asOption(menuWithOrders);

/**
 * María Fernanda keeps three businesses (Fase 24F): the taquería since yesterday, Café La Esquina and a bakery that no
 * longer shows in Explorar (it stays with its name).
 */
function withFavorites(data: DemoData) {
  data.favorites = [
    { slug: 'taqueria-el-guero', name: 'Taquería El Güero', logoUrl: null, savedAt: '2026-10-03T20:10:00.000Z' },
    { slug: 'cafe-la-esquina', name: 'Café La Esquina', logoUrl: '/visual-img/logo.svg', savedAt: '2026-09-28T16:00:00.000Z' },
    { slug: 'panaderia-san-juan', name: 'Panadería San Juan', logoUrl: null, savedAt: '2026-09-12T15:30:00.000Z' },
  ];
}

export const CON_FAVORITOS = asOption(withFavorites);
