// Fase 28F endpoints for the fake backend (ADR 0050): what the ticket of an order prints for the customer to invoice it
// online. Café La Esquina has self-invoicing off (204, as the API answers), so every ticket stays as it was; a scene that
// turns it on (`data.selfInvoicing = true`) gets the folio and the code of each sale, and the page of the business.

/** The code the API would print for the order: ten Crockford base32 characters, fixed for the scenes. */
const CODE = 'K7M2Q-9XH4C';

export function phase28fRoutes(data, { ok }) {
  return [
    ['GET', /^\/invoicing\/orders\/([^/]+)\/self-invoice$/, (match) => {
      const order = data.orders.find((o) => o.id === match[1]);
      if (!data.selfInvoicing || !order) return { status: 204, json: null };
      // Sold the scenes' day (October 4): it can be invoiced online until the end of the month.
      return ok({ path: `/m/${data.tenant.slug}/facturar`, orderNumber: order.orderNumber, code: CODE, invoiceBy: '2026-10-31' });
    }],
  ];
}
