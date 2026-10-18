import { render } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { APP_TITLE, useDocumentTitle } from './useDocumentTitle';

function Page({ title }: { title: string | null }) {
  useDocumentTitle(title);
  return null;
}

describe('useDocumentTitle', () => {
  afterEach(() => {
    document.title = APP_TITLE;
  });

  it('titles the tab while the page is open, and gives back the title of the app when it closes', () => {
    document.title = APP_TITLE;
    const { rerender, unmount } = render(<Page title={null} />);
    // Still loading: the title the server wrote stays.
    expect(document.title).toBe('TienditApp');

    rerender(<Page title="Café Luna: pide en línea · TienditApp" />);
    expect(document.title).toBe('Café Luna: pide en línea · TienditApp');

    unmount();
    expect(document.title).toBe('TienditApp');
  });

  it('does not keep the title the server wrote for the page once the page closes', () => {
    // The server wrote the page's own title in index.html (Fase 24D-2): leaving it goes back to the app's.
    document.title = 'Explorar · TienditApp';
    const { unmount } = render(<Page title="Explorar · TienditApp" />);
    expect(document.title).toBe('Explorar · TienditApp');

    unmount();
    expect(document.title).toBe('TienditApp');
  });
});
