/** The photos of what happens a visit requested online takes (Fase 29H, ADR 0062), as the server says. */
export const MAX_PROBLEM_PHOTOS = 5;
export const MAX_PHOTO_BYTES = 10 * 1024 * 1024;
export const ACCEPTED_PHOTO_TYPES = ['image/jpeg', 'image/png', 'image/webp'];
// Some Android file providers leave the type empty: the name says it, and the server reads what the file really is.
const PHOTO_NAME = /\.(?:jpe?g|png|webp)$/i;

/** A photo the customer picked on this device. */
export interface PickedPhoto {
  id: string;
  file: File;
}

let nextId = 0;

function isPhoto(file: File): boolean {
  return ACCEPTED_PHOTO_TYPES.includes(file.type) || (file.type === '' && PHOTO_NAME.test(file.name));
}

/**
 * The photos that fit of the ones picked, and why the others stay out: what is not a JPG, PNG or WebP, what weighs more than
 * 10 MB, or what does not fit among the five. The server checks again what each one really is.
 */
export function pickPhotos(files: File[], room: number): { picked: PickedPhoto[]; refused: string | null } {
  const reasons: string[] = [];
  const fit: File[] = [];
  for (const file of files) {
    if (!isPhoto(file)) reasons.push(`${file.name} no es una foto JPG, PNG o WebP.`);
    else if (file.size > MAX_PHOTO_BYTES) reasons.push(`${file.name} pesa más de 10 MB.`);
    else fit.push(file);
  }
  const kept = fit.slice(0, Math.max(room, 0));
  if (fit.length > kept.length) {
    const left = fit.length - kept.length;
    reasons.push(`Caben ${MAX_PROBLEM_PHOTOS} fotos: ${left === 1 ? 'una se quedó fuera' : `${left} se quedaron fuera`}.`);
  }
  return { picked: kept.map((file) => ({ id: `foto-${++nextId}`, file })), refused: reasons.length > 0 ? reasons.join(' ') : null };
}

const previews = new WeakMap<File, string>();

/**
 * The preview of a picked photo, only on this device: one per file while the page lives (also when a screen shows it again),
 * let go of when the photo is taken out.
 */
export function previewOf(file: File): string {
  let url = previews.get(file);
  if (!url) {
    url = URL.createObjectURL(file);
    previews.set(file, url);
  }
  return url;
}

/** Lets go of the preview of a photo taken out. */
export function forgetPreview(file: File): void {
  const url = previews.get(file);
  if (!url) return;
  URL.revokeObjectURL(url);
  previews.delete(file);
}

/** «2 fotos», «una foto». */
export function photoCount(count: number): string {
  return count === 1 ? 'una foto' : `${count} fotos`;
}
