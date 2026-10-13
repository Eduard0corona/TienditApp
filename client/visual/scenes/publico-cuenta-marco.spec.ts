import type { Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import { SIZES, dialog, open, settle, snap } from '../helpers';
import {
  CON_DOS_PASOS,
  INICIO_CON_ERROR,
  MENU_A_DOMICILIO,
  MENU_CERRADO,
  MENU_CIERRE_ESPECIAL,
  MENU_HORARIO_ESPECIAL,
  NEGOCIO_SUSPENDIDO,
  PRINCIPAL_SOLO_A_DOMICILIO,
  PUBLICO_ROUTES,
} from './publico-cuenta-marco.mocks';

// The screens before signing in (login, registration, recovery, confirmations and the invitation), the public menu of
// a business and the frame of the app around every screen: its menus, the search, the menu on each size and the
// states that replace a screen (no connection, a suspended business, an API that fails). A dialog, a drawer or an
// open menu is taken at the size of the screen: it covers the screen, not the page.

const LOOKS = [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'telefono', colorMode: 'light', viewport: SIZES.phone },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
] as const;

test.use({ extraRoutes: PUBLICO_ROUTES });

/** A dialog, a drawer or an open menu as the screen shows it. */
async function snapScreen(page: Page, name: string) {
  await snap(page, name, { fullPage: false });
}

/**
 * Like `snap`, but the pointer stays where the scene left it: the condensed menu shows a label only while the pointer
 * is over its icon, and `snap` moves the pointer away first.
 */
async function snapHovering(page: Page, name: string) {
  await settle(page);
  await expect(page).toHaveScreenshot(`${name}.png`, { fullPage: false });
}

/** Opens the phone menu from the top bar. */
async function openPhoneMenu(page: Page) {
  const toggle = page.getByRole('button', { name: 'Mostrar u ocultar el menú' });
  await toggle.click();
  await expect(toggle).toHaveAttribute('aria-expanded', 'true');
  await expect(page.getByRole('button', { name: 'Cerrar el menú' })).toBeVisible();
}

/** Opens the options of the Latte. */
async function openLatteOptions(page: Page) {
  await page.getByRole('button', { name: 'Agregar Latte' }).click();
  return dialog(page, 'Latte');
}

/** Adds a Latte to the cart from its options dialog: large, with almond milk. */
async function addLatte(page: Page) {
  const options = await openLatteOptions(page);
  await options.getByLabel('Grande').check();
  await options.getByLabel('Almendra').check();
  await options.getByRole('button', { name: /^Agregar · / }).click();
  await expect(options).toBeHidden();
}

// ------------------------------------------------------------------ before signing in

for (const look of LOOKS) {
  test.describe(`Pantallas de acceso ${look.suffix}`, () => {
    test.use({ signedIn: false, colorMode: look.colorMode });
    test.beforeEach(async ({ page }) => {
      await page.setViewportSize(look.viewport);
    });

    test(`login ${look.suffix}`, async ({ page }) => {
      await open(page, '/login');
      await expect(page.getByRole('heading', { name: 'Iniciar sesión' })).toBeVisible();
      await snap(page, `login-${look.suffix}`);
    });

    test(`registro tipo de cuenta ${look.suffix}`, async ({ page }) => {
      await open(page, '/register');
      await expect(page.getByRole('button', { name: /Tengo un negocio/ })).toBeVisible();
      await snap(page, `registro-tipo-de-cuenta-${look.suffix}`);
    });

    test(`registro negocio ${look.suffix}`, async ({ page }) => {
      await open(page, '/register');
      await page.getByRole('button', { name: /Tengo un negocio/ }).click();
      await expect(page.getByRole('heading', { name: 'Registro de negocio' })).toBeVisible();
      await snap(page, `registro-negocio-${look.suffix}`);
    });

    test(`recuperar contraseña ${look.suffix}`, async ({ page }) => {
      await open(page, '/forgot-password');
      await expect(page.getByRole('button', { name: 'Enviar enlace' })).toBeVisible();
      await snap(page, `recuperar-contrasena-${look.suffix}`);
    });

    test(`nueva contraseña ${look.suffix}`, async ({ page }) => {
      await open(page, '/reset-password?userId=user-mariana&token=enlace-visual');
      await expect(page.getByLabel('Confirmar contraseña')).toBeVisible();
      await snap(page, `nueva-contrasena-${look.suffix}`);
    });

    test(`revisa tu correo ${look.suffix}`, async ({ page }) => {
      await open(page, '/check-email');
      await expect(page.getByRole('heading', { name: 'Revisa tu correo' })).toBeVisible();
      await snap(page, `revisa-tu-correo-${look.suffix}`);
    });

    test(`confirmar correo enlace incompleto ${look.suffix}`, async ({ page }) => {
      await open(page, '/confirm-email');
      await expect(page.getByRole('alert')).toContainText('El enlace está incompleto');
      await snap(page, `confirmar-correo-${look.suffix}`);
    });

    test(`confirmar teléfono ${look.suffix}`, async ({ page }) => {
      await open(page, '/confirm-phone?userId=user-mariana');
      await expect(page.getByLabel('Código')).toBeVisible();
      await snap(page, `confirmar-telefono-${look.suffix}`);
    });

    test(`aceptar invitación ${look.suffix}`, async ({ page }) => {
      await open(page, '/aceptar-invitacion?userId=user-7&token=CfDJ8demo-token');
      await expect(page.getByRole('heading', { name: 'Hola, Andrea' })).toBeVisible();
      await snap(page, `aceptar-invitacion-${look.suffix}`);
    });
  });
}

