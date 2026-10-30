import { isAxiosError } from 'axios';

// What a download of a file answered as a blob needs, for the screens of the business and the public pages alike.

/** The refusal of a download comes as a blob too: read back, its message (the PDF not available, say) reaches the person. */
export async function readBlobError(err: unknown): Promise<void> {
  if (isAxiosError(err) && err.response?.data instanceof Blob) {
    try {
      err.response.data = JSON.parse(await err.response.data.text()) as unknown;
    } catch {
      // Not JSON: the generic message stays.
    }
  }
}

/** The file's name as the response's Content-Disposition gives it, or the fallback. */
export function fileNameOf(contentDisposition: unknown, fallbackFileName: string): string {
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(String(contentDisposition ?? ''));
  return match ? decodeURIComponent(match[1]) : fallbackFileName;
}
