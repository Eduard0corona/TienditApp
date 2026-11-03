import { useSyncExternalStore } from 'react';
import { getApiErrorMessage, getApiErrorStatus } from '../api/errors';
import { publicMenuApi, type CustomerAppointment } from '../api/publicMenuApi';
import type { PickedPhoto } from '../utils/problemPhotos';

export type UploadStatus = 'waiting' | 'sending' | 'sent' | 'failed';

/** A photo of what happens on its way to the visit of a link (Fase 29H). */
export interface PhotoUpload {
  id: string;
  file: File;
  status: UploadStatus;
  error?: string;
  /** It may go again: the connection or the server failed, not a refusal of the visit or of the photo. */
  retryable?: boolean;
}

export interface PhotoUploads {
  photos: PhotoUpload[];
  /** The visit as the server answered last: after a photo it took, or read again after one it refused. */
  latest: CustomerAppointment | null;
  /** The link stopped opening while sending: nothing more goes. */
  closed: boolean;
}

const NO_UPLOADS: PhotoUploads = { photos: [], latest: null, closed: false };

/**
 * The photos sent with one link, kept while the page lives: they keep going when the customer leaves the receipt for the
 * visit's link (or the receipt shows again), and both show the same ones. One at a time, in the order picked: the server
 * gives each the next place.
 */
class LinkUploads {
  private state: PhotoUploads = NO_UPLOADS;
  private readonly listeners = new Set<() => void>();
  private readonly answerListeners = new Set<(latest: CustomerAppointment) => void>();
  private running = false;
  private readonly token: string;

  constructor(token: string) {
    this.token = token;
  }

  subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  };

  getSnapshot = () => this.state;

  /**
   * Calls back with each answer of the server from now on: the visit after a photo it took, or read again after one it
   * refused. Never one from before, which a screen opened later reads for itself.
   */
  onAnswer = (listener: (latest: CustomerAppointment) => void) => {
    this.answerListeners.add(listener);
    return () => {
      this.answerListeners.delete(listener);
    };
  };

  /** Queues the photos not queued before: the same pick shown twice goes once. */
  enqueue(photos: PickedPhoto[]) {
    const known = new Set(this.state.photos.map((photo) => photo.id));
    const fresh = photos.filter((photo) => !known.has(photo.id));
    if (fresh.length === 0) return;
    this.set({ photos: [...this.state.photos, ...fresh.map(({ id, file }) => ({ id, file, status: 'waiting' as const }))] });
    void this.run();
  }

  /** Sends again the ones that failed for the connection or the server. */
  retry() {
    this.set({
      photos: this.state.photos.map((photo) => (
        photo.status === 'failed' && photo.retryable ? { ...photo, status: 'waiting', error: undefined } : photo)),
    });
    void this.run();
  }

  /** Lets go of one that failed and will not go. */
  dismiss(id: string) {
    this.set({ photos: this.state.photos.filter((photo) => photo.id !== id || photo.status !== 'failed') });
  }

  private set(change: Partial<PhotoUploads>) {
    this.state = { ...this.state, ...change };
    for (const listener of this.listeners) listener();
  }

  private mark(id: string, change: Partial<PhotoUpload>) {
    this.set({ photos: this.state.photos.map((photo) => (photo.id === id ? { ...photo, ...change } : photo)) });
  }

  private answered(latest: CustomerAppointment) {
    this.set({ latest });
    for (const listener of this.answerListeners) listener(latest);
  }

  private async run() {
    if (this.running) return;
    this.running = true;
    window.addEventListener('beforeunload', warnBeforeLeaving);
    try {
      for (let next = this.firstWaiting(); next; next = this.firstWaiting()) {
        const { id, file } = next;
        if (this.state.closed) {
          this.mark(id, { status: 'failed', error: CLOSED_MESSAGE, retryable: false });
          continue;
        }
        this.mark(id, { status: 'sending' });
        try {
          const latest = await publicMenuApi.addAppointmentPhoto(this.token, file);
          this.mark(id, { status: 'sent' });
          this.answered(latest);
        } catch (err: unknown) {
          const status = getApiErrorStatus(err);
          const retryable = status === undefined || status === 429 || status >= 500;
          this.mark(id, { status: 'failed', error: getApiErrorMessage(err, 'No se pudo mandar la foto. Intenta de nuevo.'), retryable });
          // Refused: the visit may have changed (full, cancelled, its time came); how it stands now is the server's to say.
          if (!retryable) await this.readAgain();
        }
      }
    } finally {
      this.running = false;
      window.removeEventListener('beforeunload', warnBeforeLeaving);
    }
  }

  private firstWaiting() {
    return this.state.photos.find((photo) => photo.status === 'waiting');
  }

  private async readAgain() {
    try {
      this.answered(await publicMenuApi.trackAppointment(this.token));
    } catch (err: unknown) {
      if (getApiErrorStatus(err) === 404) this.set({ closed: true });
    }
  }
}

const CLOSED_MESSAGE = 'El enlace de tu visita ya no abre.';

/** Leaving the page while photos are going would drop the ones left: the browser asks first. */
function warnBeforeLeaving(event: BeforeUnloadEvent) {
  event.preventDefault();
  event.returnValue = '';
}

const stores = new Map<string, LinkUploads>();

/** The uploads of a link, the same for every screen that shows it. */
export function linkUploads(token: string): LinkUploads {
  let store = stores.get(token);
  if (!store) {
    store = new LinkUploads(token);
    stores.set(token, store);
  }
  return store;
}

/** Forgets every link's uploads (for tests: in the app they live while the page does). */
export function resetLinkUploads() {
  stores.clear();
}

/** The photos going to the visit of a link, and how they went. */
export function useProblemPhotoUploads(token: string) {
  const store = linkUploads(token);
  const state = useSyncExternalStore(store.subscribe, store.getSnapshot);
  return { ...state, store };
}
