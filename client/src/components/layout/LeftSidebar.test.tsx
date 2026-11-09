import { act, fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import LeftSidebar from './LeftSidebar';

vi.mock('../../hooks/useAuth', () => ({ useAuth: () => ({ user: { role: 'TENANT_ADMIN' } }) }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: () => ({ tenant: { name: 'Café La Esquina', status: 0 }, capabilities: null }) }));

function Page() {
  return <p>Pantalla {useLocation().pathname}</p>;
}

function renderMenu() {
  render(
    <MemoryRouter initialEntries={['/dashboard']}>
      <LeftSidebar onClose={() => {}} />
      <Routes>
        <Route path="*" element={<Page />} />
      </Routes>
    </MemoryRouter>,
  );
  return screen.getByRole('navigation', { name: 'Menú principal' });
}

describe('LeftSidebar', () => {
  // jsdom's window never has the focus: as in a browser, this one does unless a test says otherwise.
  beforeEach(() => {
    vi.spyOn(document, 'hasFocus').mockReturnValue(true);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  // Fase 30F: in the condensed menu the keyboard grows a link to its name (shell.css). Nothing moves the focus when the
  // page changes, so the link just used says so and stops growing until the focus leaves it.
  it('marks the link just used while it keeps the focus', async () => {
    const menu = renderMenu();
    const links = menu.querySelectorAll('a.ta-nav-link');
    expect(links.length).toBeGreaterThan(1);
    const [first, second] = [links[0] as HTMLAnchorElement, links[1] as HTMLAnchorElement];
    expect(first).not.toHaveAttribute('data-used');

    act(() => first.focus());
    await userEvent.keyboard('{Enter}');
    expect(screen.getByText(`Pantalla ${first.getAttribute('href')}`)).toBeInTheDocument();
    expect(first).toHaveFocus();
    expect(first).toHaveAttribute('data-used');

    await userEvent.tab();
    expect(second).toHaveFocus();
    expect(first).not.toHaveAttribute('data-used');
    expect(second).not.toHaveAttribute('data-used');
  });

  it('does not mark a link a click did not focus (Safari, Firefox on macOS), as no blur would take the mark away', () => {
    const menu = renderMenu();
    const second = menu.querySelectorAll('a.ta-nav-link')[1] as HTMLAnchorElement;

    fireEvent.click(second);
    expect(second).not.toHaveFocus();
    expect(second).not.toHaveAttribute('data-used');
  });

  it('keeps the mark while the focus is in another window, where the link gets it back', async () => {
    const menu = renderMenu();
    const first = menu.querySelector('a.ta-nav-link') as HTMLAnchorElement;
    act(() => first.focus());
    await userEvent.keyboard('{Enter}');
    expect(first).toHaveAttribute('data-used');

    // Another app takes the focus: the link gets a blur, but it is still the active element...
    fireEvent.blur(first);
    expect(first).toHaveAttribute('data-used');
    // ...or the document no longer has the focus (how each browser shows it).
    vi.mocked(document.hasFocus).mockReturnValue(false);
    act(() => first.blur());
    expect(first).toHaveAttribute('data-used');
  });
});
