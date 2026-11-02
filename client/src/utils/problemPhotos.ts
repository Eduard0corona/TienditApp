/** The photos of what happens a visit requested online takes (Fase 29H, ADR 0062), as the server says. */
export const MAX_PROBLEM_PHOTOS = 5;
export const MAX_PHOTO_BYTES = 10 * 1024 * 1024;
export const ACCEPTED_PHOTO_TYPES = ['image/jpeg', 'image/png', 'image/webp'];

/** A photo the customer picked on this device, with the preview only this page shows. */
export interface PickedPhoto {
  id: string;
  file: File;
  preview: string;
}

let nextId = 0;

/**
 * The photos that fit of the ones picked, and why the others stay out: what is not a JPG, PNG or WebP, what weighs more than
 * 10 MB, or what does not fit among the five. The server checks again what each one really is.
 */
export function pickPhotos(files: File[], room: number): { picked: PickedPhoto[]; refused: string | null } {
  const reasons: string[] = [];
  const fit: File[] = [];
  for (const file of files) {
    if (!ACCEPTED_PHOTO_TYPES.includes(file.type)) reasons.push(`${file.name} no es una foto JPG, PNG o WebP.`);
    else if (file.size > MAX_PHOTO_BYTES) reasons.push(`${file.name} pesa más de 10 MB.`);
    else fit.push(file);
  }
  const kept = fit.slice(0, Math.max(room, 0));
  if (fit.length > kept.length) {
    const left = fit.length - kept.length;
    reasons.push(`Caben ${MAX_PROBLEM_PHOTOS} fotos: ${left === 1 ? 'una se quedó fuera' : `${left} se quedaron fuera`}.`);
  }
  const picked = kept.map((file) => ({ id: `foto-${++nextId}`, file, preview: URL.createObjectURL(file) }));
  return { picked, refused: reasons.length > 0 ? reasons.join(' ') : null };
}

/** Lets go of the previews of photos no longer shown. */
export function forgetPhotos(photos: PickedPhoto[]): void {
  for (const photo of photos) URL.revokeObjectURL(photo.preview);
}

/** «2 fotos», «una foto». */
export function photoCount(count: number): string {
  return count === 1 ? 'una foto' : `${count} fotos`;
}
