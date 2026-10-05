import { toPrivateFilePath } from './privateFiles';

/** One line of the intake checklist of a service (stored as JSON by the intake form). */
export interface ChecklistItem {
  label?: string;
  checked?: boolean;
}

/** A photo of a service: before (intake) or after (delivery). */
export interface ServicePhoto {
  /** Private file path ("/files/orders/..."): the photo opens only with the session. */
  path: string;
  fileName?: string;
}

function safeParseArray<T>(raw?: string | null): T[] {
  if (!raw) return [];
  try {
    const parsed = JSON.parse(raw) as unknown;
    return Array.isArray(parsed) ? (parsed as T[]) : [];
  } catch {
    return [];
  }
}

export function parseChecklist(raw?: string | null): ChecklistItem[] {
  return safeParseArray<ChecklistItem>(raw);
}

/** Photos stored as addresses or { url, fileName }: only the business's own private photos are shown. */
export function parsePhotos(raw?: string | null): ServicePhoto[] {
  return safeParseArray<{ url?: string; fileName?: string } | string>(raw)
    .map((item) => (typeof item === 'string' ? { url: item } : item))
    .flatMap((item) => {
      const path = toPrivateFilePath(item.url);
      return path ? [{ path, fileName: item.fileName }] : [];
    });
}

/**
 * Priority of a service in words. Orders store 1 to 5 (older intakes used five steps); the intake now offers
 * Baja (1), Normal (3) and Urgente (5).
 */
export function getServicePriorityLabel(priority: number | null | undefined): string {
  if (!priority || priority <= 1) return 'Baja';
  if (priority >= 4) return 'Urgente';
  return 'Normal';
}

export const SERVICE_PRIORITY_OPTIONS = [
  { value: 1, label: 'Baja' },
  { value: 3, label: 'Normal' },
  { value: 5, label: 'Urgente' },
] as const;

/** "10 min", "2 h 5 min" or "3 días": how late a service is, or how long until its promise. */
export function formatDuration(minutes: number): string {
  const total = Math.max(0, Math.round(minutes));
  if (total < 60) return `${total} min`;
  if (total < 24 * 60) {
    const hours = Math.floor(total / 60);
    const rest = total % 60;
    return rest === 0 ? `${hours} h` : `${hours} h ${rest} min`;
  }
  const days = Math.floor(total / (24 * 60));
  return `${days} ${days === 1 ? 'día' : 'días'}`;
}
