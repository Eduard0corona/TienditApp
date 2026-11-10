import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import SellNotices from './SellNotices';

describe('SellNotices', () => {
  it('says nothing with connection and the drawer open', () => {
    const { container } = render(<SellNotices offlineCatalogAt={null} cashClosed={false} hasPlatforms={false} onOpenDrawer={vi.fn()} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('says Vender sells with the catalog saved without connection', () => {
    render(<SellNotices offlineCatalogAt="2026-11-10T16:30:00Z" cashClosed={false} hasPlatforms={false} onOpenDrawer={vi.fn()} />);
    expect(screen.getByRole('status')).toHaveTextContent(/^Sin conexión: vendes con el catálogo guardado \(.+\)\. Las ventas cobradas se guardan/);
  });

  it('warns of a closed drawer, as the platforms let it register orders without one, and opens it', async () => {
    const onOpenDrawer = vi.fn();
    const { rerender } = render(<SellNotices offlineCatalogAt={null} cashClosed hasPlatforms={false} onOpenDrawer={onOpenDrawer} />);
    expect(screen.getByRole('status')).toHaveTextContent(
      'Tu caja está cerrada: ábrela para cobrar. Todo cobro se registra en una caja; sin ella solo puedes guardar pedidos.');
    await userEvent.click(screen.getByRole('button', { name: 'Abrir caja' }));
    expect(onOpenDrawer).toHaveBeenCalledTimes(1);

    rerender(<SellNotices offlineCatalogAt={null} cashClosed hasPlatforms onOpenDrawer={onOpenDrawer} />);
    expect(screen.getByRole('status')).toHaveTextContent(
      'Tu caja está cerrada: ábrela para cobrar. Sin ella puedes guardar pedidos y registrar los que ya cobró una plataforma.');
  });
});
