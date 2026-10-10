import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { initialOf } from '../../utils/initials';
import PhotoOrInitial from './PhotoOrInitial';

describe('PhotoOrInitial', () => {
  it('shows the photo, or the initial in the colors of the business while there is none', () => {
    const { container, rerender } = render(<PhotoOrInitial src="https://cdn/latte.webp" name="Latte" className="thumb" />);
    expect(container.querySelector('img.thumb')).toHaveAttribute('src', 'https://cdn/latte.webp');

    rerender(<PhotoOrInitial src={null} name="  capuchino" className="thumb" />);
    const initial = container.querySelector('.thumb.photo-initial');
    expect(initial).toHaveTextContent('C');
    // The name is next to it: the initial is decoration.
    expect(initial).toHaveAttribute('aria-hidden', 'true');
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });

  it('falls back to the initial when the photo does not load (offline, deleted)', () => {
    const { container } = render(<PhotoOrInitial src="https://cdn/latte.webp" name="Latte" className="thumb" />);

    fireEvent.error(container.querySelector('img')!);

    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('.thumb.photo-initial')).toHaveTextContent('L');
  });

  it('takes the first letter even when it is accented or an emoji', () => {
    expect(initialOf('ñoquis')).toBe('Ñ');
    expect(initialOf('2x1 tacos')).toBe('2');
    expect(initialOf('🌮 Tacos')).toBe('🌮');
    expect(initialOf('   ')).toBe('');
  });
});
