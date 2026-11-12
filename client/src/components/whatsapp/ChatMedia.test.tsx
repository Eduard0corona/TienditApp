import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { filesApi } from '../../api/filesApi';
import type { ChatMessageDto } from '../../types';
import ChatMedia from './ChatMedia';

vi.mock('../../api/filesApi', () => ({
  filesApi: { get: vi.fn() },
}));

const api = vi.mocked(filesApi);
const photoPath = '/files/whatsapp/0b6f1c7e-2d1a-4c3b-9e8f-1a2b3c4d5e6f/202611/9f8e7d6c-5b4a-4321-8765-0fedcba98765.webp';
const audioPath = '/files/whatsapp/0b6f1c7e-2d1a-4c3b-9e8f-1a2b3c4d5e6f/202611/1a2b3c4d-5b4a-4321-8765-0fedcba98765.ogg';

const message = (overrides: Partial<ChatMessageDto>): ChatMessageDto => ({
  id: 'm1', conversationId: 'c1', direction: 'Inbound', body: '📷 Foto', status: 'Delivered', sentAt: '2026-11-12T18:00:00Z',
  ...overrides,
});

describe('ChatMedia (Fase 31L)', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    // jsdom has no object URLs.
    URL.createObjectURL = vi.fn(() => 'blob:medio');
    URL.revokeObjectURL = vi.fn();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('shows the photo of the customer fetched with the session, and opens it whole', async () => {
    api.get.mockResolvedValue({ data: new Blob(['webp'], { type: 'image/webp' }) } as Awaited<ReturnType<typeof filesApi.get>>);

    render(<ChatMedia message={message({ mediaKind: 'Image', mediaState: 'Saved', mediaPath: photoPath })} />);

    await waitFor(() => expect(screen.getByRole('img', { name: 'Foto del cliente' })).toHaveAttribute('src', 'blob:medio'));
    expect(screen.getByRole('link', { name: 'Abrir Foto del cliente' })).toHaveAttribute('href', 'blob:medio');
    expect(api.get).toHaveBeenCalledWith(photoPath);
  });

  it('plays the voice note of the customer from memory', async () => {
    api.get.mockResolvedValue({ data: new Blob(['OggS'], { type: 'audio/ogg' }) } as Awaited<ReturnType<typeof filesApi.get>>);

    const { container } = render(<ChatMedia message={message({ body: '🎤 Nota de voz', mediaKind: 'Audio', mediaState: 'Saved', mediaPath: audioPath })} />);

    await waitFor(() => expect(container.querySelector('audio')).toHaveAttribute('src', 'blob:medio'));
    expect(container.querySelector('audio')).toHaveAttribute('aria-label', 'Audio del cliente');
    expect(container.querySelector('audio')).toHaveAttribute('controls');
    expect(api.get).toHaveBeenCalledWith(audioPath);
  });

  it('says what is on its way while it downloads, and nothing more when only its marker stays', () => {
    const { rerender, container } = render(<ChatMedia message={message({ mediaKind: 'Image', mediaState: 'Pending' })} />);
    expect(screen.getByRole('status')).toHaveTextContent('Descargando la foto…');

    rerender(<ChatMedia message={message({ body: '🎤 Nota de voz', mediaKind: 'Audio', mediaState: 'Pending' })} />);
    expect(screen.getByRole('status')).toHaveTextContent('Descargando el audio…');

    rerender(<ChatMedia message={message({ mediaKind: 'Image', mediaState: 'Unavailable' })} />);
    expect(container).toBeEmptyDOMElement();
    expect(api.get).not.toHaveBeenCalled();
  });

  it('says the audio could not be opened when its file is gone', async () => {
    api.get.mockRejectedValue(new Error('404'));

    render(<ChatMedia message={message({ body: '🎵 Audio', mediaKind: 'Audio', mediaState: 'Saved', mediaPath: audioPath })} />);

    expect(await screen.findByText('No se pudo abrir el audio.')).toBeInTheDocument();
  });
});
