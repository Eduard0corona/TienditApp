// Phase 31L endpoints for the fake backend (ADR 0092): the photos and audios customers send by WhatsApp, kept privately and
// opened with the session (GET /files/whatsapp/...). The photo Valeria sent in her conversation (routes-p4) was kept; a
// scene file changes the messages in its `prepareData`.
import { readFileSync } from 'node:fs';
import path from 'node:path';

const HERE = path.dirname(new URL(import.meta.url).pathname);

/** Where the demo photo of the chat is kept. */
export const chatPhotoPath = (data) => `/files/whatsapp/${data.TENANT_ID}/202611/7a1d2c3b-4e5f-4a6b-8c7d-9e0f1a2b3c4d.webp`;

export function phase31lRoutes(data) {
  const photo = Object.values(data.messagesByConversation).flat().find((message) => message.id === 'mock-photo');
  if (photo && photo.mediaKind === undefined) {
    Object.assign(photo, { mediaKind: 'Image', mediaState: 'Saved', mediaPath: chatPhotoPath(data) });
  }

  return [
    ['GET', /^\/files\/(whatsapp\/.+\.webp)$/, () => ({
      status: 200,
      contentType: 'image/svg+xml',
      body: readFileSync(path.join(HERE, 'img', 'portada.svg')),
    })],
  ];
}
