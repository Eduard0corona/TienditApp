import { useEffect } from 'react';

/** The app's own title: the one of index.html, for every page that does not title itself. */
export const APP_TITLE = 'TienditApp';

/**
 * The title of the browser tab while a page is open, back to the app's own when it closes. The public pages use the
 * title the server already wrote for search engines (Fase 24D-2, PublicPages.cs), so it does not change on arrival; it
 * is also why closing one goes back to the app's title, not to the one the tab had (the page's own).
 * Without a title (the page is still loading) it leaves the current one.
 */
export function useDocumentTitle(title: string | null | undefined): void {
  useEffect(() => {
    if (!title) return undefined;
    document.title = title;
    return () => {
      document.title = APP_TITLE;
    };
  }, [title]);
}
