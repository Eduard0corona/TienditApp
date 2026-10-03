/**
 * Only files served by the app (relative /uploads paths) or https storage URLs are rendered as links or
 * images. Anything else (javascript:, data:, http:, other schemes) is treated as missing.
 */
export function isSafeFileUrl(url: string | null | undefined): url is string {
  if (!url) return false;
  const value = url.trim();
  if (value.startsWith('/uploads/')) return true;
  try {
    return new URL(value).protocol === 'https:';
  } catch {
    return false;
  }
}
