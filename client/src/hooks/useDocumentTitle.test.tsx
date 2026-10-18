import { render } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { useDocumentTitle } from './useDocumentTitle';

function Page({ title }: { title: string | null }) {
  useDocumentTitle(title);
  return null;
}

describe('useDocumentTitle', () => {
  afterEach(() => {
    document.title = 'TienditApp';
  });

  it('titles the tab while the page is open, and gives back the title before when it closes', () => {
    document.title = 'TienditApp';
    const { rerender, unmount } = render(<Page title={null} />);
    // Still loading: the title the server wrote stays.
    expect(document.title).toBe('TienditApp');

    rerender(<Page title="Café Luna: pide en línea · TienditApp" />);
    expect(document.title).toBe('Café Luna: pide en línea · TienditApp');

    unmount();
    expect(document.title).toBe('TienditApp');
  });
});
