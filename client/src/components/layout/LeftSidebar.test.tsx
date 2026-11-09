import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
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
});
