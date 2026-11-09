import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { categoriesApi } from '../../api/categoriesApi';
import type { CategoryDto, CategoryTemplateDto } from '../../types';
import CategoriesList from './CategoriesList';

const toast = vi.hoisted(() => ({ success: vi.fn(), warning: vi.fn(), error: vi.fn() }));
const dialogs = vi.hoisted(() => ({ confirm: vi.fn() }));

vi.mock('../../components/common/PageTitle', () => ({ default: ({ title }: { title: string }) => <h1>{title}</h1> }));
vi.mock('../../api/categoriesApi', () => ({
  categoriesApi: {
    getAll: vi.fn(), create: vi.fn(), update: vi.fn(), toggleActive: vi.fn(), getTemplates: vi.fn(), installTemplate: vi.fn(),
  },
}));
vi.mock('../../hooks/useToast', () => ({ useToast: () => toast }));
vi.mock('../../hooks/useConfirm', () => ({ useConfirm: () => dialogs }));

/** Tacos with many products, drinks with one, desserts with none, and a seasonal one archived. */
const tacos: CategoryDto = { id: 'tacos', name: 'Tacos', sortOrder: 1, isActive: true, productCount: 12 };
const drinks: CategoryDto = { id: 'bebidas', name: 'Bebidas', description: 'Frías y calientes', sortOrder: 2, isActive: true, productCount: 1 };
const desserts: CategoryDto = { id: 'postres', name: 'Postres', description: '', sortOrder: 3, isActive: true, productCount: 0 };
const seasonal: CategoryDto = { id: 'temporada', name: 'Temporada', sortOrder: 4, isActive: false, productCount: 0 };

const restaurant: CategoryTemplateDto = {
  id: 'restaurante', name: 'Restaurante', description: 'Para cocinas con menú del día', isRecommended: true, categoryCount: 8,
  categories: ['Entradas', 'Sopas', 'Platos fuertes', 'Tacos', 'Bebidas', 'Postres', 'Extras', 'Para llevar']
    .map((name, index) => ({ name, sortOrder: index })),
};
const shop: CategoryTemplateDto = {
  id: 'abarrotes', name: 'Abarrotes', description: 'Tienda de la esquina', isRecommended: false, categoryCount: 2,
  categories: [{ name: 'Lácteos', sortOrder: 0 }, { name: 'Botanas', sortOrder: 1 }],
};

const ok = <T,>(data: T) => Promise.resolve({ data } as never);
// What the API answers when a rule refuses something: ProblemDetails with its code, the detail and the same text in `error`.
const problem = (detail: string) => ({ isAxiosError: true, response: { status: 400, data: { code: 'business_rule', detail, error: detail } } });
const refused = (detail: string) => Promise.reject(problem(detail));
const limitMessage = 'Un negocio puede tener como máximo 300 categorías (también cuentan las archivadas).';

function serve(active: CategoryDto[] = [tacos, drinks, desserts]) {
  vi.mocked(categoriesApi.getAll).mockImplementation((includeArchived = false) => ok(includeArchived ? [...active, seasonal] : active));
}

function renderCategories() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}><CategoriesList /></QueryClientProvider>);
}

const row = async (name: string) => (await screen.findByText(name, { selector: '.fw-semibold' })).closest('tr') as HTMLElement;
const names = () => screen.queryAllByRole('row').slice(1).map((tr) => within(tr).queryAllByRole('cell')[0]?.textContent);

