// Phase 31J endpoints for the fake backend: the carts a customer account leaves in the stores (GET /me/carts, PUT and
// DELETE /me/carts/{slug}) and its postal code (GET and PUT /me/postal-code). They are `data.accountCarts` (as the API
// answers them) and `data.accountPostalCode`; a scene file changes them in its `prepareData`.
import { NOW_ISO } from './data.mjs';

export function phase31jRoutes(data, { ok }) {
  data.accountCarts ??= [];
  data.accountPostalCode ??= null;

  return [
    ['GET', /^\/me\/carts$/, () => ok(data.accountCarts)],
    ['PUT', /^\/me\/carts\/([^/]+)$/, (m, q, body) => {
      const business = data.directory.find((candidate) => candidate.slug === m[1]);
      if (!business) {
        return { status: 404, json: { status: 404, title: 'Not Found', detail: 'Este negocio no tiene menú en línea.', code: 'not_found' } };
      }
      data.accountCarts = data.accountCarts.filter((cart) => cart.slug !== business.slug);
      const lines = (body?.lines ?? []).map((line) => ({ ...line, notes: line.notes ?? null }));
      if (lines.length === 0) return { status: 204, json: null };
      const cart = {
        slug: business.slug, businessName: business.name, savedAt: NOW_ISO, lines,
        reorderOf: body?.reorderOf ?? null, skipped: body?.skipped ?? null,
      };
      data.accountCarts = [cart, ...data.accountCarts];
      return ok(cart);
    }],
    ['DELETE', /^\/me\/carts\/([^/]+)$/, (m) => {
      data.accountCarts = data.accountCarts.filter((cart) => cart.slug !== m[1]);
      return { status: 204, json: null };
    }],
    ['GET', /^\/me\/postal-code$/, () => ok({ postalCode: data.accountPostalCode })],
    ['PUT', /^\/me\/postal-code$/, (m, q, body) => {
      data.accountPostalCode = body?.postalCode || null;
      return ok({ postalCode: data.accountPostalCode });
    }],
  ];
}
