// Phase 25 endpoints for the fake backend: the photos of a service order (GET, POST and DELETE
// /orders/{id}/service-photos), the upload of one (POST /orders/uploads/service-photo) and the private files they open
// (GET /files/orders/...), from `data.servicePhotos`; a scene file changes them in its `prepareData`.
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { at } from './data.mjs';

const HERE = path.dirname(new URL(import.meta.url).pathname);
// ServicePhotoStage as the contract numbers it.
const BEFORE = 0;
const AFTER = 1;
const CANCELLED = 99;
// Delivered, finished or cancelled: its photos stay.
const CLOSED = [6, 7, CANCELLED];

const fileOf = (data, name) => `/files/orders/${data.TENANT_ID}/service-intake/202610/${name}.webp`;

/**
 * The photos of the demo services: the De'Longhi (ORD-000337) came with the sarro of its regadera and Carlos added the
 * one after descaling it; the Oster (ORD-000342) has the dent of its side, added when it was received.
 */
export function demoServicePhotos(data) {
  const order = (n) => data.orders.find((o) => o.orderNumber === `ORD-${String(n).padStart(6, '0')}`);
  return {
    [order(337).id]: [
      {
        stage: BEFORE, url: fileOf(data, '5c0f8a3e-1d2b-4c5d-8e9f-0a1b2c3d4e01'), fileName: 'regadera.jpg', atIntake: true,
        canRemove: false,
      },
      {
        id: 'foto-337-despues', stage: AFTER, url: fileOf(data, '5c0f8a3e-1d2b-4c5d-8e9f-0a1b2c3d4e02'), fileName: 'regadera-limpia.jpg',
        note: 'Regadera sin sarro', takenAt: at(11, 40), takenBy: 'Carlos Méndez', atIntake: false, canRemove: true,
      },
    ],
    [order(342).id]: [
      {
        id: 'foto-342-golpe', stage: BEFORE, url: fileOf(data, '5c0f8a3e-1d2b-4c5d-8e9f-0a1b2c3d4e03'), fileName: 'carcasa.jpg',
        note: 'Golpe en la carcasa lateral', takenAt: at(12, 9), takenBy: data.OWNER_EMAIL, atIntake: false, canRemove: true,
      },
    ],
  };
}

/** The picture each demo photo shows: the ones taken before, with sarro; the one after, clean. */
const pictureOf = (url) => (url.endsWith('4e02.webp') ? 'servicio-despues.svg' : 'servicio-antes.svg');

export function phase25Routes(data, { ok, notFound }) {
  data.servicePhotos ??= demoServicePhotos(data);
  const orderOf = (id) => data.orders.find((o) => o.id === id);

  return [
    ['GET', /^\/orders\/([^/]+)\/service-photos$/, (m) => {
      const order = orderOf(m[1]);
      if (!order) return notFound('Orden');
      const open = !CLOSED.includes(order.status);
      const photos = (data.servicePhotos[order.id] ?? []).map((photo) => ({ ...photo, canRemove: photo.canRemove && open }));
      return ok({ orderId: order.id, canAdd: order.status !== CANCELLED, canAddBefore: open, photos });
    }],
    ['POST', /^\/orders\/uploads\/service-photo$/, () => ok({
      url: fileOf(data, `5c0f8a3e-1d2b-4c5d-8e9f-${String(Object.values(data.servicePhotos).flat().length + 10).padStart(12, '0')}`),
      fileName: 'foto.jpg',
      contentType: 'image/webp',
    })],
    ['POST', /^\/orders\/([^/]+)\/service-photos$/, (m, q, body) => {
      const order = orderOf(m[1]);
      if (!order) return notFound('Orden');
      const photo = {
        id: `foto-${order.orderNumber}-${(data.servicePhotos[order.id] ?? []).length + 1}`, stage: body?.stage ?? AFTER,
        url: body?.fileUrl, fileName: body?.fileName ?? null, note: body?.note ?? null, takenAt: at(12, 30),
        takenBy: data.OWNER_EMAIL, atIntake: false, canRemove: true,
      };
      data.servicePhotos[order.id] = [...(data.servicePhotos[order.id] ?? []), photo];
      return ok(photo);
    }],
    ['DELETE', /^\/orders\/([^/]+)\/service-photos\/([^/]+)$/, (m) => {
      data.servicePhotos[m[1]] = (data.servicePhotos[m[1]] ?? []).filter((photo) => photo.id !== m[2]);
      return { status: 204, json: null };
    }],
    ['GET', /^\/files\/(orders\/.+\/service-intake\/.+)$/, (m) => ({
      status: 200,
      contentType: 'image/svg+xml',
      body: readFileSync(path.join(HERE, 'img', pictureOf(m[1]))),
    })],
  ];
}
