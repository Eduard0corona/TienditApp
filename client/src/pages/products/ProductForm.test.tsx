import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ProductOptionGroupInput } from '../../api/productsApi';
import { categoriesApi } from '../../api/categoriesApi';
import { productsApi } from '../../api/productsApi';
import { useAuth } from '../../hooks/useAuth';
import { useTenant } from '../../hooks/useTenant';
import { CfdiTaxObject, TenantCapability, UnitType, type CategoryDto, type ProductDto } from '../../types';
import ProductForm from './ProductForm';

const toast = vi.hoisted(() => ({ success: vi.fn(), warning: vi.fn(), error: vi.fn() }));

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../components/common/UploadLoadingModal', () => ({ default: () => null }));
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));
vi.mock('../../hooks/useAuth', () => ({ useAuth: vi.fn() }));
vi.mock('../../hooks/useTenant', () => ({ useTenant: vi.fn() }));
vi.mock('../../api/categoriesApi', () => ({ categoriesApi: { getAll: vi.fn() } }));
vi.mock('../../api/productsApi', () => ({
  productsApi: { getById: vi.fn(), create: vi.fn(), update: vi.fn(), setOptionGroups: vi.fn(), uploadImage: vi.fn() },
}));
// The stock card reads its own stock and has its own tests: here only whether it may manage it.
vi.mock('../../components/catalog/ProductStockCard', () => ({
  default: ({ canManage }: { canManage: boolean }) => <p>{canManage ? 'Existencias: puede ajustar' : 'Existencias: solo ver'}</p>,
}));
// The editor of the options is a part of its own: the form only keeps what it hands back and saves it.
vi.mock('../../components/catalog/ProductOptionsEditor', () => ({
  default: ({ groups, onChange }: { groups: ProductOptionGroupInput[]; onChange: (groups: ProductOptionGroupInput[]) => void }) => (
    <div>
      <p>{groups.length === 1 ? '1 grupo de opciones' : `${groups.length} grupos de opciones`}</p>
      <button type="button" onClick={() => onChange([{
        id: null, name: '  Tamaño ', minSelections: 1, maxSelections: 1,
        options: [{ id: null, name: ' Grande ', priceDelta: 15, isAvailable: true }],
      }])}>Agregar tamaños</button>
      <button type="button" onClick={() => onChange([{ id: null, name: '   ', minSelections: 0, maxSelections: 1, options: [] }])}>
        Agregar un grupo sin nombre
      </button>
    </div>
  ),
}));

const categories = [
  { id: 'coffee', name: 'Café en grano' },
  { id: 'bakery', name: 'Panadería' },
] as unknown as CategoryDto[];

/** Bread sold by the kilo, exempt of IVA, with its schedule, photo, CFDI codes and its sizes. */
const bread = {
  id: 'bread', name: 'Pan de masa madre', categoryId: 'bakery', description: 'Fermentación de 24 horas', priceAmount: 95.5, priceCurrency: 'MXN',
  sku: 'PAN-MM', barcode: '7501234567890', sortOrder: 4, isFeatured: true, tags: 'pan,artesanal', compareAtPrice: 110,
  isTaxExempt: true, taxRate: null, satProductCode: '50181900', satUnitCode: 'KGM', taxObject: CfdiTaxObject.NotSubject, calories: 260,
  allergens: 'Gluten', maxQuantityPerOrder: 3, unit: UnitType.Kg, preparationTimeMinutes: 5, availableFromTime: '07:00:00',
  availableToTime: '13:30:00', isAvailable: true, hasDiscount: true, tracksStock: false, isLowStock: false,
  images: { medium: '/uploads/bread-medium.webp' },
  optionGroups: [{ id: 'g1', name: 'Rebanado', minSelections: 0, maxSelections: 1, options: [{ id: 'o1', name: 'Sí', priceDelta: 0, isAvailable: true }] }],
} as unknown as ProductDto;

/** What the global search does: it goes to the form of another product from wherever it is. */
function GoTo({ to }: { to: string }) {
  const navigate = useNavigate();
  return <button type="button" onClick={() => navigate(to)}>Ir a {to}</button>;
}

function renderForm(path = '/products/new', goTo?: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      {goTo && <GoTo to={goTo} />}
      <Routes>
        <Route path="/products/new" element={<ProductForm />} />
        <Route path="/products/:id/edit" element={<ProductForm />} />
        <Route path="/products" element={<p>Lista de productos</p>} />
      </Routes>
    </MemoryRouter>,
  );
}

