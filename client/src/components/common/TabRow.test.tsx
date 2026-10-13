import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { SHOWN, TAB, layOutTabRow, reducedMotion } from '../../test/tabRowLayout';
import TabRow from './TabRow';

// The parts of one screen, as Inventario has them: seven buttons with role tab, the first one open.
const PARTS = ['Insumos', 'Existencias', 'Entradas y salidas', 'Historial por insumo', 'Recetas', 'Ubicaciones', 'Alertas'];

function Parts({ first = PARTS[0] }: { first?: string }) {
  const [open, setOpen] = useState(first);
  return (
    <TabRow role="tablist" label="Partes del inventario" contentKey={PARTS.join()} activeKey={open}>
      {PARTS.map((part) => (
        <button
          key={part}
          type="button"
          role="tab"
          aria-selected={open === part}
          className={`section-tab${open === part ? ' active' : ''}`}
          onClick={() => setOpen(part)}
        >
          {part}
        </button>
      ))}
    </TabRow>
  );
}

let layout: ReturnType<typeof layOutTabRow>;

beforeEach(() => {
  layout = layOutTabRow();
});

afterEach(() => {
  layout.restore();
});

describe('TabRow as the parts of one screen', () => {
  it('holds only its tabs, with the buttons beside the tablist', () => {
    render(<Parts />);
    const row = screen.getByRole('tablist', { name: 'Partes del inventario' });

    expect(within(row).getAllByRole('tab')).toHaveLength(7);
    expect([...row.children].every((tab) => tab.getAttribute('role') === 'tab')).toBe(true);
    // The first part shows: only the end edge hides tabs, and its button sits outside the row.
    expect(row).toHaveClass('ta-scroll-fade', 'fade-end');
    expect(row).not.toHaveClass('fade-start');
    const next = screen.getByTitle('Ver las pestañas siguientes');
    expect(row).not.toContainElement(next);
    expect(next).toHaveAttribute('tabindex', '-1');
    // The frame holds the button, so it is positioned.
    expect(row.parentElement).toHaveClass('section-tabs-frame', 'slides');
    expect(layout.scrollTo).not.toHaveBeenCalled();
  });

  it('stays a plain frame, with neither fade nor buttons, when every tab fits', () => {
    layout.setShown(7 * TAB);
    render(<Parts />);
    const row = screen.getByRole('tablist');

    expect(row).not.toHaveClass('fade-start');
    expect(row).not.toHaveClass('fade-end');
    // Not positioned: it paints as the row did before it had a frame.
    expect(row.parentElement).toHaveClass('section-tabs-frame');
    expect(row.parentElement).not.toHaveClass('slides');
    expect(screen.queryByTitle(/Ver las pestañas/)).not.toBeInTheDocument();
  });

  it('slides a part cut by the edge into view when it opens', async () => {
    reducedMotion(false);
    render(<Parts />);

    // Entradas y salidas runs from 300 to 450 px, past the 400 that show.
    await userEvent.click(screen.getByRole('tab', { name: 'Entradas y salidas' }));

    expect(screen.getByRole('tab', { name: 'Entradas y salidas' })).toHaveAttribute('aria-selected', 'true');
    expect(layout.scrollTo).toHaveBeenCalledExactlyOnceWith({ left: 3 * TAB - SHOWN, behavior: 'smooth' });
  });

  it('brings it at once for whoever asks for less motion', async () => {
    reducedMotion(true);
    render(<Parts />);

    await userEvent.click(screen.getByRole('tab', { name: 'Entradas y salidas' }));

    expect(layout.scrollTo).toHaveBeenCalledExactlyOnceWith({ left: 3 * TAB - SHOWN, behavior: 'instant' });
  });

  it('leaves the row where it is when the part that opens already shows', async () => {
    render(<Parts />);

    await userEvent.click(screen.getByRole('tab', { name: 'Existencias' }));

    expect(layout.scrollTo).not.toHaveBeenCalled();
  });

  it('shows an open part at the end of the row as it draws, before paint', () => {
    reducedMotion(false);
    render(<Parts first="Alertas" />);

    expect(layout.scrollTo).toHaveBeenCalledExactlyOnceWith({ left: 7 * TAB - SHOWN, behavior: 'instant' });
    expect(screen.getByRole('tablist')).toHaveClass('fade-start');
  });
});
