import { useEffect } from 'react';

/**
 * The title of the browser tab while a page is open, back to the one before when it closes. The public pages use the
 * title the server already wrote for search engines (Fase 24D-2, PublicPages.cs), so it does not change on arrival.
 * Without a title (the page is still loading) it leaves the current one.
 */
export function useDocumentTitle(title: string | null | undefined): void {
  useEffect(() => {
    if (!title) return undefined;
    const previous = document.title;
    document.title = title;
    return () => {
      document.title = previous;
    };
  }, [title]);
}
