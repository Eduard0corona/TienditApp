import type { CSSProperties } from 'react';
import { usePrivateFile } from '../../hooks/usePrivateFile';

interface PrivatePhotoProps {
  /** A private file path ("/files/orders/..."). */
  path: string;
  alt: string;
  /** Size and shape of the thumbnail. */
  style?: CSSProperties;
  className?: string;
  /** Opens the full photo in a new tab when clicked. */
  linked?: boolean;
}

/** A photo the business keeps privately: fetched with the session, never from a public address. */
export default function PrivatePhoto({ path, alt, style, className, linked = false }: PrivatePhotoProps) {
  const file = usePrivateFile(path);

  if (file.failed) {
    return (
      <span className={`d-inline-flex align-items-center justify-content-center bg-light text-muted small ${className ?? ''}`} style={style} role="img" aria-label={`${alt}: no disponible`}>
        <i className="ri-image-line" aria-hidden="true"></i>
      </span>
    );
  }

  if (!file.url) {
    return <span className={`d-inline-block placeholder-glow bg-light ${className ?? ''}`} style={style} aria-busy="true" aria-label={alt} role="img"></span>;
  }

  const image = <img src={file.url} alt={alt} style={{ width: '100%', height: '100%', objectFit: 'cover' }} />;
  if (!linked) {
    return <span className={`d-inline-block overflow-hidden ${className ?? ''}`} style={style}>{image}</span>;
  }

  // A blob address of this page: no rel, since a browsing context without opener cannot open it.
  return (
    <a href={file.url} target="_blank" className={`d-inline-block overflow-hidden ${className ?? ''}`} style={style} aria-label={`Abrir ${alt}`}>
      {image}
    </a>
  );
}
