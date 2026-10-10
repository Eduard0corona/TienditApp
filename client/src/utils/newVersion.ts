/**
 * Whether the server has a newer build than the page running: index.html loads one script whose name carries the
 * hash of the build (`/assets/index-<hash>.js`), so a deploy changes it. The API serves index.html checked again on
 * every visit (ClientDelivery.cs), so asking for it is cheap and always current.
 */

const ENTRY_SCRIPT = /\/assets\/index-[\w-]+\.js/;

/** The entry script that a copy of index.html loads, or null. */
export function entryScriptOf(html: string): string | null {
  return ENTRY_SCRIPT.exec(html)?.[0] ?? null;
}

/** The entry script of this page; null in development, where Vite serves the sources. */
export function runningEntryScript(doc: Document = document): string | null {
  for (const script of Array.from(doc.querySelectorAll<HTMLScriptElement>('script[type="module"][src]'))) {
    const found = entryScriptOf(new URL(script.getAttribute('src') ?? '', doc.baseURI).pathname);
    if (found) return found;
  }
  return null;
}

/** Asks for index.html and tells whether it loads another build than `running`. False when it cannot tell. */
export async function isNewVersionDeployed(running: string): Promise<boolean> {
  const response = await fetch('/', { cache: 'no-store', headers: { Accept: 'text/html' } });
  if (!response.ok) return false;
  const deployed = entryScriptOf(await response.text());
  return deployed !== null && deployed !== running;
}
