import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { filesApi } from '../../api/filesApi';
import PrivatePhoto from './PrivatePhoto';

vi.mock('../../api/filesApi', () => ({
  filesApi: { get: vi.fn() },
}));

const api = vi.mocked(filesApi);
const path = '/files/orders/0b6f1c7e-2d1a-4c3b-9e8f-1a2b3c4d5e6f/service-intake/202610/9f8e7d6c-5b4a-4321-8765-0fedcba98765.webp';

describe('PrivatePhoto', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    // jsdom has no object URLs.
    URL.createObjectURL = vi.fn(() => 'blob:foto');
    URL.revokeObjectURL = vi.fn();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('fetches the photo with the session and shows it from memory, never from a public address', async () => {
    api.get.mockResolvedValue({ data: new Blob(['webp'], { type: 'image/webp' }) } as Awaited<ReturnType<typeof filesApi.get>>);

    const { unmount } = render(<PrivatePhoto path={path} alt="Foto al recibir" linked />);

    // While it loads a busy placeholder takes its place.
    await waitFor(() => expect(screen.getByRole('img', { name: 'Foto al recibir' })).toHaveAttribute('src', 'blob:foto'));
    expect(screen.getByRole('link', { name: 'Abrir Foto al recibir' })).toHaveAttribute('href', 'blob:foto');
    expect(api.get).toHaveBeenCalledWith(path);

    unmount();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:foto');
  });

  it('says the photo is not available when it cannot be opened', async () => {
    api.get.mockRejectedValue(new Error('404'));

    render(<PrivatePhoto path={path} alt="Foto al recibir" />);

    await waitFor(() => expect(screen.getByRole('img', { name: 'Foto al recibir: no disponible' })).toBeInTheDocument());
  });
});