const field = (label: string | RegExp) => screen.getByLabelText(label);

describe('ProductForm', () => {
  const createObjectURL = URL.createObjectURL;

  beforeEach(() => {
    vi.resetAllMocks();
    URL.createObjectURL = vi.fn(() => 'blob:foto-nueva');
    vi.mocked(useAuth).mockReturnValue({ user: { id: 'u1', role: 'Employee', permissions: ['ManageCatalog'] } } as unknown as ReturnType<typeof useAuth>);
    vi.mocked(useTenant).mockReturnValue({
      tenant: { defaultTaxRate: 16 }, capabilities: TenantCapability.Retail | TenantCapability.Inventory,
    } as unknown as ReturnType<typeof useTenant>);
    vi.mocked(categoriesApi.getAll).mockResolvedValue({ data: categories } as never);
    vi.mocked(productsApi.getById).mockResolvedValue({ data: bread } as never);
    vi.mocked(productsApi.create).mockResolvedValue({ data: { id: 'new-id' } } as never);
    vi.mocked(productsApi.update).mockResolvedValue({ data: {} } as never);
    vi.mocked(productsApi.setOptionGroups).mockResolvedValue({ data: {} } as never);
  });

  afterEach(() => {
    URL.createObjectURL = createObjectURL;
  });

  it('creates a product with the numbers, times and codes the API takes, and goes back to the list', async () => {
    const user = userEvent.setup();
    renderForm();
    await screen.findByRole('option', { name: 'Panadería' });

    await user.type(field(/^Nombre/), 'Café de Chiapas 250 g');
    await user.selectOptions(field(/^Categoría/), 'coffee');
    await user.type(field('Descripción'), 'Altura, notas a cacao');
    await user.selectOptions(field('Unidad'), 'Paquete');
    await user.type(field('Etiquetas'), 'café,chiapas');
    await user.clear(field('Orden'));
    await user.type(field('Orden'), '3');
    await user.click(field('Destacado'));
    await user.clear(field(/^Precio \*/));
    await user.type(field(/^Precio \*/), '180');
    await user.type(field('Precio anterior'), '220');
    await user.selectOptions(field('IVA'), '0');
    await user.type(field('Clave de producto o servicio'), ' 50201700 ');
    await user.type(field('Clave de unidad'), 'xpk');
    await user.selectOptions(field('Objeto de impuesto'), String(CfdiTaxObject.NotSubject));
    await user.type(field('SKU'), 'CAF-CHI-250');
    await user.type(field('Código de barras'), '7501112223334');
    await user.type(field('Máx. por orden'), '6');
    await user.type(field('Disponible desde'), '08:00');
    await user.type(field('Disponible hasta'), '20:30');
    await user.type(field('Tiempo preparación (min)'), '2');
    await user.type(field('Calorías'), '5');
    await user.type(field('Alérgenos'), 'Ninguno');
    await user.click(screen.getByRole('button', { name: 'Crear producto' }));

    await waitFor(() => expect(productsApi.create).toHaveBeenCalledTimes(1));
    expect(productsApi.create).toHaveBeenCalledWith(expect.objectContaining({
      categoryId: 'coffee', name: 'Café de Chiapas 250 g', description: 'Altura, notas a cacao', unit: UnitType.Pack, tags: 'café,chiapas',
      sortOrder: 3, isFeatured: true, priceAmount: 180, priceCurrency: 'MXN', compareAtPrice: 220, taxRate: 0, isTaxExempt: false,
      satProductCode: '50201700', satUnitCode: 'XPK', taxObject: CfdiTaxObject.NotSubject, sku: 'CAF-CHI-250', barcode: '7501112223334',
      maxQuantityPerOrder: 6, availableFromTime: '08:00:00', availableToTime: '20:30:00', preparationTimeMinutes: 2, calories: 5,
      allergens: 'Ninguno',
    }), 'Product');
    // The rate goes as taxRate and isTaxExempt, never as the option of the form.
    expect(vi.mocked(productsApi.create).mock.calls[0][0]).not.toHaveProperty('vatOption');
    // The options were not touched: they are not rewritten.
    expect(productsApi.setOptionGroups).not.toHaveBeenCalled();
    expect(productsApi.uploadImage).not.toHaveBeenCalled();
    expect(toast.success).toHaveBeenCalledWith('Producto creado.');
    expect(await screen.findByText('Lista de productos')).toBeInTheDocument();
  });

  it('sends the optional numbers as nothing when they are left empty', async () => {
    const user = userEvent.setup();
    renderForm();
    await screen.findByRole('option', { name: 'Panadería' });

    await user.type(field(/^Nombre/), 'Bolsa de papel');
    await user.selectOptions(field(/^Categoría/), 'coffee');
    await user.clear(field(/^Precio \*/));
    await user.type(field(/^Precio \*/), '5');
    await user.click(screen.getByRole('button', { name: 'Crear producto' }));

    await waitFor(() => expect(productsApi.create).toHaveBeenCalledWith(expect.objectContaining({
      priceAmount: 5, unit: undefined, compareAtPrice: undefined, calories: undefined, maxQuantityPerOrder: undefined,
      preparationTimeMinutes: undefined, availableFromTime: undefined, availableToTime: undefined, taxRate: undefined, isTaxExempt: false,
      satProductCode: undefined, satUnitCode: undefined,
    }), 'Product'));
  });

  it('asks for the name, the category and a price above 0 before sending', async () => {
    const user = userEvent.setup();
    renderForm();
    await screen.findByRole('option', { name: 'Panadería' });

    await user.click(screen.getByRole('button', { name: 'Crear producto' }));

    expect(await screen.findByText('El nombre es requerido')).toBeInTheDocument();
    expect(screen.getByText('Selecciona una categoría')).toBeInTheDocument();
    expect(screen.getByText('Debe ser mayor a 0')).toBeInTheDocument();
    expect(productsApi.create).not.toHaveBeenCalled();
  });

  it('edits a product from what it has, sends again its rate and codes, and leaves its options as they were', async () => {
    const user = userEvent.setup();
    renderForm('/products/bread/edit');

    expect(await screen.findByDisplayValue('Pan de masa madre')).toBeInTheDocument();
    expect(productsApi.getById).toHaveBeenCalledWith('bread');
    expect(field(/^Categoría/)).toHaveValue('bakery');
    expect(field('IVA')).toHaveValue('exempt');
    expect(field('Unidad')).toHaveValue(String(UnitType.Kg));
    expect(field('Disponible desde')).toHaveValue('07:00');
    expect(field('Disponible hasta')).toHaveValue('13:30');
    expect(field('Destacado')).toBeChecked();
    expect(screen.getByRole('img', { name: 'Imagen del producto' })).toHaveAttribute('src', '/uploads/bread-medium.webp');
    expect(screen.getByText('1 grupo de opciones')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cambiar imagen' })).toBeInTheDocument();

    await user.clear(field(/^Precio \*/));
    await user.type(field(/^Precio \*/), '99.9');
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    // Everything it had goes back: an update without a field would empty it.
    await waitFor(() => expect(productsApi.update).toHaveBeenCalledWith('bread', {
      id: 'bread', name: 'Pan de masa madre', categoryId: 'bakery', description: 'Fermentación de 24 horas', priceAmount: 99.9,
      priceCurrency: 'MXN', compareAtPrice: 110, unit: UnitType.Kg, sku: 'PAN-MM', barcode: '7501234567890', tags: 'pan,artesanal',
      allergens: 'Gluten', taxRate: undefined, isTaxExempt: true, satProductCode: '50181900', satUnitCode: 'KGM',
      taxObject: CfdiTaxObject.NotSubject, sortOrder: 4, isFeatured: true, availableFromTime: '07:00:00', availableToTime: '13:30:00',
      preparationTimeMinutes: 5, calories: 260, maxQuantityPerOrder: 3,
    }, 'Product'));
    expect(productsApi.create).not.toHaveBeenCalled();
    expect(productsApi.setOptionGroups).not.toHaveBeenCalled();
    expect(toast.success).toHaveBeenCalledWith('Producto actualizado.');
    expect(await screen.findByText('Lista de productos')).toBeInTheDocument();
  });

  it('takes the unit away from a product when «Sin unidad» is chosen', async () => {
    const user = userEvent.setup();
    renderForm('/products/bread/edit');
    await screen.findByDisplayValue('Pan de masa madre');

    await user.selectOptions(field('Unidad'), 'Sin unidad');
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    await waitFor(() => expect(productsApi.update).toHaveBeenCalledWith('bread', expect.objectContaining({ unit: undefined }), 'Product'));
  });

  it('saves the options once touched, trimmed, on the product just created', async () => {
    const user = userEvent.setup();
    renderForm();
    await screen.findByRole('option', { name: 'Panadería' });

    await user.type(field(/^Nombre/), 'Latte');
    await user.selectOptions(field(/^Categoría/), 'coffee');
    await user.clear(field(/^Precio \*/));
    await user.type(field(/^Precio \*/), '55');
    await user.click(screen.getByRole('button', { name: 'Agregar tamaños' }));
    await user.click(screen.getByRole('button', { name: 'Crear producto' }));

    await waitFor(() => expect(productsApi.setOptionGroups).toHaveBeenCalledWith('new-id', [{
      id: null, name: 'Tamaño', minSelections: 1, maxSelections: 1, options: [{ id: null, name: 'Grande', priceDelta: 15, isAvailable: true }],
    }]));
    expect(await screen.findByText('Lista de productos')).toBeInTheDocument();
  });

  it('does not create the product twice when its options fail: it goes on with its photo and says so', async () => {
    const user = userEvent.setup();
    vi.mocked(productsApi.setOptionGroups).mockRejectedValue(new Error('Network Error'));
    vi.mocked(productsApi.uploadImage).mockResolvedValue({ data: { medium: '/uploads/latte-medium.webp' } } as never);
    renderForm();
    await screen.findByRole('option', { name: 'Panadería' });

    const photo = new File([new Uint8Array(1024)], 'latte.png', { type: 'image/png' });
    await user.upload(screen.getByLabelText('Archivo de la imagen'), photo);
    await user.type(field(/^Nombre/), 'Latte');
    await user.selectOptions(field(/^Categoría/), 'coffee');
    await user.clear(field(/^Precio \*/));
    await user.type(field(/^Precio \*/), '55');
    await user.click(screen.getByRole('button', { name: 'Agregar tamaños' }));
    await user.click(screen.getByRole('button', { name: 'Crear producto' }));

    expect(await screen.findByText('Lista de productos')).toBeInTheDocument();
    expect(productsApi.create).toHaveBeenCalledTimes(1);
    expect(toast.warning).toHaveBeenCalledWith(
      'El producto se creó, pero sus opciones no se guardaron: Error al guardar las opciones. Revísalas desde Editar.',
    );
    expect(productsApi.uploadImage).toHaveBeenCalledWith('new-id', photo);
    expect(toast.success).toHaveBeenCalledWith('Producto creado.');
  });

  it('stays on the form when the options of a product being edited fail, to try again', async () => {
    const user = userEvent.setup();
    vi.mocked(productsApi.setOptionGroups).mockRejectedValue({
      isAxiosError: true, response: { status: 400, data: { error: 'El grupo "Tamaño" está repetido.' } },
    });
    renderForm('/products/bread/edit');
    await screen.findByDisplayValue('Pan de masa madre');

    await user.click(screen.getByRole('button', { name: 'Agregar tamaños' }));
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    expect(await screen.findByText('El grupo "Tamaño" está repetido.')).toBeInTheDocument();
    expect(screen.queryByText('Lista de productos')).not.toBeInTheDocument();
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('says what is wrong in the options before saving anything', async () => {
    const user = userEvent.setup();
    renderForm('/products/bread/edit');
    await screen.findByDisplayValue('Pan de masa madre');

    await user.click(screen.getByRole('button', { name: 'Agregar un grupo sin nombre' }));
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    expect(await screen.findByText('Cada grupo de opciones necesita un nombre.')).toBeInTheDocument();
    expect(productsApi.update).not.toHaveBeenCalled();
    expect(productsApi.setOptionGroups).not.toHaveBeenCalled();
  });

  it('uploads the chosen photo once the product is saved, and goes on with a warning when it fails', async () => {
    const user = userEvent.setup();
    vi.mocked(productsApi.uploadImage).mockRejectedValue(new Error('Network Error'));
    renderForm();
    await screen.findByRole('option', { name: 'Panadería' });

    const photo = new File([new Uint8Array(2048)], 'latte.png', { type: 'image/png' });
    await user.upload(screen.getByLabelText('Archivo de la imagen'), photo);
    expect(screen.getByRole('img', { name: 'Imagen del producto' })).toHaveAttribute('src', 'blob:foto-nueva');
    expect(screen.getByText('Archivo: latte.png (2 KB)')).toBeInTheDocument();

    await user.type(field(/^Nombre/), 'Latte');
    await user.selectOptions(field(/^Categoría/), 'coffee');
    await user.clear(field(/^Precio \*/));
    await user.type(field(/^Precio \*/), '55');
    await user.click(screen.getByRole('button', { name: 'Crear producto' }));

    await waitFor(() => expect(productsApi.uploadImage).toHaveBeenCalledWith('new-id', photo));
    expect(toast.warning).toHaveBeenCalledWith('El producto se guardó, pero la imagen no se pudo subir. Inténtalo de nuevo desde Editar.');
    expect(toast.success).toHaveBeenCalledWith('Producto creado.');
    expect(await screen.findByText('Lista de productos')).toBeInTheDocument();
  });

  it('keeps what was written when a new product could not be created', async () => {
    const user = userEvent.setup();
    vi.mocked(productsApi.create).mockRejectedValue({
      isAxiosError: true, response: { status: 409, data: { error: 'Ya existe un producto con el SKU LAT-01.' } },
    });
    renderForm();
    await screen.findByRole('option', { name: 'Panadería' });

    await user.type(field(/^Nombre/), 'Latte');
    await user.selectOptions(field(/^Categoría/), 'coffee');
    await user.clear(field(/^Precio \*/));
    await user.type(field(/^Precio \*/), '55');
    await user.click(screen.getByRole('button', { name: 'Crear producto' }));

    expect(await screen.findByText('Ya existe un producto con el SKU LAT-01.')).toBeInTheDocument();
    expect(field(/^Nombre/)).toHaveValue('Latte');
    expect(screen.getByRole('button', { name: 'Crear producto' })).toBeEnabled();
  });

  it('shows why the server did not save, and stays on the form', async () => {
    const user = userEvent.setup();
    vi.mocked(productsApi.update).mockRejectedValue({
      isAxiosError: true, response: { status: 409, data: { error: 'Ya existe un producto con el SKU PAN-MM.' } },
    });
    renderForm('/products/bread/edit');
    await screen.findByDisplayValue('Pan de masa madre');

    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    expect(await screen.findByText('Ya existe un producto con el SKU PAN-MM.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Guardar cambios' })).toBeEnabled();
    expect(toast.success).not.toHaveBeenCalled();
    expect(screen.queryByText('Lista de productos')).not.toBeInTheDocument();
  });

  it('lets adjust the stock only to whoever manages the inventory', async () => {
    const { unmount } = renderForm('/products/bread/edit');
    expect(await screen.findByText('Existencias: solo ver')).toBeInTheDocument();
    unmount();

    vi.mocked(useAuth).mockReturnValue({
      user: { id: 'u1', role: 'Employee', permissions: ['ManageCatalog', 'ManageInventory'] },
    } as unknown as ReturnType<typeof useAuth>);
    renderForm('/products/bread/edit');
    expect(await screen.findByText('Existencias: puede ajustar')).toBeInTheDocument();
  });

  it('starts over when it goes from one product to another, so the one before is never saved over the next one', async () => {
    const user = userEvent.setup();
    const cake = { ...bread, id: 'cake', name: 'Pastel de zanahoria', images: undefined, optionGroups: [] };
    vi.mocked(productsApi.getById).mockImplementation(async (id) => {
      if (id === 'other') throw new Error('Network Error');
      return { data: id === 'cake' ? cake : bread } as never;
    });
    const { unmount } = renderForm('/products/bread/edit', '/products/other/edit');
    await screen.findByDisplayValue('Pan de masa madre');

    // The next one could not be read: no form, and nothing of the bread to save under its id.
    await user.click(screen.getByRole('button', { name: 'Ir a /products/other/edit' }));
    expect(await screen.findByText('Error al cargar los datos.')).toBeInTheDocument();
    expect(productsApi.getById).toHaveBeenLastCalledWith('other');
    expect(screen.queryByRole('button', { name: 'Guardar cambios' })).not.toBeInTheDocument();
    expect(productsApi.update).not.toHaveBeenCalled();
    unmount();

    // Read: it shows only what the next one has, not the photo or the options of the one before.
    renderForm('/products/bread/edit', '/products/cake/edit');
    await screen.findByDisplayValue('Pan de masa madre');
    await user.click(screen.getByRole('button', { name: 'Ir a /products/cake/edit' }));
    expect(await screen.findByDisplayValue('Pastel de zanahoria')).toBeInTheDocument();
    expect(screen.queryByRole('img', { name: 'Imagen del producto' })).not.toBeInTheDocument();
    expect(screen.getByText('0 grupos de opciones')).toBeInTheDocument();
  });

  it('says when the product could not be read, and offers no form that would save empty fields over it', async () => {
    vi.mocked(productsApi.getById).mockRejectedValue(new Error('Network Error'));
    renderForm('/products/bread/edit');

    expect(await screen.findByText('Error al cargar los datos.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Guardar cambios' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/^Nombre/)).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Volver a Productos' })).toHaveAttribute('href', '/products');
  });
});
