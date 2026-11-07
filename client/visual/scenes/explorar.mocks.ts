import { asOption, type DemoData } from '../fixtures';
import { demoDirectory } from '../mocks/routes-p9.mjs';

// OnlineMenuMode, BusinessCategory and OrderConfirmTime as the contract numbers them.
const ORDERS = 2;
const FOOD = 0;
const CONFIRMS_WITHIN_FIVE_MINUTES = 0;

/** No business published its menu yet. */
function emptyDirectory(data: DemoData) {
  data.directory = [];
}

/**
 * The demo business takes orders online, so Explorar lists it; it confirms them within five minutes (Fase 29T, ADR 0074),
 * which its owner reads under the preview of its card.
 */
function menuWithOrders(data: DemoData) {
  data.tenant.onlineMenuMode = ORDERS;
  data.directoryProfile = {
    category: FOOD, suggestedCategory: FOOD, description: 'Café de especialidad, pan dulce y chilaquiles desde 2021.', listed: true,
    countsOrders: true, orderConfirmTime: CONFIRMS_WITHIN_FIVE_MINUTES,
  };
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

/** She keeps every business of the demo city and the bakery: a wide screen shows two rows and «Ver todos». */
function withManyFavorites(data: DemoData) {
  withFavorites(data);
  // The directory is filled by its routes after this runs: the same demo city.
  data.directory ??= demoDirectory();
  const kept = new Set(data.favorites.map((favorite: { slug: string }) => favorite.slug));
  data.favorites = [
    ...data.directory
      .filter((business: { slug: string }) => !kept.has(business.slug))
      .map((business: { slug: string; name: string; logoUrl: string | null }, index: number) => ({
        slug: business.slug, name: business.name, logoUrl: business.logoUrl, savedAt: `2026-10-0${3 - Math.min(index, 2)}T12:00:00.000Z`,
      })),
    ...data.favorites,
  ];
}

export const CON_FAVORITOS = asOption(withFavorites);
export const MUCHOS_FAVORITOS = asOption(withManyFavorites);
