import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { categoriesApi } from '../../api/categoriesApi';
import { productsApi } from '../../api/productsApi';
import { UnitType, type CategoryDto, type ProductDto } from '../../types';
import ProductsList from './ProductsList';

const dialogs = vi.hoisted(() => ({ confirm: vi.fn() }));

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => dialogs }));
vi.mock('../../api/categoriesApi', () => ({ categoriesApi: { getAll: vi.fn() } }));
vi.mock('../../api/productsApi', () => ({
  productsApi: { getAll: vi.fn(), delete: vi.fn(), toggleAvailability: vi.fn() },
}));

const categories = [
  { id: 'coffee', name: 'Café en grano' },
  { id: 'bakery', name: 'Panadería' },
] as unknown as CategoryDto[];

function product(patch: Partial<ProductDto>): ProductDto {
  return {
    id: 'p', name: 'Producto', categoryId: 'coffee', priceAmount: 100, priceCurrency: 'MXN', sortOrder: 0, unit: UnitType.Unit,
    hasDiscount: false, isTaxExempt: false, isAvailable: true, tracksStock: false, isLowStock: false,
    ...patch,
  } as ProductDto;
}

/**
 * Coffee on sale with its photo and plenty of stock, bread sold by the kilo running low, a sold-out mug with a schedule,
 * and a gift card of a category no longer listed that keeps no stock.
 */
const coffee = product({
  id: 'chiapas', name: 'Café de Chiapas 250 g', sku: 'CAF-CHI-250', priceAmount: 180, compareAtPrice: 220, hasDiscount: true,
  images: { thumbnail: '/uploads/chiapas-thumb.webp' } as ProductDto['images'], tracksStock: true, onHand: 42,
});
const bread = product({ id: 'bread', name: 'Pan de masa madre', categoryId: 'bakery', priceAmount: 95.5, unit: UnitType.Kg, tracksStock: true, onHand: 1.25, isLowStock: true });
const mug = product({
  id: 'mug', name: 'Taza de barro', priceAmount: 150, tracksStock: true, onHand: 0, isLowStock: true,
  availableFromTime: '08:00:00', availableToTime: '20:30:00', isAvailable: false,
});
// As the server sends it: a product that keeps no stock has no figure of it.
const giftCard = product({ id: 'gift', name: 'Tarjeta de regalo', categoryId: 'gone', priceAmount: 500, compareAtPrice: 600, hasDiscount: false, onHand: null });

const listPage = (items: ProductDto[], totalCount = items.length) =>
  ({ data: { items, pageNumber: 1, totalPages: Math.ceil(totalCount / 10), totalCount, hasPreviousPage: false, hasNextPage: totalCount > 10 } }) as never;

const row = async (name: string) => (await screen.findByRole('link', { name })).closest('tr') as HTMLElement;
const renderList = () => render(<MemoryRouter><ProductsList /></MemoryRouter>);

