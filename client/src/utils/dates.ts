/** "4 oct 2026, 12:30 p.m." in the browser's time zone. */
export function formatDateTime(value?: string | null): string {
  if (!value) return '-';
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? '-'
    : date.toLocaleString('es-MX', { dateStyle: 'medium', timeStyle: 'short' });
}

/** Value for an <input type="datetime-local"> in local time. */
export function toDateTimeLocalInput(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
