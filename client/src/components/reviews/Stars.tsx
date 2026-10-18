import { starsLabel } from '../../utils/reviews';
import './Reviews.css';

/**
 * A rating as five stars (Fase 24H): a whole one for each star, half of one from the half up (an average of 4.6 shows four
 * and a half). The screen reader hears «4.6 de 5 estrellas».
 */
export default function Stars({ rating, className = '', decorative = false }: {
  rating: number;
  className?: string;
  /** Beside the number it shows: the screen reader hears the number once. */
  decorative?: boolean;
}) {
  const halves = Math.round(rating * 2);
  return (
    <span
      className={`rv-stars ${className}`}
      {...(decorative ? { 'aria-hidden': true } : { role: 'img', 'aria-label': starsLabel(rating) })}
    >
      {[1, 2, 3, 4, 5].map((star) => {
        const icon = halves >= star * 2 ? 'ri-star-fill' : halves === star * 2 - 1 ? 'ri-star-half-line' : 'ri-star-line';
        return <i key={star} className={`${icon} ${icon === 'ri-star-line' ? 'rv-star-off' : 'rv-star-on'}`} aria-hidden="true"></i>;
      })}
    </span>
  );
}
