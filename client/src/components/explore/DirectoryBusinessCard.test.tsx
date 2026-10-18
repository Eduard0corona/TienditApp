import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { BusinessCategory } from '../../api/generated/enums';
import type { DirectoryBusiness } from '../../api/publicDirectoryApi';
import DirectoryBusinessCard from './DirectoryBusinessCard';

function business(overrides: Partial<DirectoryBusiness>): DirectoryBusiness {
  return {
    name: 'Taquería El Güero',
    slug: 'el-guero',
    category: BusinessCategory.Food,
    theme: 0,
    deliveryOnly: false,
    deliveryAvailable: false,
    pricesIncludeTax: true,
    booksOnline: false,
    acceptsOrders: false,
    ordersPaused: false,
    closed: false,
    ...overrides,
  };
}

function renderCard(card: DirectoryBusiness) {
  render(<MemoryRouter><DirectoryBusinessCard business={card} /></MemoryRouter>);
}

describe('DirectoryBusinessCard', () => {
  it('shows the rating of a business from three reviews, read as a sentence', () => {
    renderCard(business({ rating: { count: 23, average: 4.6 } }));

    const link = screen.getByRole('link');
    expect(link).toHaveTextContent('4.6 (23)');
    expect(link).toHaveAccessibleName(expect.stringContaining('Calificación 4.6 de 5, 23 reseñas'));
  });

  it('shows no rating with fewer than three reviews, as the API sends no average', () => {
    renderCard(business({ rating: { count: 2, average: null } }));

    expect(screen.getByRole('link')).not.toHaveTextContent('reseña');
    expect(screen.getByRole('link')).not.toHaveTextContent('(2)');
  });
});