test.describe('Login con verificación en dos pasos', () => {
  test.use({ signedIn: false, prepareData: CON_DOS_PASOS });

  test('login dos pasos', async ({ page }) => {
    await open(page, '/login');
    await page.getByLabel('Correo').fill('mariana.ortiz@cafelaesquina.mx');
    await page.getByLabel('Contraseña', { exact: true }).fill('Esquina2026');
    await page.getByRole('button', { name: 'Iniciar sesión' }).click();
    await expect(page.getByRole('heading', { name: 'Verificación en dos pasos' })).toBeVisible();
    await snap(page, 'login-dos-pasos-escritorio');
  });
});

test.describe('Pantallas de acceso: otros estados', () => {
  test.use({ signedIn: false });

  test('login con la cuenta recién activada', async ({ page }) => {
    // The link of the confirmation email activates the account and lands on the login with the notice.
    await open(page, '/confirm-email?userId=user-mariana&token=enlace-visual');
    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByRole('status')).toContainText('Tu cuenta quedó activada');
    await snap(page, 'login-cuenta-activada-escritorio');
  });

  test('confirmar correo con el enlace vencido', async ({ page }) => {
    await open(page, '/confirm-email?userId=user-mariana&token=vencido');
    await expect(page.getByRole('alert')).toContainText('El enlace ya venció');
    await snap(page, 'confirmar-correo-vencido-escritorio');
  });

  test('registro de cliente', async ({ page }) => {
    await open(page, '/register');
    await page.getByRole('button', { name: /Soy cliente/ }).click();
    await expect(page.getByRole('heading', { name: 'Registro de cliente' })).toBeVisible();
    await snap(page, 'registro-cliente-escritorio');
  });

  test('registro de negocio con datos fiscales y dirección', async ({ page }) => {
    await open(page, '/register');
    await page.getByRole('button', { name: /Tengo un negocio/ }).click();
    await page.getByRole('button', { name: 'Agregar datos fiscales y dirección (opcional)' }).click();
    await expect(page.getByLabel('Giro')).toBeVisible();
    await snap(page, 'registro-negocio-datos-opcionales-escritorio');
  });

  test('recuperar contraseña por SMS', async ({ page }) => {
    await open(page, '/forgot-password');
    await page.getByRole('button', { name: 'Por SMS' }).click();
    await expect(page.getByRole('button', { name: 'Enviar código' })).toBeVisible();
    await snap(page, 'recuperar-contrasena-sms-escritorio');
  });

  test('recuperar contraseña con el enlace enviado', async ({ page }) => {
    await open(page, '/forgot-password');
    await page.getByLabel('Correo').fill('mariana.ortiz@cafelaesquina.mx');
    await page.getByRole('button', { name: 'Enviar enlace' }).click();
    await expect(page.getByRole('status')).toContainText('te llegará un enlace');
    await snap(page, 'recuperar-contrasena-enviado-escritorio');
  });

  test('nueva contraseña con un enlace que no sirve', async ({ page }) => {
    await open(page, '/reset-password');
    await expect(page.getByRole('alert')).toContainText('El enlace no es válido');
    await snap(page, 'nueva-contrasena-enlace-invalido-escritorio');
  });

  test('confirmar teléfono cambiando el número', async ({ page }) => {
    await open(page, '/confirm-phone?userId=user-mariana');
    await page.getByRole('button', { name: '¿Número equivocado? Cambiarlo' }).click();
    await expect(page.getByLabel('Número correcto')).toBeVisible();
    await snap(page, 'confirmar-telefono-cambiar-numero-escritorio');
  });

  test('aceptar una invitación ya usada', async ({ page }) => {
    await open(page, '/aceptar-invitacion?userId=user-7&token=usada');
    await expect(page.getByRole('alert')).toContainText('La invitación ya se usó');
    await snap(page, 'aceptar-invitacion-usada-escritorio');
  });
});

