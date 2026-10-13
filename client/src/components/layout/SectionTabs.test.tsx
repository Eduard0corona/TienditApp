import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SHOWN, TAB, layOutTabRow, reducedMotion } from '../../test/tabRowLayout';
import SectionTabs from './SectionTabs';

// An owner of a business with every module (63): Configuración has all its pages.
vi.mock('../../hooks/useAuth', () => ({ useAuth: () => ({ user: { role: 'TENANT_ADMIN' } }) }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: () => ({ capabilities: 63 }) }));

let layout: ReturnType<typeof layOutTabRow>;

beforeEach(() => {
  layout = layOutTabRow();
});

afterEach(() => {
  layout.restore();
});

function renderAt(path: string) {
  render(
    <MemoryRouter initialEntries={[path]}>
      <SectionTabs />
    </MemoryRouter>,
  );
  return screen.getByRole('navigation');
}

describe('SectionTabs', () => {
  it('shows the open page tab when the page loads, even at the end of a long row', () => {
    const row = renderAt('/setup');
    const tabs = within(row).getAllByRole('link');

    const last = tabs[tabs.length - 1];
    expect(last).toHaveAccessibleName('Primeros pasos');
    expect(last).toHaveAttribute('aria-current', 'page');
    // Straight to the end of the row, where the last tab shows whole.
    expect(layout.scrollTo).toHaveBeenCalledExactlyOnceWith({ left: tabs.length * TAB - SHOWN, behavior: 'instant' });
    // At the end only the start edge hides tabs.
    expect(row).toHaveClass('fade-start');
    expect(row).not.toHaveClass('fade-end');
    expect(screen.getByTitle('Ver las pestañas anteriores')).toBeInTheDocument();
    expect(screen.queryByTitle('Ver las pestañas siguientes')).not.toBeInTheDocument();
  });

  it('slides a page with the button on the edge that hides tabs', async () => {
    reducedMotion(false);
    const row = renderAt('/settings/business');

    // Its tab, the first, already shows: the row stays at the start.
    expect(layout.scrollTo).not.toHaveBeenCalled();
    expect(row).toHaveClass('fade-end');
    await userEvent.click(screen.getByTitle('Ver las pestañas siguientes'));

    // The third tab (300 to 450 px) was cut at 400: the next page starts with it.
    expect(layout.scrollTo).toHaveBeenCalledWith({ left: 2 * TAB, behavior: 'smooth' });
  });

  it('slides without motion for whoever asks for less', async () => {
    reducedMotion(true);
    renderAt('/settings/business');

    await userEvent.click(screen.getByTitle('Ver las pestañas siguientes'));

    expect(layout.scrollTo).toHaveBeenCalledWith({ left: 2 * TAB, behavior: 'instant' });
  });

  it('keeps the buttons out of the keyboard and of screen readers, which reach every tab', () => {
    renderAt('/settings/business');

    expect(screen.getByTitle('Ver las pestañas siguientes')).toHaveAttribute('tabindex', '-1');
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('neither fades nor shows buttons when every tab fits', () => {
    // Wider than every page of Configuración side by side.
    layout.setShown(20 * TAB);
    const row = renderAt('/settings/business');

    expect(row).not.toHaveClass('fade-start');
    expect(row).not.toHaveClass('fade-end');
    expect(screen.queryByTitle(/Ver las pestañas/)).not.toBeInTheDocument();
  });
});
