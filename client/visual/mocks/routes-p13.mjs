// Phase 24F endpoints for the fake backend: the favorite businesses of a customer account (GET /me/favorites with the
// postal code, PUT and DELETE /me/favorites/{slug}), the last one kept first, each with its card of Explorar while it
// shows there. They are `data.favorites` (slug, name, logo and when it was kept); a scene file changes them in its
// `prepareData`.
import { NOW_ISO } from './data.mjs';
import { withDeliveryTo } from './routes-p9.mjs';

export function phase24fRoutes(data, { ok, notFound }) {
  data.favorites ??= [];

  return [
    ['GET', /^\/me\/favorites$/, (m, q) => ok(data.favorites.map((favorite) => {
      const card = data.directory.find((business) => business.slug === favorite.slug);
      return { ...favorite, card: card ? withDeliveryTo(data, [card], q.get('postalCode'))[0] : null };
    }))],
    ['PUT', /^\/me\/favorites\/([^/]+)$/, (m) => {
      const business = data.directory.find((candidate) => candidate.slug === m[1]);
      if (!business) return notFound('Negocio');
      if (!data.favorites.some((favorite) => favorite.slug === business.slug)) {
        data.favorites = [{ slug: business.slug, name: business.name, logoUrl: business.logoUrl, savedAt: NOW_ISO }, ...data.favorites];
      }
      return { status: 204, json: null };
    }],
    ['DELETE', /^\/me\/favorites\/([^/]+)$/, (m) => {
      data.favorites = data.favorites.filter((favorite) => favorite.slug !== m[1]);
      return { status: 204, json: null };
    }],
  ];
}
