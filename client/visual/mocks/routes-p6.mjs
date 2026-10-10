// Phase 5A endpoints for the fake backend: product stock in the inventory ledger (one stock model).
import { uuid } from './data.mjs';

// Products that control their stock, with what their item has: low when on hand <= reorder point.
const TRACKED = {
  croissant: { onHand: 14, reorderPoint: 6 },
  concha: { onHand: 24, reorderPoint: 10 },
  elote: { onHand: 5, reorderPoint: 8 },
  chiapas250: { onHand: 14, reorderPoint: 5 },
  veracruz500: { onHand: 0, reorderPoint: 5 },
};

function applyStock(product, state) {
  product.tracksStock = !!state;
  product.stockItemId = state ? uuid(`stock:${product.id}`) : null;
  product.onHand = state ? state.onHand : null;
  product.reorderPoint = state ? state.reorderPoint : null;
  product.isLowStock = state ? state.onHand <= state.reorderPoint : false;
}

function seedPhase5a(data) {
  if (data.p5a) return;
  for (const product of data.products) {
    const key = Object.keys(TRACKED).find((k) => product.id === uuid(`prod:${k}`));
    applyStock(product, key ? { ...TRACKED[key] } : null);
  }
  data.p5a = true;
}

function stockDto(data, product) {
  const location = data.locations.find((l) => l.isDefault) ?? data.locations[0];
  return {
    productId: product.id,
    tracksStock: product.tracksStock,
    stockItemId: product.stockItemId,
    onHand: product.onHand ?? 0,
    reorderPoint: product.reorderPoint ?? 0,
    isLowStock: product.isLowStock,
    locations: product.tracksStock ? [{ inventoryLocationId: location.id, locationName: location.name, onHand: product.onHand }] : [],
    capturedStockQuantity: product.stockQuantity ?? null,
    capturedLowStockThreshold: product.lowStockThreshold ?? null,
  };
}

export function phase5aRoutes(data, { ok, notFound }) {
  seedPhase5a(data);
  const find = (id) => data.products.find((p) => p.id === id);
  return [
    ['GET', /^\/inventory\/products\/([^/]+)\/stock$/, (m) => {
      const product = find(m[1]);
      return product ? ok(stockDto(data, product)) : notFound('Producto');
    }],
    ['POST', /^\/inventory\/products\/([^/]+)\/stock$/, (m, q, body) => {
      const product = find(m[1]);
      if (!product) return notFound('Producto');
      if (product.tracksStock) return { status: 400, json: { error: 'Este producto ya controla sus existencias.' } };
      applyStock(product, { onHand: body.initialQuantity ?? 0, reorderPoint: body.reorderPoint ?? 0 });
      return ok(stockDto(data, product));
    }],
    ['DELETE', /^\/inventory\/products\/([^/]+)\/stock$/, (m) => {
      const product = find(m[1]);
      if (!product) return notFound('Producto');
      applyStock(product, null);
      return ok(stockDto(data, product));
    }],
  ];
}
