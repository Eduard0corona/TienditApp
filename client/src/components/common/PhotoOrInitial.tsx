import { useState } from 'react';
import { initialOf } from '../../utils/initials';
import './PhotoOrInitial.css';

interface PhotoOrInitialProps {
  src?: string | null;
  /** The name shown next to it: the initial comes from it. */
  name: string;
  /** Size and shape, the same for the photo and the initial. */
  className: string;
  loading?: 'lazy' | 'eager';
}

/**
 * The photo of a product or a business, or, while it has none, its initial on the tint of the business's theme: a
 * list keeps its rows and tiles aligned. A photo that does not load (offline, deleted) gives way to the initial too.
 * Decoration only (the name is always next to it), so no alternative text.
 */
export default function PhotoOrInitial({ src, name, className, loading = 'lazy' }: PhotoOrInitialProps) {
  const [failedSrc, setFailedSrc] = useState<string | null>(null);
  if (src && src !== failedSrc) {
    return <img src={src} alt="" className={className} loading={loading} onError={() => setFailedSrc(src)} />;
  }
  return <span className={`${className} photo-initial`} aria-hidden="true">{initialOf(name)}</span>;
}
