import { fireEvent, render, screen } from '@testing-library/react';
import { createRef, type ComponentProps } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { CfdiTaxObject, UnitType, type CategoryDto, type ProductDto } from '../../types';
import CatalogPanel from './CatalogPanel';

function product(overrides: Partial<ProductDto>): ProductDto {
  return {
    id: 'taco',
    categoryId: 'c1',
    name: 'Taco',
    sortOrder: 1,
    priceAmount: 25,
    priceCurrency: 'MXN',
    isTaxExempt: false,
    hasDiscount: false,
    taxObject: CfdiTaxObject.Subject,
    isAvailable: true,
    isFeatured: false,
    tracksStock: false,
    isLowStock: false,
    isTrackable: false,
    notifyCustomer: false,
    unit: UnitType.Unit,
    ...overrides,
  };
}

const photo = { original: 'o', large: 'l', medium: 'm', small: 's', thumbnail: 'https://cdn/gringa_th.webp' };

function renderPanel(visibleProducts: ProductDto[], categories: CategoryDto[] = []) {
  const props: ComponentProps<typeof CatalogPanel> = {
    searchInputRef: createRef<HTMLInputElement>(),
    search: '',
    onSearchChange: vi.fn(),
    onSearchKeyDown: vi.fn(),
    categoryFilter: 'all',
    onCategoryChange: vi.fn(),
    hasFeatured: false,
    categories,
    loadError: '',
    onRetry: vi.fn(),
    catalogTruncated: false,
    loading: false,
    hasProducts: true,
    visibleProducts,
    cart: [],
    onAdd: vi.fn(),
    canManageCatalog: false,
  };
  return render(<MemoryRouter><CatalogPanel {...props} /></MemoryRouter>);
}

describe('CatalogPanel', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('fades the edge of the categories that hides more of them', () => {
    // jsdom lays nothing out: a row of 900 px seen through 400 px.
    vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(400);
    vi.spyOn(HTMLElement.prototype, 'scrollWidth', 'get').mockReturnValue(900);
    const categories = ['Tacos', 'Tortas', 'Bebidas', 'Postres', 'Salsas'].map((name, index) => ({ id: `c${index}`, name }) as CategoryDto);
    renderPanel([product({})], categories);

    const row = screen.getByRole('tablist', { name: 'Categorías' });
    expect(row).toHaveClass('fade-end');
    expect(row).not.toHaveClass('fade-start');

    row.scrollLeft = 500;
    fireEvent.scroll(row);
    expect(row).toHaveClass('fade-start');
    expect(row).not.toHaveClass('fade-end');
  });


  it('puts the initial where a product has no photo while others have one, so the names line up', () => {
    renderPanel([product({ id: 'gringa', name: 'Gringa', images: photo }), product({ id: 'taco', name: 'Taco' })]);

    const gringa = screen.getByRole('button', { name: /Agregar Gringa/ });
    const taco = screen.getByRole('button', { name: /Agregar Taco/ });
    expect(gringa.querySelector('img')).toHaveAttribute('src', 'https://cdn/gringa_th.webp');
    expect(taco.querySelector('.photo-initial')).toHaveTextContent('T');
  });

  it('shows only names and prices while no product has a photo', () => {
    renderPanel([product({ id: 'taco', name: 'Taco' }), product({ id: 'agua', name: 'Agua de horchata' })]);

    expect(screen.getByRole('button', { name: /Agregar Taco/ }).querySelector('.photo-initial, img')).toBeNull();
    expect(screen.getByRole('button', { name: /Agregar Agua de horchata/ }).querySelector('.photo-initial, img')).toBeNull();
  });
});
