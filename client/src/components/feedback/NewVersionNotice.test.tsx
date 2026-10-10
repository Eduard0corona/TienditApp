import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CHECK_EVERY_MS, MIN_GAP_MS } from '../../hooks/useNewVersion';
import NewVersionNotice from './NewVersionNotice';

const page = (build: string) => new Response(`<script type="module" crossorigin src="/assets/index-${build}.js"></script>`);

describe('NewVersionNotice', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] });
    document.head.innerHTML = '<script type="module" crossorigin src="/assets/index-old111.js"></script>';
  });

  afterEach(() => {
    vi.useRealTimers();
    document.head.innerHTML = '';
  });

  async function comeBackToTheTab() {
    await act(async () => {
      vi.advanceTimersByTime(MIN_GAP_MS);
      window.dispatchEvent(new Event('focus'));
      document.dispatchEvent(new Event('visibilitychange'));
    });
  }

  it('does not ask right after loading, which got the current version', () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch');

    render(<NewVersionNotice />);
    window.dispatchEvent(new Event('focus'));

    expect(fetchMock).not.toHaveBeenCalled();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it('says there is a new version once the server has another build, and reloads on request', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(page('new222'));
    const reload = vi.fn();
    vi.spyOn(window, 'location', 'get').mockReturnValue({ ...window.location, reload });
    render(<NewVersionNotice />);

    await comeBackToTheTab();

    // Focus and visibility come together: one question.
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(await screen.findByText('Hay una versión nueva de TienditApp')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Actualizar' }));
    expect(reload).toHaveBeenCalled();
  });

  it('stays quiet while the server has the same build, and keeps asking while the page is open', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(page('old111'));
    render(<NewVersionNotice />);

    await comeBackToTheTab();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();

    fetchMock.mockResolvedValue(page('new222'));
    await act(async () => {
      vi.advanceTimersByTime(CHECK_EVERY_MS);
    });

    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(await screen.findByRole('status')).toHaveTextContent('Hay una versión nueva de TienditApp');
  });

  it('can be closed, and asks nothing in development', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(page('new222'));
    const { unmount } = render(<NewVersionNotice />);
    await comeBackToTheTab();
    fireEvent.click(await screen.findByRole('button', { name: 'Cerrar aviso' }));
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
    unmount();

    document.head.innerHTML = '<script type="module" src="/src/main.tsx"></script>';
    const fetchMock = vi.spyOn(globalThis, 'fetch');
    fetchMock.mockClear();
    render(<NewVersionNotice />);
    await comeBackToTheTab();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