// ------------------------------------------------------------------ public menu

for (const businessTheme of [0, 1, 2, 3, 4, 5, 6, 7]) {
  test.describe(`Menú público tema ${businessTheme}`, () => {
    test.use({ signedIn: false, businessTheme });

    test(`menú público tema ${businessTheme} teléfono`, async ({ page }) => {
      await page.setViewportSize(SIZES.phone);
      await open(page, '/m/cafe-la-esquina');
      await expect(page.getByRole('heading', { name: 'Café La Esquina' })).toBeVisible();
      await snap(page, `menu-publico-tema-${businessTheme}-telefono`);
    });
  });
}

for (const look of [
  { suffix: 'escritorio', colorMode: 'light', viewport: SIZES.desktop },
  { suffix: 'escritorio-oscuro', colorMode: 'dark', viewport: SIZES.desktop },
  { suffix: 'telefono-oscuro', colorMode: 'dark', viewport: SIZES.phone },
] as const) {
  test.describe(`Menú público ${look.suffix}`, () => {
    test.use({ signedIn: false, colorMode: look.colorMode });

    test(`menú público ${look.suffix}`, async ({ page }) => {
      await page.setViewportSize(look.viewport);
      await open(page, '/m/cafe-la-esquina');
      await expect(page.getByRole('heading', { name: 'Café La Esquina' })).toBeVisible();
      await snap(page, `menu-publico-${look.suffix}`);
    });
  });
}

test.describe('Menú público: pedir', () => {
  test.use({ signedIn: false });
  test.beforeEach(async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
  });

  test('menú público con el horario de la semana', async ({ page }) => {
    await open(page, '/m/cafe-la-esquina');
    await page.getByText('Hoy: 9:00 a 15:00').click();
    await expect(page.getByRole('list', { name: 'Horario de la semana' })).toBeVisible();
    await snap(page, 'menu-publico-horario-telefono');
  });

  test('menú público producto con opciones', async ({ page }) => {
    await open(page, '/m/cafe-la-esquina');
    const options = await openLatteOptions(page);
    await options.getByLabel('Grande').check();
    await options.getByLabel('Shot extra').check();
    await expect(options.getByRole('button', { name: /^Agregar · / })).toBeEnabled();
    await snapScreen(page, 'menu-publico-opciones-telefono');
  });

  test('menú público carrito', async ({ page }) => {
    await open(page, '/m/cafe-la-esquina');
    await addLatte(page);
    await page.getByRole('button', { name: 'Agregar Café americano' }).click();
    await page.getByRole('button', { name: 'Agregar Concha de vainilla' }).click();
    await page.getByRole('button', { name: 'Agregar Concha de vainilla' }).click();
    await page.getByRole('button', { name: /^Ver pedido · 4 · / }).click();
    await dialog(page, 'Tu pedido');
    await snapScreen(page, 'menu-publico-carrito-telefono');
  });

  test('menú público pedido enviado', async ({ page }) => {
    await open(page, '/m/cafe-la-esquina');
    await page.getByRole('button', { name: 'Agregar Café americano' }).click();
    await page.getByRole('button', { name: /^Ver pedido · 1 · / }).click();
    const cart = await dialog(page, 'Tu pedido');
    await cart.getByLabel('Tu nombre').fill('Valeria Morales');
    await cart.getByLabel('Teléfono (WhatsApp)').fill('55 2210 7765');
    await cart.getByRole('button', { name: /^Enviar pedido · / }).click();
    await expect(page.getByRole('heading', { name: '¡Pedido enviado!' })).toBeVisible();
    await snap(page, 'menu-publico-pedido-enviado-telefono');
  });

  test('menú público de un negocio que no existe', async ({ page }) => {
    await open(page, '/m/no-existe');
    await expect(page.getByRole('heading', { name: 'Este negocio no tiene menú en línea' })).toBeVisible();
    await snap(page, 'menu-publico-no-existe-telefono');
  });
});

