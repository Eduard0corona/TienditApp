import type { Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { SIZES, dialog, open, snap, snapElement } from '../helpers';
import { productPhotos } from './catalogo-inventario.mocks';

// Catálogo (productos, categorías, servicios) and Inventario (insumos, compras, proveedores): every screen at the
// three base looks, then the tabs, filters and dialogs each one has. A dialog is taken at the size of the screen
// (not the whole page): it is fixed over the page, and the shot shows it where the person sees it.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

for (const look of LOOKS) {
  test.describe(`Catálogo e inventario ${look.suffix}`, () => {
    test.use({ colorMode: look.colorMode, prepareData: productPhotos });
    test.beforeEach(async ({ page }) => {
      await page.setViewportSize(look.viewport);
    });

    test(`productos ${look.suffix}`, async ({ page }) => {
      await open(page, '/products');
      await expect(page.getByRole('link', { name: 'Editar Latte' })).toBeVisible();
      await snap(page, `productos-${look.suffix}`);
    });

    test(`producto nuevo ${look.suffix}`, async ({ page }) => {
      await open(page, '/products/new');
      await expect(page.getByRole('button', { name: 'Crear producto' })).toBeVisible();
      await snap(page, `producto-nuevo-${look.suffix}`);
    });

    test(`producto editar ${look.suffix}`, async ({ page }) => {
      // Latte: the demo product with option groups (size, milk, extras) and a photo.
      await open(page, '/products/c0cf3283-f80a-468d-b62f-9d7e6be47ec1/edit');
      // The stock card loads after the product: its form is the last part to draw.
      await expect(page.getByRole('button', { name: 'Controlar existencias' })).toBeVisible();
      await expect(page.getByLabel('Nombre').first()).toHaveValue('Latte');
      await snap(page, `producto-editar-${look.suffix}`);
    });

    test(`categorías ${look.suffix}`, async ({ page }) => {
      await open(page, '/categories');
      await expect(page.getByRole('button', { name: 'Editar Bebidas calientes' })).toBeVisible();
      await snap(page, `categorias-${look.suffix}`);
    });

    test(`servicios ${look.suffix}`, async ({ page }) => {
      await open(page, '/services');
      await expect(page.getByRole('button', { name: 'Editar Mantenimiento de cafetera espresso' })).toBeVisible();
      await snap(page, `servicios-${look.suffix}`);
    });

    test(`inventario ${look.suffix}`, async ({ page }) => {
      await open(page, '/inventory');
      await expect(page.getByRole('button', { name: 'Editar Café en grano Chiapas' })).toBeVisible();
      await snap(page, `inventario-${look.suffix}`);
    });

    test(`compras ${look.suffix}`, async ({ page }) => {
      await open(page, '/purchases');
      await expect(page.getByRole('button', { name: 'Ver PO-20261004-0001' })).toBeVisible();
      await snap(page, `compras-${look.suffix}`);
    });

    test(`proveedores ${look.suffix}`, async ({ page }) => {
      await open(page, '/suppliers');
      await expect(page.getByRole('button', { name: 'Editar Café Finca Las Nubes' })).toBeVisible();
      await snap(page, `proveedores-${look.suffix}`);
    });
  });
}

test.describe('Productos', () => {
  test.use({ prepareData: productPhotos });

  test('productos en un teléfono chico', async ({ page }) => {
    await page.setViewportSize(SIZES.smallPhone);
    await open(page, '/products');
    await expect(page.getByRole('link', { name: 'Editar Latte' })).toBeVisible();
    await snap(page, 'productos-telefono-chico');
  });

  test('productos de panadería con existencias', async ({ page }) => {
    // Panadería has the products that control their stock: the column shows their badges (one low).
    await open(page, '/products');
    await page.getByLabel('Categoría').selectOption({ label: 'Panadería' });
    await expect(page.getByRole('link', { name: 'Editar Pan de elote' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Editar Latte' })).toBeHidden();
    await snap(page, 'productos-panaderia');
  });

  test('producto editar, opciones y extras', async ({ page }) => {
    await open(page, '/products/c0cf3283-f80a-468d-b62f-9d7e6be47ec1/edit');
    await expect(page.getByRole('button', { name: 'Controlar existencias' })).toBeVisible();
    // The option groups alone (no accessible handle, so its own class): a change in them is not lost in the page.
    const editor = page.locator('.ta-options-editor');
    await expect(editor).toContainText('Leche');
    await snapElement(editor, 'producto-opciones');
  });
});

test.describe('Categorías', () => {
  test('categorías, nueva categoría', async ({ page }) => {
    await open(page, '/categories');
    await page.getByRole('button', { name: 'Nueva categoría' }).click();
    await expect(await dialog(page)).toContainText('Nueva categoría');
    await snap(page, 'categorias-nueva-dialogo', { fullPage: false });
  });

  test('categorías, editar categoría', async ({ page }) => {
    await open(page, '/categories');
    await page.getByRole('button', { name: 'Editar Bebidas calientes' }).click();
    const editing = await dialog(page);
    await expect(editing.getByLabel('Nombre')).toHaveValue('Bebidas calientes');
    await snap(page, 'categorias-editar-dialogo', { fullPage: false });
  });

  test('categorías, editar categoría en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/categories');
    await page.getByRole('button', { name: 'Editar Bebidas calientes' }).click();
    const editing = await dialog(page);
    await expect(editing.getByLabel('Nombre')).toHaveValue('Bebidas calientes');
    await snap(page, 'categorias-editar-dialogo-telefono', { fullPage: false });
  });

  test('categorías con las archivadas', async ({ page }) => {
    await open(page, '/categories');
    await page.getByRole('switch', { name: 'Mostrar archivadas' }).check();
    await expect(page.getByRole('button', { name: 'Restaurar Temporada' })).toBeVisible();
    await snap(page, 'categorias-archivadas');
  });

  test('categorías, archivar una con productos', async ({ page }) => {
    // A category with products asks where to move them before it is archived.
    await open(page, '/categories');
    await page.getByRole('button', { name: 'Archivar Bebidas calientes' }).click();
    await expect(await dialog(page)).toContainText('Pasar los productos a');
    await snap(page, 'categorias-archivar-dialogo', { fullPage: false });
  });

  test('categorías, instalar plantilla', async ({ page }) => {
    await open(page, '/categories');
    await page.getByRole('button', { name: 'Instalar plantilla' }).click();
    const templates = await dialog(page);
    await expect(templates).toContainText('Lavanderia de calzado');
    await snap(page, 'categorias-plantillas-dialogo', { fullPage: false });
  });

  test.describe('oscuro', () => {
    test.use({ colorMode: 'dark' });

    test('categorías, editar categoría en oscuro', async ({ page }) => {
      await open(page, '/categories');
      await page.getByRole('button', { name: 'Editar Bebidas calientes' }).click();
      const editing = await dialog(page);
      await expect(editing.getByLabel('Nombre')).toHaveValue('Bebidas calientes');
      await snap(page, 'categorias-editar-dialogo-oscuro', { fullPage: false });
    });
  });
});

test.describe('Servicios', () => {
  test('servicios, editar servicio', async ({ page }) => {
    await open(page, '/services');
    await page.getByRole('button', { name: 'Editar Mantenimiento de cafetera espresso' }).click();
    const editing = await dialog(page);
    await expect(editing.getByLabel('Nombre')).toHaveValue('Mantenimiento de cafetera espresso');
    await snap(page, 'servicios-editar-dialogo', { fullPage: false });
  });
});

test.describe('Inventario', () => {
  // Each tab of the only row of tabs, and what shows its list has loaded: a tab's list can start loading as it opens,
  // after the click returns, and its empty state would otherwise pass for the screen.
  const TABS: Array<{ name: string; shot: string; loaded: (page: Page) => Promise<void> }> = [
    {
      name: 'Existencias',
      shot: 'inventario-existencias',
      loaded: (page) => expect(page.getByRole('cell', { name: 'Sucursal Coyoacán' }).first()).toBeVisible(),
    },
    {
      name: 'Entradas y salidas',
      shot: 'inventario-entradas-y-salidas',
      loaded: (page) => expect(page.getByRole('button', { name: 'Ver el movimiento INV-20261004-0004' })).toBeVisible(),
    },
    {
      name: 'Historial por insumo',
      shot: 'inventario-historial-por-insumo',
      loaded: (page) => expect(page.getByRole('cell', { name: 'INV-20261002-0001' })).toBeVisible(),
    },
    {
      // The first item in alphabetical order opens with its recipe (Agua de jamaica).
      name: 'Recetas',
      shot: 'inventario-recetas',
      loaded: (page) => expect(page.getByLabel('Notas de la línea 1')).toHaveValue('Concentrado preparado diario'),
    },
    {
      name: 'Ubicaciones',
      shot: 'inventario-ubicaciones',
      loaded: (page) => expect(page.getByRole('button', { name: 'Editar Kiosko Parque México' })).toBeVisible(),
    },
    {
      name: 'Alertas',
      shot: 'inventario-alertas',
      loaded: (page) => expect(page.getByRole('cell', { name: 'Totopos para chilaquiles' })).toBeVisible(),
    },
  ];

  for (const tab of TABS) {
    test(`inventario, pestaña ${tab.name}`, async ({ page }) => {
      await open(page, '/inventory');
      await page.getByRole('tab', { name: tab.name }).click();
      await expect(page.getByRole('tab', { name: tab.name })).toHaveAttribute('aria-selected', 'true');
      await tab.loaded(page);
      await snap(page, tab.shot);
    });
  }

  test('inventario, editar insumo', async ({ page }) => {
    await open(page, '/inventory');
    await page.getByRole('button', { name: 'Editar Leche entera' }).click();
    const editing = await dialog(page);
    await expect(editing.getByLabel('Nombre *')).toHaveValue('Leche entera');
    await snap(page, 'inventario-insumo-dialogo', { fullPage: false });
  });

  test('inventario, editar la existencia de un producto', async ({ page }) => {
    // Pan de elote is the stock of a product: its unit is the product's and cannot change here.
    await open(page, '/inventory');
    await page.getByRole('button', { name: 'Editar Pan de elote' }).click();
    const editing = await dialog(page);
    await expect(editing.getByText('Es la unidad del producto.')).toBeVisible();
    await snap(page, 'inventario-insumo-producto-dialogo', { fullPage: false });
  });

  test('inventario, registrar movimiento', async ({ page }) => {
    await open(page, '/inventory');
    await page.getByRole('tab', { name: 'Entradas y salidas' }).click();
    await page.getByRole('button', { name: 'Registrar movimiento' }).click();
    await expect(await dialog(page)).toContainText('Registrar movimiento de inventario');
    await snap(page, 'inventario-movimiento-dialogo', { fullPage: false });
  });

  test('inventario, detalle de un movimiento', async ({ page }) => {
    await open(page, '/inventory');
    await page.getByRole('tab', { name: 'Entradas y salidas' }).click();
    await page.getByRole('button', { name: 'Ver el movimiento INV-20261004-0001' }).click();
    const detail = await dialog(page);
    await expect(detail).toContainText('Recepción parcial de lácteos');
    await snap(page, 'inventario-movimiento-detalle-dialogo', { fullPage: false });
  });

  test('inventario, editar ubicación', async ({ page }) => {
    await open(page, '/inventory');
    await page.getByRole('tab', { name: 'Ubicaciones' }).click();
    await page.getByRole('button', { name: 'Editar Sucursal Roma Norte' }).click();
    const editing = await dialog(page);
    await expect(editing.getByLabel('Nombre *')).toHaveValue('Sucursal Roma Norte');
    await snap(page, 'inventario-ubicacion-dialogo', { fullPage: false });
  });
});

test.describe('Compras', () => {
  test('compras, detalle de una compra', async ({ page }) => {
    // A partial reception: what came, what is missing and every action its status allows.
    await open(page, '/purchases');
    await page.getByRole('button', { name: 'Ver PO-20261003-0002' }).click();
    await expect(await dialog(page)).toContainText('faltan');
    await snap(page, 'compras-detalle-escritorio', { fullPage: false });
  });

  test('compras, detalle de una compra en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/purchases');
    await page.getByRole('button', { name: 'PO-20261003-0002', exact: true }).click();
    await expect(await dialog(page)).toContainText('faltan');
    await snap(page, 'compras-detalle-telefono', { fullPage: false });
  });

  test('compras, recibir mercancía', async ({ page }) => {
    await open(page, '/purchases');
    await page.getByRole('button', { name: 'Recibir PO-20261002-0001' }).click();
    const receiving = await dialog(page);
    await expect(receiving.getByRole('button', { name: 'Registrar recepción' })).toBeVisible();
    await snap(page, 'compras-recibir-escritorio', { fullPage: false });
  });

  test('compras, recibir mercancía en el teléfono', async ({ page }) => {
    // On a phone the reception starts from the detail, the way the folio opens it.
    await page.setViewportSize(SIZES.phone);
    await open(page, '/purchases');
    await page.getByRole('button', { name: 'PO-20261002-0001', exact: true }).click();
    const detail = await dialog(page);
    await detail.getByRole('button', { name: 'Recibir mercancía' }).click();
    await expect(detail.getByRole('button', { name: 'Registrar recepción' })).toBeVisible();
    await snap(page, 'compras-recibir-telefono', { fullPage: false });
  });

  test('compras, nueva compra', async ({ page }) => {
    await open(page, '/purchases');
    await page.getByRole('button', { name: 'Nueva compra' }).click();
    await expect(await dialog(page)).toContainText('Registrar compra');
    await snap(page, 'compras-nueva-dialogo', { fullPage: false });
  });

  test.describe('oscuro', () => {
    test.use({ colorMode: 'dark' });

    test('compras, detalle de una compra en oscuro', async ({ page }) => {
      await open(page, '/purchases');
      await page.getByRole('button', { name: 'Ver PO-20261003-0002' }).click();
      await expect(await dialog(page)).toContainText('faltan');
      await snap(page, 'compras-detalle-escritorio-oscuro', { fullPage: false });
    });
  });
});

test.describe('Proveedores', () => {
  test('proveedores, editar proveedor', async ({ page }) => {
    await open(page, '/suppliers');
    await page.getByRole('button', { name: 'Editar Café Finca Las Nubes' }).click();
    const editing = await dialog(page);
    await expect(editing.getByLabel('Nombre *')).toHaveValue('Café Finca Las Nubes');
    await snap(page, 'proveedores-editar-dialogo', { fullPage: false });
  });

  test('proveedores, nuevo proveedor en el teléfono', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/suppliers');
    await page.getByRole('button', { name: 'Nuevo proveedor' }).click();
    await expect(await dialog(page)).toContainText('Crear proveedor');
    await snap(page, 'proveedores-nuevo-dialogo-telefono', { fullPage: false });
  });
});
