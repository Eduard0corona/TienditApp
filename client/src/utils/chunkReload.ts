const RELOAD_KEY = 'tienditapp:new-version-reload-at';
// A deploy that is really broken must not reload the page in a loop.
const RELOAD_WINDOW_MS = 60_000;

/** The browser could not load a piece of the app: usually the page is older than the last deploy. */
export function isChunkLoadError(error: unknown): boolean {
  const text = error instanceof Error ? `${error.name} ${error.message}` : String(error);
  return /Failed to fetch dynamically imported module|Importing a module script failed|error loading dynamically imported module|Unable to preload CSS|ChunkLoadError/i.test(text);
}

/** Reloads to get the new version, at most once a minute. False when it already tried (or cannot tell). */
export function reloadOnceForNewVersion(): boolean {
  try {
    const last = Number(window.sessionStorage.getItem(RELOAD_KEY) ?? 0);
    if (Date.now() - last < RELOAD_WINDOW_MS) return false;
    window.sessionStorage.setItem(RELOAD_KEY, String(Date.now()));
  } catch {
    // Without storage there is no way to stop a loop: show the error page instead.
    return false;
  }
  window.location.reload();
  return true;
}
