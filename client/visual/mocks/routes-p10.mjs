// Phase 24B endpoints for the fake backend: the addresses a customer account keeps (GET, POST, PUT and DELETE
// /me/addresses), the last one an order used first. They are `data.accountAddresses`; a scene file changes them in its
// `prepareData`.

/** The two addresses of María Fernanda, the demo customer: her home in Roma Norte (used last week) and her office. */
export function demoAddresses() {
  return [
    {
      id: 'dir-casa', label: 'Casa', postalCode: '06700', addressLine: 'Colima 145, depto. 3, Roma Norte',
      references: 'Portón negro, timbre 3', lastUsedAt: '2026-09-27T19:40:00.000Z',
    },
    {
      id: 'dir-oficina', label: 'Oficina', postalCode: '06140', addressLine: 'Ámsterdam 210, Condesa',
      references: null, lastUsedAt: null,
    },
  ];
}

export function phase24bRoutes(data, { ok, notFound }) {
  data.accountAddresses ??= demoAddresses();
  const read = (body, id) => ({
    id,
    label: body.label?.trim() || 'Dirección',
    postalCode: body.postalCode,
    addressLine: body.addressLine.trim(),
    references: body.references?.trim() || null,
    lastUsedAt: null,
  });

  return [
    ['GET', /^\/me\/addresses$/, () => ok(data.accountAddresses)],
    ['POST', /^\/me\/addresses$/, (m, q, body) => {
      const saved = read(body, `dir-${data.accountAddresses.length + 1}`);
      data.accountAddresses = [...data.accountAddresses, saved];
      return ok(saved);
    }],
    ['PUT', /^\/me\/addresses\/([^/]+)$/, (m, q, body) => {
      const current = data.accountAddresses.find((address) => address.id === m[1]);
      if (!current) return notFound('Dirección');
      const saved = { ...read(body, current.id), lastUsedAt: current.lastUsedAt };
      data.accountAddresses = data.accountAddresses.map((address) => (address.id === current.id ? saved : address));
      return ok(saved);
    }],
    ['DELETE', /^\/me\/addresses\/([^/]+)$/, (m) => {
      data.accountAddresses = data.accountAddresses.filter((address) => address.id !== m[1]);
      return { status: 204, json: null };
    }],
  ];
}
