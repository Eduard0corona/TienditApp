import PrivatePhoto from '../common/PrivatePhoto';
import { usePrivateFile } from '../../hooks/usePrivateFile';
import type { ChatMessageDto } from '../../types';

/**
 * The photo or audio a customer sent by WhatsApp (Fase 31L, ADR 0092), above its text: fetched with the session from the
 * business's private files, never from a public address. While it downloads it says so; one that could not be kept shows
 * nothing more than its marker, which is the message's text.
 */
export default function ChatMedia({ message }: { message: ChatMessageDto }) {
  if (message.mediaState === 'Pending') {
    return (
      <p className="small text-muted mb-1" role="status">
        <i className="ri-download-cloud-2-line me-1" aria-hidden="true"></i>
        {message.mediaKind === 'Audio' ? 'Descargando el audio…' : 'Descargando la foto…'}
      </p>
    );
  }
  if (message.mediaState !== 'Saved' || !message.mediaPath) return null;

  return message.mediaKind === 'Audio'
    ? <ChatAudio path={message.mediaPath} />
    : (
      <PrivatePhoto
        path={message.mediaPath}
        alt="Foto del cliente"
        linked
        className="d-block rounded mb-1"
        style={{ width: 240, maxWidth: '100%', aspectRatio: '4 / 3' }}
      />
    );
}

function ChatAudio({ path }: { path: string }) {
  const file = usePrivateFile(path);
  if (file.failed) {
    return <p className="small text-muted mb-1">No se pudo abrir el audio.</p>;
  }
  if (!file.url) {
    return (
      <span
        className="d-block placeholder-glow bg-light rounded mb-1"
        style={{ width: 240, maxWidth: '100%', height: 40 }}
        aria-busy="true"
        aria-label="Audio del cliente"
        role="img"
      ></span>
    );
  }
  // A customer's voice note comes without captions, and none can be written for it: the message's text says what it is.
  return (
    // eslint-disable-next-line jsx-a11y/media-has-caption
    <audio controls preload="metadata" src={file.url} className="d-block mb-1" style={{ width: 260, maxWidth: '100%' }} aria-label="Audio del cliente">
      Tu navegador no reproduce este audio; ábrelo en WhatsApp.
    </audio>
  );
}