test.describe('Menú público fuera de horario', () => {
  test.use({ signedIn: false, prepareData: MENU_CERRADO });
  test.beforeEach(async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
  });

  test('menú público cerrado por horario', async ({ page }) => {
    await open(page, '/m/cafe-la-esquina');
    await expect(page.getByText('Fuera de horario no recibimos pedidos en línea')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Agregar Latte' })).toHaveCount(0);
    await snap(page, 'menu-publico-cerrado-telefono');
  });

  test('menú público desde el QR de una mesa', async ({ page }) => {
    // At a table in use the customer is already seated: the business takes the order even while closed.
    await open(page, '/m/cafe-la-esquina?mesa=Mesa%204');
    await expect(page.getByText('Pedido para Mesa 4')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Agregar Latte' })).toBeVisible();
    await snap(page, 'menu-publico-mesa-telefono');
  });
});

test.describe('Menú público a domicilio', () => {
  test.use({ signedIn: false, prepareData: MENU_A_DOMICILIO });
  test.beforeEach(async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
  });

  /** Two conchas and an americano, to Roma Norte: the server says what it charges to 06700. */
  async function cartToRoma(page: Page) {
    await open(page, '/m/cafe-la-esquina');
    await page.getByRole('button', { name: 'Agregar Café americano' }).click();
    await page.getByRole('button', { name: 'Agregar Concha de vainilla' }).click();
    await page.getByRole('button', { name: 'Agregar Concha de vainilla' }).click();
    await page.getByRole('button', { name: /^Ver pedido · 3 · / }).click();
    const cart = await dialog(page, 'Tu pedido');
    await cart.getByRole('radio', { name: 'A domicilio' }).click();
    await cart.getByLabel('Código postal').fill('06700');
    // The server answered for 06700: the delivery enters the total.
    await expect(cart.getByText('Envío a domicilio')).toBeVisible();
    return cart;
  }

  test('menú público que también entrega a domicilio', async ({ page }) => {
    await open(page, '/m/cafe-la-esquina');
    await expect(page.getByText('También a domicilio: al pedir te decimos si llegamos a tu código postal.')).toBeVisible();
    await snap(page, 'menu-publico-tambien-a-domicilio-telefono');
  });

  test('menú público carrito a domicilio por debajo del mínimo', async ({ page }) => {
    const cart = await cartToRoma(page);
    await expect(cart.getByText('El pedido mínimo para el 06700 es de $150.00.')).toBeVisible();
    await snapScreen(page, 'menu-publico-carrito-a-domicilio-minimo-telefono');
  });

  test('menú público carrito a domicilio', async ({ page }) => {
    const cart = await cartToRoma(page);
    await page.keyboard.press('Escape');
    await expect(cart).toBeHidden();
    // Enough for the minimum: three lattes more.
    for (let latte = 0; latte < 3; latte += 1) await addLatte(page);
    await page.getByRole('button', { name: /^Ver pedido · 6 · / }).click();
    const full = await dialog(page, 'Tu pedido');
    await full.getByLabel('Dirección').fill('Colima 145, depto. 3, Roma Norte');
    await full.getByLabel('Referencias (opcional)').fill('Portón negro, timbre 3');
    await expect(full.getByText('Envío $35.00 · gratis desde $400.00 · pedido mínimo $150.00')).toBeVisible();
    await expect(full.getByRole('button', { name: /^Enviar pedido · / })).toBeEnabled();
    await snapScreen(page, 'menu-publico-carrito-a-domicilio-telefono');
  });

  test('menú público carrito a domicilio con cuánto paga', async ({ page }) => {
    const cart = await cartToRoma(page);
    await cart.getByLabel('Código postal').fill('06140');
    await expect(cart.getByText('Envío $45.00')).toBeVisible();
    await cart.getByLabel('Dirección').fill('Ámsterdam 210, Condesa');
    await cart.getByLabel('¿Con cuánto pagas? (opcional)').fill('200');
    await expect(cart.getByText('Tu cambio:')).toBeVisible();
    await snapScreen(page, 'menu-publico-carrito-paga-con-telefono');
  });

  test('menú público pedido a domicilio enviado', async ({ page }) => {
    const cart = await cartToRoma(page);
    await cart.getByLabel('Código postal').fill('06140');
    await expect(cart.getByText('Envío $45.00')).toBeVisible();
    await cart.getByLabel('Dirección').fill('Ámsterdam 210, Condesa');
    await cart.getByLabel('Tu nombre').fill('Valeria Morales');
    await cart.getByLabel('Teléfono (WhatsApp)').fill('55 2210 7765');
    await cart.getByRole('button', { name: /^Enviar pedido · / }).click();
    await expect(page.getByRole('heading', { name: '¡Pedido enviado!' })).toBeVisible();
    await expect(page.getByText('Incluye envío de $45.00')).toBeVisible();
    await snap(page, 'menu-publico-pedido-a-domicilio-enviado-telefono');
  });
});