describe('ProductsList', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(categoriesApi.getAll).mockResolvedValue({ data: categories } as never);
    vi.mocked(productsApi.getAll).mockResolvedValue(listPage([coffee, bread, mug, giftCard]));
  });

  it('lists each product with its photo, SKU, category, price, stock and schedule', async () => {
    renderList();

    const cells = (tr: HTMLElement) => within(tr).getAllByRole('cell').slice(1, 6).map((cell) => cell.textContent);
    // The category names arrive after the products: the row waits for them.
    await waitFor(async () => expect(cells(await row('Café de Chiapas 250 g')))
      .toEqual(['Café de Chiapas 250 gSKU: CAF-CHI-250', 'Café en grano', '$220.00$180.00', '42', 'Todo el día']));
    expect(cells(await row('Pan de masa madre'))).toEqual(['Pan de masa madre', 'Panadería', '$95.50', '1.25 kg', 'Todo el día']);
    expect(cells(await row('Taza de barro'))).toEqual(['Taza de barro', 'Café en grano', '$150.00', '0', '08:00 - 20:30']);
    // A price it had before shows struck through only while the product is on sale.
    expect(cells(await row('Tarjeta de regalo'))).toEqual(['Tarjeta de regalo', '-', '$500.00', '-', 'Todo el día']);

    expect(screen.getByRole('img', { name: 'Café de Chiapas 250 g' })).toHaveAttribute('src', '/uploads/chiapas-thumb.webp');
    expect(screen.getByRole('img', { name: 'Pan de masa madre' })).toHaveAttribute('src', '/images/placeholders/product.svg');
    expect(within(await row('Café de Chiapas 250 g')).getByText('$220.00')).toHaveClass('text-decoration-line-through');
    // Sold out, low or enough, by color and by its title.
    expect(within(await row('Taza de barro')).getByText('0', { selector: '.badge' })).toHaveAttribute('title', 'Agotado');
    expect(within(await row('Taza de barro')).getByText('0', { selector: '.badge' })).toHaveClass('bg-danger');
    expect(within(await row('Pan de masa madre')).getByText('1.25 kg')).toHaveAttribute('title', 'Poca existencia');
    expect(within(await row('Pan de masa madre')).getByText('1.25 kg')).toHaveClass('bg-warning');
    expect(within(await row('Café de Chiapas 250 g')).getByText('42', { selector: '.badge' })).toHaveClass('bg-success');
    expect(within(await row('Café de Chiapas 250 g')).getByText('42', { selector: '.badge' })).not.toHaveAttribute('title');
    expect(within(await row('Tarjeta de regalo')).getByText('-', { selector: 'span' })).toHaveAttribute('title', 'No controla existencias');

    expect(screen.getByLabelText('Disponible: Café de Chiapas 250 g')).toBeChecked();
    expect(screen.getByLabelText('Disponible: Taza de barro')).not.toBeChecked();
    expect(productsApi.getAll).toHaveBeenCalledWith({ pageNumber: 1, pageSize: 10, itemType: 'Product' });
  });

  it('leads to the form of a new product and of each one', async () => {
    renderList();

    expect(within(await row('Pan de masa madre')).getByRole('link', { name: 'Editar Pan de masa madre' })).toHaveAttribute('href', '/products/bread/edit');
    expect(screen.getByRole('link', { name: 'Pan de masa madre' })).toHaveAttribute('href', '/products/bread/edit');
    expect(screen.getByRole('link', { name: 'Nuevo producto' })).toHaveAttribute('href', '/products/new');
  });

  it('asks the server for the category, the availability and what is searched, from the first page', async () => {
    const user = userEvent.setup();
    vi.mocked(productsApi.getAll).mockResolvedValue(listPage([coffee, bread], 25));
    renderList();
    await row('Café de Chiapas 250 g');
    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
    await waitFor(() => expect(productsApi.getAll).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 2 })));

    // Each filter takes the list back to its first page.
    await user.selectOptions(screen.getByLabelText('Categoría'), 'bakery');
    await waitFor(() => expect(productsApi.getAll).toHaveBeenLastCalledWith({ pageNumber: 1, pageSize: 10, itemType: 'Product', categoryId: 'bakery' }));
    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
    await user.selectOptions(screen.getByLabelText('Disponibilidad'), 'false');
    await waitFor(() => expect(productsApi.getAll).toHaveBeenLastCalledWith({
      pageNumber: 1, pageSize: 10, itemType: 'Product', categoryId: 'bakery', isAvailable: false,
    }));
    await user.click(screen.getByRole('button', { name: 'Página siguiente' }));
    await user.type(screen.getByLabelText('Nombre, SKU o código de barras'), 'CAF-CHI');
    await waitFor(() => expect(productsApi.getAll).toHaveBeenLastCalledWith({
      pageNumber: 1, pageSize: 10, itemType: 'Product', search: 'CAF-CHI', categoryId: 'bakery', isAvailable: false,
    }));
  });

  it('turns a product on or off and reads the list again, and says so when it cannot', async () => {
    const user = userEvent.setup();
    vi.mocked(productsApi.toggleAvailability).mockResolvedValueOnce({ data: {} } as never).mockRejectedValueOnce(new Error('Network Error'));
    renderList();
    await row('Taza de barro');

    await user.click(screen.getByLabelText('Disponible: Taza de barro'));
    expect(productsApi.toggleAvailability).toHaveBeenCalledWith('mug', true);
    await waitFor(() => expect(productsApi.getAll).toHaveBeenCalledTimes(2));

    await user.click(screen.getByLabelText('Disponible: Café de Chiapas 250 g'));
    expect(productsApi.toggleAvailability).toHaveBeenLastCalledWith('chiapas', false);
    expect(await screen.findByText('Error al cambiar la disponibilidad.')).toBeInTheDocument();
  });

  it('deletes a product only once confirmed, and says why when the server refuses', async () => {
    const user = userEvent.setup();
    vi.mocked(productsApi.delete)
      .mockResolvedValueOnce({ data: {} } as never)
      .mockRejectedValueOnce({ isAxiosError: true, response: { status: 409, data: { error: 'Taza de barro está en un combo activo.' } } });
    dialogs.confirm.mockResolvedValueOnce(false).mockResolvedValue(true);
    renderList();

    await user.click(within(await row('Pan de masa madre')).getByRole('button', { name: 'Eliminar Pan de masa madre' }));
    expect(dialogs.confirm).toHaveBeenCalledWith(expect.objectContaining({
      message: 'Se eliminará “Pan de masa madre” del catálogo. Las ventas anteriores no cambian.', danger: true,
    }));
    expect(productsApi.delete).not.toHaveBeenCalled();

    await user.click(within(await row('Pan de masa madre')).getByRole('button', { name: 'Eliminar Pan de masa madre' }));
    expect(productsApi.delete).toHaveBeenCalledWith('bread');
    await waitFor(() => expect(productsApi.getAll).toHaveBeenCalledTimes(2));

    await user.click(within(await row('Taza de barro')).getByRole('button', { name: 'Eliminar Taza de barro' }));
    expect(await screen.findByText('Taza de barro está en un combo activo.')).toBeInTheDocument();
  });

  it('says when the products could not be read', async () => {
    vi.mocked(productsApi.getAll).mockRejectedValue(new Error('Network Error'));
    renderList();

    expect(await screen.findByText('Error al cargar los productos.')).toBeInTheDocument();
  });
});
