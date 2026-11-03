import { waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { publicMenuApi } from '../api/publicMenuApi';
import { linkUploads, resetLinkUploads } from './useProblemPhotoUploads';

vi.mock('../api/publicMenuApi', () => ({ publicMenuApi: { addAppointmentPhoto: vi.fn(), trackAppointment: vi.fn() } }));

const api = vi.mocked(publicMenuApi);
const PLUMBER = 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE';
const PAINTER = 'ZyXwVuTsRqPoNmLkJiHgFeDcBa9876543210_-ZyXwV';

function picked(name: string) {
  return { id: name, file: new File([name], `${name}.jpg`, { type: 'image/jpeg' }) };
}

/** Whether the browser would ask before closing the page. */
function asksBeforeLeaving() {
  const event = new Event('beforeunload', { cancelable: true });
  window.dispatchEvent(event);
  return event.defaultPrevented;
}

describe('the photos of each link (Fase 29H)', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    resetLinkUploads();
  });

  afterEach(() => resetLinkUploads());

  it('asks before leaving while any link still sends, not only the first to finish (por la revisión automática)', async () => {
    let answerPainter: (visit: never) => void = () => {};
    api.addAppointmentPhoto.mockImplementation((token) => (token === PLUMBER
      ? Promise.resolve({ problemPhotos: 1, canAddPhotos: true } as never)
      : new Promise((resolve) => { answerPainter = resolve; })));

    linkUploads(PAINTER).enqueue([picked('pared')]);
    linkUploads(PLUMBER).enqueue([picked('fuga')]);
    await waitFor(() => expect(linkUploads(PLUMBER).getSnapshot().photos[0].status).toBe('sent'));

    // The plumber's link finished; the painter's still sends.
    expect(asksBeforeLeaving()).toBe(true);
    answerPainter({ problemPhotos: 1, canAddPhotos: true } as never);
    await waitFor(() => expect(linkUploads(PAINTER).getSnapshot().photos[0].status).toBe('sent'));

    expect(asksBeforeLeaving()).toBe(false);
  });
});