test.describe('Menú público de una cocina sin local', () => {
  test.use({ signedIn: false, prepareData: PRINCIPAL_SOLO_A_DOMICILIO });
  test.beforeEach(async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
  });

  test('menú público solo a domicilio', async ({ page }) => {
    // The main branch only delivers: no address and no «Cómo llegar», only that there are no premises to visit.
    await open(page, '/m/cafe-la-esquina');
    await expect(page.getByText('Solo a domicilio: no tenemos local para visitar.')).toBeVisible();
    await expect(page.getByRole('link', { name: /Cómo llegar/ })).toHaveCount(0);
    await snap(page, 'menu-publico-solo-a-domicilio-telefono');
  });
});

test.describe('Menú público en un día especial', () => {
  test.use({ signedIn: false });
  test.beforeEach(async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
  });

  test.describe('cerrado', () => {
    test.use({ prepareData: MENU_CIERRE_ESPECIAL });

    test('menú público cerrado por un día especial', async ({ page }) => {
      // The API words the closing (why and when it opens again) and lists the special days ahead with the week.
      await open(page, '/m/cafe-la-esquina');
      await expect(page.getByText('por Inventario · abre mañana a las 8:00')).toBeVisible();
      await expect(page.getByText('No recibimos pedidos en línea por Inventario')).toBeVisible();
      await expect(page.getByRole('button', { name: 'Agregar Latte' })).toHaveCount(0);
      await page.getByText('Hoy: Cerrado · Inventario').click();
      await expect(page.getByRole('list', { name: 'Días especiales' })).toContainText('Lun 2 nov · Cerrado · Día de Muertos');
      await snap(page, 'menu-publico-cierre-especial-telefono');
    });
  });

  test.describe('con otro horario', () => {
    test.use({ prepareData: MENU_HORARIO_ESPECIAL });

    test('menú público con horario especial', async ({ page }) => {
      await open(page, '/m/cafe-la-esquina');
      await expect(page.getByText('cierra a las 14:00 por Inventario')).toBeVisible();
      await expect(page.getByText('Hoy: 10:00 a 14:00 · Inventario')).toBeVisible();
      await expect(page.getByText('El viernes 9 cerramos por Aniversario.')).toBeVisible();
      await expect(page.getByRole('button', { name: 'Agregar Latte' })).toBeVisible();
      await snap(page, 'menu-publico-horario-especial-telefono');
    });
  });
});

// ------------------------------------------------------------------ the frame of the app (Inicio as the host)

for (const look of LOOKS) {
  test.describe(`Marco ${look.suffix}`, () => {
    test.use({ colorMode: look.colorMode });
    test.beforeEach(async ({ page }) => {
      await page.setViewportSize(look.viewport);
    });

    test(`marco menú de la cuenta ${look.suffix}`, async ({ page }) => {
      await open(page, '/dashboard');
      await page.getByRole('button', { name: 'Menú de tu cuenta' }).click();
      await expect(page.getByRole('link', { name: 'Mi perfil' })).toBeVisible();
      await snapScreen(page, `marco-menu-cuenta-${look.suffix}`);
    });

    test(`marco selector de negocio ${look.suffix}`, async ({ page }) => {
      await open(page, '/dashboard');
      await page.getByRole('button', { name: /^Negocio actual/ }).click();
      await expect(page.getByRole('button', { name: /La Esquina Catering/ })).toBeVisible();
      await snapScreen(page, `marco-selector-negocio-${look.suffix}`);
    });

    test(`marco avisos ${look.suffix}`, async ({ page }) => {
      await open(page, '/dashboard');
      const bell = page.getByRole('button', { name: /^Avisos: / });
      await bell.click();
      await expect(bell).toHaveAttribute('aria-expanded', 'true');
      await snapScreen(page, `marco-avisos-${look.suffix}`);
    });

    test(`marco búsqueda ${look.suffix}`, async ({ page }) => {
      await open(page, '/dashboard');
      await page.keyboard.press('Control+k');
      await dialog(page, /Buscar pedidos/);
      await snapScreen(page, `marco-busqueda-${look.suffix}`);
    });
  });
}

