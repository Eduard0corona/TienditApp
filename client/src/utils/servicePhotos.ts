import type { ServiceOrderPhotoDto } from '../types';
import { formatDateTime } from './dates';

/** The longest note a photo of a service takes (ServiceOrderPhoto.MaxNoteLength): the server checks it too. */
export const SERVICE_PHOTO_NOTE_MAX_LENGTH = 300;

export const servicePhotosQueryKey = (orderId: string) => ['orders', 'service-photos', orderId] as const;

/** Who took a photo of a service and when, or that it came with the order (Fase 25). */
export function describeServicePhoto(photo: ServiceOrderPhotoDto): string {
  if (photo.atIntake) return 'Al recibir la orden';
  return [photo.takenBy, photo.takenAt ? formatDateTime(photo.takenAt) : null].filter(Boolean).join(' · ');
}