describe('CategoriesList', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    serve();
  });

  it('lists the active categories with their products, order and status, and the archived ones when asked', async () => {
    const user = userEvent.setup();
    renderCategories();

    const cells = (tr: HTMLElement) => within(tr).getAllByRole('cell').map((cell) => cell.textContent);
    expect(cells(await row('Bebidas'))).toEqual(['Bebidas', 'Frías y calientes', '1', '2', 'Activa', 'EditarArchivar']);
    expect(cells(await row('Postres'))).toEqual(['Postres', '-', '0', '3', 'Activa', 'EditarArchivar']);
    expect(names()).toEqual(['Tacos', 'Bebidas', 'Postres']);
    expect(categoriesApi.getAll).toHaveBeenCalledWith(false);

    await user.click(screen.getByRole('switch', { name: 'Mostrar archivadas' }));
    expect(cells(await row('Temporada'))).toEqual(['Temporada', '-', '0', '4', 'Archivada', 'EditarRestaurar']);
    expect(categoriesApi.getAll).toHaveBeenLastCalledWith(true);
  });

  it('finds a category by its name or its description, whatever the case', async () => {
    const user = userEvent.setup();
    renderCategories();
    await row('Tacos');

    await user.type(screen.getByLabelText('Nombre o descripción'), 'FRÍAS');
    await waitFor(() => expect(names()).toEqual(['Bebidas']));
    await user.clear(screen.getByLabelText('Nombre o descripción'));
    await user.type(screen.getByLabelText('Nombre o descripción'), 'pos');
    await waitFor(() => expect(names()).toEqual(['Postres']));
  });

  it('creates a category, says so, closes the form and reads the list again', async () => {
    const user = userEvent.setup();
    vi.mocked(categoriesApi.create).mockReturnValue(ok({}));
    renderCategories();
    await row('Tacos');

    await user.click(screen.getByRole('button', { name: 'Nueva categoría' }));
    const form = await screen.findByRole('dialog', { name: 'Nueva categoría' });
    expect(within(form).getByLabelText('Orden *')).toHaveValue(0);
    await user.type(within(form).getByLabelText('Nombre *'), 'Tortas');
    await user.type(within(form).getByLabelText('Descripción'), 'De pierna y de milanesa');
    await user.clear(within(form).getByLabelText('Orden *'));
    await user.type(within(form).getByLabelText('Orden *'), '5');
    await user.click(within(form).getByRole('button', { name: 'Crear categoría' }));

    expect(categoriesApi.create).toHaveBeenCalledWith({ name: 'Tortas', description: 'De pierna y de milanesa', sortOrder: 5 });
    expect(toast.success).toHaveBeenCalledWith('Categoría creada.');
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nueva categoría' })).not.toBeInTheDocument());
    expect(categoriesApi.getAll).toHaveBeenCalledTimes(2);
  });

  it('starts a new category empty after one that was left without saving', async () => {
    const user = userEvent.setup();
    renderCategories();
    await row('Tacos');

    await user.click(screen.getByRole('button', { name: 'Nueva categoría' }));
    const form = await screen.findByRole('dialog', { name: 'Nueva categoría' });
    await user.type(within(form).getByLabelText('Nombre *'), 'Tortas');
    await user.type(within(form).getByLabelText('Descripción'), 'De pierna');
    await user.click(within(form).getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nueva categoría' })).not.toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'Nueva categoría' }));
    const again = await screen.findByRole('dialog', { name: 'Nueva categoría' });
    expect(within(again).getByLabelText('Nombre *')).toHaveValue('');
    expect(within(again).getByLabelText('Descripción')).toHaveValue('');
    expect(categoriesApi.create).not.toHaveBeenCalled();
  });

  it('edits a category from what it has', async () => {
    const user = userEvent.setup();
    vi.mocked(categoriesApi.update).mockReturnValue(ok({}));
    renderCategories();

    await user.click(within(await row('Bebidas')).getByRole('button', { name: 'Editar Bebidas' }));
    const form = await screen.findByRole('dialog', { name: 'Editar categoría' });
    expect(within(form).getByLabelText('Nombre *')).toHaveValue('Bebidas');
    expect(within(form).getByLabelText('Descripción')).toHaveValue('Frías y calientes');
    expect(within(form).getByLabelText('Orden *')).toHaveValue(2);
    await user.type(within(form).getByLabelText('Nombre *'), ' frías');
    await user.click(within(form).getByRole('button', { name: 'Guardar cambios' }));

    expect(categoriesApi.update).toHaveBeenCalledWith('bebidas', {
      id: 'bebidas', name: 'Bebidas frías', description: 'Frías y calientes', sortOrder: 2,
    });
    expect(toast.success).toHaveBeenCalledWith('Categoría actualizada.');
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Editar categoría' })).not.toBeInTheDocument());

    // A new one starts empty, not with what was edited before.
    await user.click(screen.getByRole('button', { name: 'Nueva categoría' }));
    const empty = await screen.findByRole('dialog', { name: 'Nueva categoría' });
    expect(within(empty).getByLabelText('Nombre *')).toHaveValue('');
    expect(within(empty).getByLabelText('Descripción')).toHaveValue('');
  });

  it('edits a category without a description, sending it empty', async () => {
    const user = userEvent.setup();
    vi.mocked(categoriesApi.update).mockReturnValue(ok({}));
    renderCategories();

    await user.click(within(await row('Tacos')).getByRole('button', { name: 'Editar Tacos' }));
    const form = await screen.findByRole('dialog', { name: 'Editar categoría' });
    expect(within(form).getByLabelText('Descripción')).toHaveValue('');
    await user.click(within(form).getByRole('button', { name: 'Guardar cambios' }));

    expect(categoriesApi.update).toHaveBeenCalledWith('tacos', { id: 'tacos', name: 'Tacos', description: '', sortOrder: 1 });
  });

  it('says what is missing or too long before sending anything', async () => {
    const user = userEvent.setup();
    renderCategories();
    await row('Tacos');

    await user.click(screen.getByRole('button', { name: 'Nueva categoría' }));
    const form = await screen.findByRole('dialog', { name: 'Nueva categoría' });
    await user.clear(within(form).getByLabelText('Orden *'));
    await user.click(within(form).getByRole('button', { name: 'Crear categoría' }));
    expect(await within(form).findByText('El nombre es requerido')).toBeInTheDocument();
    expect(within(form).getByLabelText('Nombre *')).toHaveAccessibleDescription('El nombre es requerido');
    expect(within(form).getByLabelText('Nombre *')).toBeInvalid();
    expect(within(form).getByLabelText('Orden *')).toHaveAccessibleDescription('Escribe el orden: 0 o mayor');

    await user.type(within(form).getByLabelText('Nombre *'), 'Tortas');
    await user.type(within(form).getByLabelText('Orden *'), '-1');
    await user.click(within(form).getByLabelText('Descripción'));
    await user.paste('a'.repeat(501));
    await user.click(within(form).getByRole('button', { name: 'Crear categoría' }));
    expect(await within(form).findByText('Debe ser 0 o mayor')).toBeInTheDocument();
    expect(within(form).getByText('Máximo 500 caracteres')).toBeInTheDocument();
    expect(within(form).getByLabelText('Orden *')).toHaveClass('is-invalid');
    expect(within(form).getByLabelText('Descripción')).toHaveClass('is-invalid');
    expect(within(form).queryByText('El nombre es requerido')).not.toBeInTheDocument();
    expect(within(form).getByLabelText('Orden *')).toHaveAccessibleDescription('Debe ser 0 o mayor');
    expect(within(form).getByLabelText('Descripción')).toHaveAccessibleDescription('Máximo 500 caracteres');
    expect(within(form).getByLabelText('Orden *')).toBeInvalid();
    expect(within(form).getByLabelText('Descripción')).toBeInvalid();
    expect(categoriesApi.create).not.toHaveBeenCalled();

    // 500 characters and the order 0 are fine.
    vi.mocked(categoriesApi.create).mockReturnValue(ok({}));
    await user.type(within(form).getByLabelText('Descripción'), '{Backspace}');
    await user.clear(within(form).getByLabelText('Orden *'));
    await user.type(within(form).getByLabelText('Orden *'), '0');
    await user.click(within(form).getByRole('button', { name: 'Crear categoría' }));
    await waitFor(() => expect(categoriesApi.create).toHaveBeenCalledWith({ name: 'Tortas', description: 'a'.repeat(500), sortOrder: 0 }));
  });

  it('says inside the form why it could not be saved, keeps what was written, and forgets it once closed', async () => {
    const user = userEvent.setup();
    vi.mocked(categoriesApi.create).mockImplementation(() => refused(limitMessage));
    renderCategories();
    await row('Tacos');

    await user.click(screen.getByRole('button', { name: 'Nueva categoría' }));
    const form = await screen.findByRole('dialog', { name: 'Nueva categoría' });
    await user.type(within(form).getByLabelText('Nombre *'), 'Tacos');
    await user.click(within(form).getByRole('button', { name: 'Crear categoría' }));

    expect(await within(form).findByRole('alert')).toHaveTextContent(limitMessage);
    expect(within(form).getByLabelText('Nombre *')).toHaveValue('Tacos');
    expect(form).toHaveClass('show');
    expect(toast.success).not.toHaveBeenCalled();

    await user.click(within(form).getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nueva categoría' })).not.toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: 'Nueva categoría' }));
    expect(within(await screen.findByRole('dialog', { name: 'Nueva categoría' })).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('creates a category once, even with a double click or Enter twice when the server answers at once', async () => {
    const user = userEvent.setup();
    vi.mocked(categoriesApi.create).mockReturnValue(ok({}));
    renderCategories();
    await row('Tacos');

    await user.click(screen.getByRole('button', { name: 'Nueva categoría' }));
    let form = await screen.findByRole('dialog', { name: 'Nueva categoría' });
    await user.type(within(form).getByLabelText('Nombre *'), 'Tortas');
    await user.dblClick(within(form).getByRole('button', { name: 'Crear categoría' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nueva categoría' })).not.toBeInTheDocument());
    expect(categoriesApi.create).toHaveBeenCalledTimes(1);

    await user.click(screen.getByRole('button', { name: 'Nueva categoría' }));
    form = await screen.findByRole('dialog', { name: 'Nueva categoría' });
    await user.type(within(form).getByLabelText('Nombre *'), 'Tamales{Enter}');
    await user.keyboard('{Enter}');
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nueva categoría' })).not.toBeInTheDocument());
    expect(categoriesApi.create).toHaveBeenCalledTimes(2);
    expect(categoriesApi.create).toHaveBeenLastCalledWith({ name: 'Tamales', description: '', sortOrder: 0 });
  });

  it('keeps the form open while it is saved, and says there why it could not be', async () => {
    const user = userEvent.setup();
    let refuse: (reason: unknown) => void = () => undefined;
    vi.mocked(categoriesApi.create).mockImplementation(() => new Promise((_, reject) => { refuse = reject; }));
    renderCategories();
    await row('Tacos');

    await user.click(screen.getByRole('button', { name: 'Nueva categoría' }));
    const form = await screen.findByRole('dialog', { name: 'Nueva categoría' });
    await user.type(within(form).getByLabelText('Nombre *'), 'Tortas');
    await user.click(within(form).getByRole('button', { name: 'Crear categoría' }));

    expect(within(form).getByRole('button', { name: 'Guardando...' })).toBeDisabled();
    expect(within(form).getByRole('button', { name: 'Cancelar' })).toBeDisabled();
    expect(within(form).queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(form).toHaveClass('show');

    refuse(problem(limitMessage));
    expect(await within(form).findByRole('alert')).toHaveTextContent(limitMessage);
    expect(within(form).getByRole('button', { name: 'Crear categoría' })).toBeEnabled();
    expect(within(form).getByRole('button', { name: 'Cancelar' })).toBeEnabled();
    expect(within(form).getByRole('button', { name: 'Close' })).toBeInTheDocument();

    // Trying again, the earlier reason goes away while the new answer is on its way.
    await user.click(within(form).getByRole('button', { name: 'Crear categoría' }));
    expect(within(form).getByRole('button', { name: 'Guardando...' })).toBeDisabled();
    expect(within(form).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('holds the next form while it saves, even when the list read after the one before arrives meanwhile', async () => {
    const user = userEvent.setup();
    let listed: (value: unknown) => void = () => undefined;
    let created: (value: unknown) => void = () => undefined;
    renderCategories();
    await row('Tacos');
    // The list read after the first one is saved, and the second save, both arrive late.
    vi.mocked(categoriesApi.getAll).mockImplementation(() => new Promise((resolve) => { listed = resolve; }) as never);
    vi.mocked(categoriesApi.create)
      .mockReturnValueOnce(ok({}))
      .mockImplementationOnce(() => new Promise((resolve) => { created = resolve; }) as never);

    await user.click(screen.getByRole('button', { name: 'Nueva categoría' }));
    let form = await screen.findByRole('dialog', { name: 'Nueva categoría' });
    await user.type(within(form).getByLabelText('Nombre *'), 'Tortas');
    await user.click(within(form).getByRole('button', { name: 'Crear categoría' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nueva categoría' })).not.toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'Nueva categoría' }));
    form = await screen.findByRole('dialog', { name: 'Nueva categoría' });
    await user.type(within(form).getByLabelText('Nombre *'), 'Tamales');
    await user.click(within(form).getByRole('button', { name: 'Crear categoría' }));
    listed({ data: [tacos, drinks, desserts, { id: 'tortas', name: 'Tortas', sortOrder: 0, isActive: true, productCount: 0 }] });

    // Its «Guardando...» stays until its own answer arrives: a second click cannot create it twice.
    await row('Tortas');
    expect(within(form).getByRole('button', { name: 'Guardando...' })).toBeDisabled();
    expect(categoriesApi.create).toHaveBeenCalledTimes(2);
    created({ data: {} });
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nueva categoría' })).not.toBeInTheDocument());
    expect(categoriesApi.create).toHaveBeenLastCalledWith({ name: 'Tamales', description: '', sortOrder: 0 });
  });

  it('archives a category without products once confirmed', async () => {
    const user = userEvent.setup();
    dialogs.confirm.mockResolvedValueOnce(false).mockResolvedValueOnce(true);
    vi.mocked(categoriesApi.toggleActive).mockReturnValue(ok({}));
    renderCategories();

    await user.click(within(await row('Postres')).getByRole('button', { name: 'Archivar Postres' }));
    expect(dialogs.confirm).toHaveBeenCalledWith(expect.objectContaining({
      title: 'Archivar categoría',
      message: '“Postres” dejará de aparecer en Vender, el menú en línea y los formularios. Podrás restaurarla después.',
    }));
    expect(categoriesApi.toggleActive).not.toHaveBeenCalled();

    await user.click(within(await row('Postres')).getByRole('button', { name: 'Archivar Postres' }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('“Postres” archivada. Puedes restaurarla desde “Mostrar archivadas”.'));
    expect(categoriesApi.toggleActive).toHaveBeenCalledWith('postres', false, undefined);
    expect(categoriesApi.getAll).toHaveBeenCalledTimes(2);
  });

  it('asks where the products of a category go before archiving it, among the other active ones', async () => {
    const user = userEvent.setup();
    let archived: (value: unknown) => void = () => undefined;
    vi.mocked(categoriesApi.toggleActive)
      .mockImplementationOnce(() => refused('La categoría destino está archivada.'))
      .mockImplementationOnce(() => new Promise((resolve) => { archived = resolve; }) as never);
    renderCategories();
    await user.click(screen.getByRole('switch', { name: 'Mostrar archivadas' }));

    await user.click(within(await row('Tacos')).getByRole('button', { name: 'Archivar Tacos' }));
    expect(dialogs.confirm).not.toHaveBeenCalled();
    const dialog = await screen.findByRole('dialog', { name: 'Archivar “Tacos”' });
    expect(dialog).toHaveTextContent('Tiene 12 productos.');
    const target = within(dialog).getByLabelText('Pasar los productos a');
    // Neither the one being archived nor the archived ones.
    expect(within(target).getAllByRole('option').map((option) => option.textContent)).toEqual(['Elige una categoría…', 'Bebidas', 'Postres']);
    const archive = within(dialog).getByRole('button', { name: 'Pasar productos y archivar' });
    expect(archive).toBeDisabled();

    await user.selectOptions(target, 'Bebidas');
    await user.click(archive);
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('La categoría destino está archivada.'));
    expect(dialog).toHaveClass('show');
    expect(archive).toBeEnabled();

    await user.click(archive);
    // While it is archived the dialog stays: neither its buttons nor Escape close it.
    expect(within(dialog).getByRole('button', { name: 'Archivando…' })).toBeDisabled();
    expect(within(dialog).getByRole('button', { name: 'Cancelar' })).toBeDisabled();
    expect(within(dialog).queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(dialog).toHaveClass('show');
    archived({ data: {} });
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('“Tacos” archivada; sus productos ahora están en “Bebidas”.'));
    expect(categoriesApi.toggleActive).toHaveBeenLastCalledWith('tacos', false, 'bebidas');
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Archivar “Tacos”' })).not.toBeInTheDocument());

    // One product is one; with no other active category there is nowhere to move it.
    serve([drinks]);
    await user.click(screen.getByRole('switch', { name: 'Mostrar archivadas' }));
    await user.click(within(await row('Bebidas')).getByRole('button', { name: 'Archivar Bebidas' }));
    const alone = await screen.findByRole('dialog', { name: 'Archivar “Bebidas”' });
    expect(alone).toHaveTextContent('Tiene 1 producto.');
    expect(alone).toHaveTextContent('No hay otra categoría activa. Crea una primero para pasar ahí los productos.');
    expect(within(alone).getByRole('button', { name: 'Pasar productos y archivar' })).toBeDisabled();
    await user.click(within(alone).getByRole('button', { name: 'Cancelar' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Archivar “Bebidas”' })).not.toBeInTheDocument());
    expect(categoriesApi.toggleActive).toHaveBeenCalledTimes(2);
  });

  it('restores an archived category, or says why it could not', async () => {
    const user = userEvent.setup();
    vi.mocked(categoriesApi.toggleActive).mockImplementationOnce(() => refused('Ya hay una categoría activa con ese nombre.')).mockReturnValueOnce(ok({}));
    renderCategories();
    await user.click(await screen.findByRole('switch', { name: 'Mostrar archivadas' }));

    await user.click(within(await row('Temporada')).getByRole('button', { name: 'Restaurar Temporada' }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('Ya hay una categoría activa con ese nombre.'));
    expect(toast.success).not.toHaveBeenCalled();

    const reads = vi.mocked(categoriesApi.getAll).mock.calls.length;
    await user.click(within(await row('Temporada')).getByRole('button', { name: 'Restaurar Temporada' }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('“Temporada” está activa otra vez.'));
    expect(categoriesApi.toggleActive).toHaveBeenLastCalledWith('temporada', true);
    await waitFor(() => expect(categoriesApi.getAll).toHaveBeenCalledTimes(reads + 1));
  });

  it('reads the templates once their dialog opens and installs one, holding the dialog meanwhile', async () => {
    const user = userEvent.setup();
    vi.mocked(categoriesApi.getTemplates).mockReturnValue(ok([restaurant, shop]));
    let installed: (value: unknown) => void = () => undefined;
    vi.mocked(categoriesApi.installTemplate).mockReturnValue(new Promise((resolve) => { installed = resolve; }) as never);
    renderCategories();
    await row('Tacos');
    expect(categoriesApi.getTemplates).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Instalar plantilla' }));
    const dialog = await screen.findByRole('dialog', { name: 'Instalar plantilla de categorías' });
    const card = (await within(dialog).findByRole('heading', { name: 'Restaurante' })).closest('.card') as HTMLElement;
    expect(within(dialog).getByRole('button', { name: 'Close' })).toBeInTheDocument();
    expect(card).toHaveTextContent('RecomendadaPara cocinas con menú del día8 categorías');
    // Six of its categories and how many more.
    expect([...card.querySelectorAll('.badge.border')].map((badge) => badge.textContent))
      .toEqual(['Entradas', 'Sopas', 'Platos fuertes', 'Tacos', 'Bebidas', 'Postres', '+2']);
    const other = within(dialog).getByRole('heading', { name: 'Abarrotes' }).closest('.card') as HTMLElement;
    expect(other).not.toHaveTextContent('Recomendada');
    expect([...other.querySelectorAll('.badge.border')].map((badge) => badge.textContent)).toEqual(['Lácteos', 'Botanas']);

    await user.click(within(card).getByRole('button', { name: 'Instalar Restaurante' }));
    expect(categoriesApi.installTemplate).toHaveBeenCalledWith('restaurante');
    expect(within(card).getByRole('button', { name: 'Instalando Restaurante...' })).toBeDisabled();
    expect(within(other).getByRole('button', { name: 'Instalar Abarrotes' })).toBeDisabled();
    expect(within(dialog).getByRole('button', { name: 'Cerrar' })).toBeDisabled();
    expect(within(dialog).queryByRole('button', { name: 'Close' })).not.toBeInTheDocument();
    await user.keyboard('{Escape}');
    expect(dialog).toHaveClass('show');

    installed({ data: { templateId: 'restaurante', templateName: 'Restaurante', createdCount: 5, skippedCount: 3, totalCount: 8 } });
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Plantilla "Restaurante" instalada: 5 creadas, 3 omitidas.'));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Instalar plantilla de categorías' })).not.toBeInTheDocument());
    await waitFor(() => expect(categoriesApi.getAll).toHaveBeenCalledTimes(2));
    expect(categoriesApi.getTemplates).toHaveBeenCalledTimes(1);
  });

  it('installs a template once, even with a double click when the server answers at once', async () => {
    const user = userEvent.setup();
    vi.mocked(categoriesApi.getTemplates).mockReturnValue(ok([shop]));
    vi.mocked(categoriesApi.installTemplate).mockReturnValue(
      ok({ templateId: 'abarrotes', templateName: 'Abarrotes', createdCount: 2, skippedCount: 0, totalCount: 2 }),
    );
    renderCategories();
    await row('Tacos');

    await user.click(screen.getByRole('button', { name: 'Instalar plantilla' }));
    await user.dblClick(await screen.findByRole('button', { name: 'Instalar Abarrotes' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Instalar plantilla de categorías' })).not.toBeInTheDocument());
    expect(categoriesApi.installTemplate).toHaveBeenCalledTimes(1);
    expect(toast.success).toHaveBeenCalledTimes(1);
  });

  it('opens the templates again ready to install while the list is still being read after installing one', async () => {
    const user = userEvent.setup();
    vi.mocked(categoriesApi.getTemplates).mockReturnValue(ok([restaurant, shop]));
    vi.mocked(categoriesApi.installTemplate).mockReturnValue(
      ok({ templateId: 'abarrotes', templateName: 'Abarrotes', createdCount: 2, skippedCount: 0, totalCount: 2 }),
    );
    renderCategories();
    await row('Tacos');
    vi.mocked(categoriesApi.getAll).mockImplementation(() => new Promise(() => undefined) as never);

    await user.click(screen.getByRole('button', { name: 'Instalar plantilla' }));
    await user.click(await screen.findByRole('button', { name: 'Instalar Abarrotes' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Instalar plantilla de categorías' })).not.toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'Instalar plantilla' }));
    const again = await screen.findByRole('dialog', { name: 'Instalar plantilla de categorías' });
    expect(await within(again).findByRole('button', { name: 'Instalar Abarrotes' })).toBeEnabled();
    expect(within(again).getByRole('button', { name: 'Cerrar' })).toBeEnabled();
  });

  it('keeps showing the templates it has when reading them again fails', async () => {
    const user = userEvent.setup();
    let fail: (reason: unknown) => void = () => undefined;
    vi.mocked(categoriesApi.getTemplates)
      .mockReturnValueOnce(ok([shop]))
      .mockImplementationOnce(() => new Promise((_, reject) => { fail = reject; }) as never);
    renderCategories();
    await row('Tacos');

    await user.click(screen.getByRole('button', { name: 'Instalar plantilla' }));
    let dialog = await screen.findByRole('dialog', { name: 'Instalar plantilla de categorías' });
    await within(dialog).findByRole('heading', { name: 'Abarrotes' });
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Instalar plantilla de categorías' })).not.toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'Instalar plantilla' }));
    dialog = await screen.findByRole('dialog', { name: 'Instalar plantilla de categorías' });
    await waitFor(() => expect(categoriesApi.getTemplates).toHaveBeenCalledTimes(2));
    // The query tells the screen on a timer, not right away: give it that turn before looking.
    await act(async () => {
      fail(new Error('Network Error'));
      await new Promise((resolve) => setTimeout(resolve, 20));
    });
    expect(within(dialog).getByRole('button', { name: 'Instalar Abarrotes' })).toBeEnabled();
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('says inside the templates dialog when they could not be read or installed', async () => {
    const user = userEvent.setup();
    let listedTemplates: (value: unknown) => void = () => undefined;
    vi.mocked(categoriesApi.getTemplates)
      .mockImplementationOnce(() => Promise.reject(new Error('Network Error')))
      .mockImplementationOnce(() => new Promise((resolve) => { listedTemplates = resolve; }) as never)
      .mockReturnValue(ok([shop]));
    vi.mocked(categoriesApi.installTemplate).mockImplementation(() => refused('Tu plan no incluye plantillas.'));
    renderCategories();
    await row('Tacos');

    await user.click(screen.getByRole('button', { name: 'Instalar plantilla' }));
    const dialog = await screen.findByRole('dialog', { name: 'Instalar plantilla de categorías' });
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Error al cargar las plantillas de categorías.');
    expect(within(dialog).queryByText(/No hay plantillas/)).not.toBeInTheDocument();

    await user.click(within(dialog).getByRole('button', { name: 'Reintentar' }));
    // Reading them again shows it is on its way, not the error.
    expect(await within(dialog).findByRole('status')).toHaveTextContent('Cargando plantillas…');
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument();
    listedTemplates({ data: [shop] });
    await user.click(await within(dialog).findByRole('button', { name: 'Instalar Abarrotes' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Tu plan no incluye plantillas.');
    expect(dialog).toHaveClass('show');
    expect(within(dialog).getByRole('button', { name: 'Instalar Abarrotes' })).toBeEnabled();
    expect(toast.success).not.toHaveBeenCalled();

    // Closed and opened again, it starts without the error.
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar' }));
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Instalar plantilla de categorías' })).not.toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: 'Instalar plantilla' }));
    const again = await screen.findByRole('dialog', { name: 'Instalar plantilla de categorías' });
    expect(within(again).getByRole('heading', { name: 'Abarrotes' })).toBeInTheDocument();
    expect(within(again).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('says when there are no templates for the business', async () => {
    const user = userEvent.setup();
    vi.mocked(categoriesApi.getTemplates).mockReturnValue(ok([]));
    renderCategories();
    await row('Tacos');

    await user.click(screen.getByRole('button', { name: 'Instalar plantilla' }));
    const dialog = await screen.findByRole('dialog', { name: 'Instalar plantilla de categorías' });
    expect(await within(dialog).findByText('No hay plantillas disponibles para este negocio.')).toBeInTheDocument();
  });

  it('says when the categories could not be read', async () => {
    vi.mocked(categoriesApi.getAll).mockImplementation(() => Promise.reject(new Error('Network Error')));
    renderCategories();

    expect(await screen.findByText('Error al cargar las categorías.')).toBeInTheDocument();
    expect(screen.getByText('No hay categorías activas.')).toBeInTheDocument();
  });
});