test.describe('Marco: búsqueda y menú en cada tamaño', () => {
  test('marco búsqueda con resultados', async ({ page }) => {
    await open(page, '/dashboard');
    await page.getByRole('button', { name: 'Buscar (Ctrl K)' }).click();
    const search = await dialog(page, /Buscar pedidos/);
    await search.getByRole('combobox').fill('lat');
    await expect(search.getByRole('option', { name: /Chai latte/ })).toBeVisible();
    await expect(search.getByText('Buscando…')).toHaveCount(0);
    await snapScreen(page, 'marco-busqueda-resultados-escritorio');
  });

  for (const [size, viewport] of [['telefono', SIZES.phone], ['telefono-chico', SIZES.smallPhone]] as const) {
    test(`marco menú abierto ${size}`, async ({ page }) => {
      await page.setViewportSize(viewport);
      await open(page, '/dashboard');
      await openPhoneMenu(page);
      await snapScreen(page, `marco-menu-abierto-${size}`);
    });
  }

  for (const [size, viewport, entry] of [['tableta', SIZES.tablet, '/orders'], ['vertical', SIZES.upright, '/settings/business']] as const) {
    test(`marco menú condensado ${size}`, async ({ page }) => {
      await page.setViewportSize(viewport);
      await open(page, '/dashboard');
      // Condensed, the labels are hidden (not in the accessible name): the entry is found by where it goes.
      await page.getByRole('navigation', { name: 'Menú principal' }).locator(`a[href="${entry}"]`).hover();
      await snapHovering(page, `marco-menu-condensado-${size}`);
    });
  }
});

test.describe('Marco oscuro: menú del teléfono', () => {
  test.use({ colorMode: 'dark' });

  test('marco menú abierto teléfono oscuro', async ({ page }) => {
    await page.setViewportSize(SIZES.phone);
    await open(page, '/dashboard');
    await openPhoneMenu(page);
    await snapScreen(page, 'marco-menu-abierto-telefono-oscuro');
  });
});

// ------------------------------------------------------------------ states that replace a screen

test.describe('Marco sin conexión', () => {
  for (const [size, viewport] of [['escritorio', SIZES.desktop], ['telefono', SIZES.phone]] as const) {
    test(`marco sin conexión ${size}`, async ({ page, context }) => {
      await page.setViewportSize(viewport);
      // The top bar has no connection indicator: Vender is where the app says it sells without connection.
      await open(page, '/sell');
      await context.setOffline(true);
      await expect(page.getByRole('status').filter({ hasText: 'Sin conexión' })).toBeVisible();
      await snap(page, `marco-sin-conexion-${size}`);
    });
  }
});

test.describe('Marco con el negocio suspendido', () => {
  test.use({ prepareData: NEGOCIO_SUSPENDIDO });

  for (const [size, viewport] of [['escritorio', SIZES.desktop], ['telefono', SIZES.phone]] as const) {
    test(`marco negocio suspendido ${size}`, async ({ page }) => {
      await page.setViewportSize(viewport);
      await open(page, '/dashboard');
      await expect(page.getByRole('heading', { name: 'Café La Esquina está suspendido' })).toBeVisible();
      await expect(page.getByText('Sin pago después de los días de gracia.')).toBeVisible();
      await snap(page, `marco-negocio-suspendido-${size}`);
    });
  }
});

test.describe('Marco con un error de la API', () => {
  test.use({ prepareData: INICIO_CON_ERROR });

  for (const [size, viewport] of [['escritorio', SIZES.desktop], ['telefono', SIZES.phone]] as const) {
    test(`marco error de la API ${size}`, async ({ page }) => {
      await page.setViewportSize(viewport);
      await open(page, '/dashboard');
      await expect(page.getByText('No se pudieron cargar las cifras de Inicio.')).toBeVisible();
      await snap(page, `marco-error-api-${size}`);
    });
  }
});
