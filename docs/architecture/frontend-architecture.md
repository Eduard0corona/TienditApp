# Arquitectura frontend

## Estado actual comprobado

`client` usa React 19.2, TypeScript 5.9, Vite 7, React Router 7, Axios, React Hook Form, Bootstrap/React Bootstrap y SignalR. `App.tsx` define rutas con `lazy` y `Suspense`; `ProtectedRoute` comprueba sesión y `AccessRoute` decide si la pantalla aplica al usuario con la matriz de `src/utils/access.ts` (permiso, capability y si administra el negocio, igual que cada caso de uso de la API).

Estructura principal:

- `src/api`: una instancia Axios y módulos por recurso.
- `src/pages`: pantallas por capacidad funcional.
- `src/components/common` y `layout`: reutilización y shell.
- `src/hooks`: Auth/Tenant y conexiones SignalR.
- `src/contexts`: estado de autenticación y tenant.
- `src/types/index.ts`: contratos TypeScript compartidos. Los enums del dominio no se escriben aquí: `npm run api:types` los genera del contrato en `src/api/generated/enums.ts` como objetos `as const` con los nombres y números del servidor (la API los intercambia como enteros) y `index.ts` los reexporta. Un enum nuevo se agrega al contrato, no a mano. `ClientEnumContractTests` (WebApi.Tests) los compara con el dominio.
- `src/utils/tenantCapabilities.ts`: helpers de capabilities.

El access token se mantiene en memoria y se agrega con interceptor. El refresh token usa cookie `HttpOnly`; `localStorage` solo conserva un marcador de sesión. Vite proxya API, uploads y hubs a `localhost:5203`.

Sesión y tiempo real:

- `refreshAccessToken` (en `src/api/axios.ts`) es single-flight y se serializa entre pestañas con la Web Locks API, porque el refresh token rota en cada uso.
- Un refresh rechazado (400, 401 o 403) emite `expired` y la sesión se cierra. Un 429, un error del servidor o uno de red la conservan y la siguiente llamada reintenta. Un 409 significa que otra petición acaba de renovar la sesión: se reintenta con la cookie nueva. Si un refresh devuelve otro tenant (otra pestaña cambió de tenant), la pestaña recarga.
- `useSessionSync` propaga logout y cambio de tenant a las demás pestañas con `BroadcastChannel`; cambiar de tenant recarga la aplicación.
- `src/api/signalr.ts` crea las conexiones: reintenta indefinidamente, reintenta el primer arranque, reinicia tras un cierre del servidor y pide un token vigente en cada (re)conexión: renueva antes el que vence en menos de 30 segundos o no trae `session_id`, porque fue emitido antes de las sesiones y la API ya no lo acepta (`isFreshForConnection`, Fase 8). Cambiar la contraseña cierra la sesión en este dispositivo también, porque la API termina todas las de la cuenta.
- `src/utils/labels.ts` concentra las etiquetas en español de estados, métodos de pago, tipos y roles (acepta el número de REST o el nombre del enum de SignalR); `src/api/errors.ts` (`getApiErrorMessage`, `getApiFieldErrors`, `getApiErrorCode`) es la única forma de leer errores de la API y decide por el `code` del `ProblemDetails`, nunca por el texto.
- `HomeRedirect`/`AccessRoute` llevan a cada usuario a la pantalla de su trabajo (`getLandingPath`); un administrador cuyo negocio no terminó el arranque guiado va a `/setup`. `isSafeFileUrl` solo deja enlazar archivos de `/uploads` o https; los comprobantes y las fotos de servicio son privados (`/files/...`): `PrivatePhoto` y `PrivateFileLink` los piden a la API con el token de la sesión y los muestran desde un object URL que se libera al terminar (Fase 8); `estimateOrderTotals` replica el desglose de IVA del backend en centavos enteros (el total definitivo es el de la API).

Shell y diseño (Fase 1):

- `src/components/layout/navigation.ts` define el menú por trabajo a partir de la matriz de acceso; `LeftSidebar`, la búsqueda global y la barra inferior del celular (`BottomTabBar`) lo leen de ahí. Desde la Fase 6, Gestión tiene cinco entradas como en la propuesta del diagnóstico: Catálogo (Productos, Servicios, Categorías), Inventario (Existencias, Compras, Proveedores), Clientes (Clientes, Campañas), Reportes y Configuración (Negocio, Sucursales, Equipo y permisos, WhatsApp, Respuestas automáticas, Seguridad y actividad, Primeros pasos). Una sección (`NavSection`) abre la primera de sus páginas que la persona puede abrir y desaparece sin ninguna; `PageTitle` muestra sus páginas como pestañas (`SectionTabs`, solo con dos o más) y la nombra en las migas. Las rutas no cambian y la búsqueda global sigue llegando a cada página.
- Fechas en la zona del negocio (Fase 6): `utils/dates.ts` formatea toda fecha y hora en la zona horaria del negocio (`TenantSettings.TimeZone`, que `TenantProvider` fija al cargar el negocio; sin negocio, la del dispositivo) y calcula "hoy" (`getBusinessToday`) y el inicio y fin de un día (`businessDayStart`, `businessDayEnd`) para los filtros de Inicio, Pedidos, Caja, Inventario, Reportes y Auditoría; una fecha de calendario se muestra sin zona (`formatCalendarDate`). Ninguna pantalla usa `toLocaleString` ni `T00:00:00` para fechas. La zona se elige en Configuración › Negocio entre las de México que acepta la API (`BusinessTimeZones`).
- Accesibilidad (Fase 6): el lint corre `eslint-plugin-jsx-a11y` (recomendado). Las 208 etiquetas que no estaban ligadas a su campo tienen `htmlFor` e `id`; los títulos de grupo son texto con `role="group"`; el menú de la cuenta, cerrar sesión y mostrar la contraseña son botones con nombre, y las tarjetas y filas clicables responden al teclado (`asButton`). `no-autofocus` está apagado a propósito: los diálogos de caja enfocan su campo principal. La configuración de WhatsApp dice "ID de la cuenta de WhatsApp Business" en lugar de "WABA ID".
- Lo urgente te encuentra (Fase 6): `GET /api/v1/dashboard/attention` cuenta lo que necesita a alguien (pedidos en línea por confirmar, insumos por reabastecer, cajas abiertas desde otro día, mensajes sin enviar, conversaciones sin leer, servicios atrasados y citas de hoy sin confirmar), cada aviso solo para quien tiene el permiso y el módulo. La campana del encabezado (`AttentionBell`) y "Requiere atención" en Inicio lo leen con `useAttention` (cada minuto, y al cambiar pedidos o la caja); `utils/attention.ts` da el texto y el enlace de cada clave y deja fuera las que no conoce. El chip de caja (`CashStatusChip`, `useCurrentCashSession`) dice si la caja de la persona está abierta y cuánto debe tener; abrir, cerrar o mover dinero lo refresca (`useRefreshCashState`).
- `Topbar` muestra el selector de negocio cuando la persona trabaja en más de uno, la búsqueda global (`GlobalSearch`, Ctrl K: pantallas más pedidos, clientes y productos buscados en el servidor según el acceso) y "Nueva venta" (F2).
- `src/assets/css/theme.css` es el tema propio sobre Bootstrap 5.3 (desde la Fase 16, por capas y con el tema del negocio: ver abajo): tokens `--ta-*`, remapeo de las variables `--ct-*` de Hyper, fuente Manrope, botones táctiles (`.btn-touch`), chips de estado y estilos de impresión del ticket. Se carga después de `app.min.css`; Hyper se retira pantalla por pantalla. Desde la Fase 4F hay un solo set de íconos (Remix): se quitó Material Design Icons y lo que Hyper dibujaba con su fuente (flechas de las migas, ojo de la contraseña) lo redibuja `overrides.css`; la hoja de Hyper ya no importa Nunito desde Google Fonts (ninguna fuente sale de un servidor externo). El CSS global bajó de 965 KB a 641 KB (91 KB con gzip).
- `ToastProvider`/`useToast` y `ConfirmProvider`/`useConfirm` reemplazan `alert`, `window.confirm` y `window.prompt` (`confirm` y `prompt` devuelven promesas).
- `DataTable` busca en el servidor cuando la pantalla le pasa `serverSide.onSearchChange` (con espera de 300 ms) y acepta `initialSearch` para abrir una lista ya filtrada (`/orders?search=…`, `/customers?search=…`).
- Vender (`/sell`) concentra la venta: catálogo táctil con categorías, favoritos y lector de código, ticket con notas por artículo guardado por negocio en `localStorage` (`tienditapp.sell.draft.v1.{tenantId}`), cobro con cambio y pago mixto (`ChargePanel`) y ticket imprimible o por WhatsApp (`SaleTicket`, `utils/ticket.ts`). Sin red (Fase 3), `src/offline/` guarda en IndexedDB el último catálogo del negocio y las ventas cobradas pendientes de enviar (por negocio y cajero); `PendingSalesSync` (montado en el layout) las envía al volver la conexión con su clave de idempotencia y `PendingSalesModal` muestra las pendientes y las no aceptadas. Ver [ventas sin conexión](../domain/order-lifecycles.md#ventas-sin-conexión-fase-3).
- El arranque guiado (`/setup`) pide módulos, política de IVA y catálogo de ejemplo, y deja una lista de primeros pasos que Inicio sigue mostrando.

Datos del servidor y cliente generado (Fase 2):

- `client/openapi/openapi.json` es el documento OpenAPI de la API (rutas en minúsculas, propiedades no nulas como requeridas). `npm run api:pull` lo descarga de una API corriendo en Development y `npm run api:types` genera `src/api/generated/schema.ts` con openapi-typescript y `src/api/generated/enums.ts` con `scripts/generate-enums.mjs`; ambos archivos se versionan y se regeneran cuando cambia un contrato. CI falla si alguno no coincide con la API y reporta en cada PR los cambios incompatibles contra la rama base ([ADR 0008](../adr/0008-api-error-contract.md)).
- `src/api/typedClient.ts` (`apiRequest('get', '/api/v1/…', { path, query, body })`) tipa ruta, parámetros, cuerpo y respuesta desde ese esquema y usa la misma instancia axios (token, refresh, negocio). `Schemas['CampaignDto']` da los tipos de los DTOs.
- TanStack Query maneja el estado del servidor de las pantallas migradas: `src/api/queryClient.ts` (un reintento, 30 s de frescura) y hooks en `src/api/queries.ts` (`useMyAccess`, `useCustomerCard`, `useCampaigns`, `useCampaignMessages`, `useCreateCampaign`, `useCancelCampaign`). Las mutaciones invalidan sus claves; cerrar sesión limpia la caché.
- Migradas: ficha del cliente y campañas (lista, detalle, crear y cancelar). El resto sigue con los módulos de `src/api` y `types/index.ts`; se migran al tocar cada pantalla, sin reescrituras incidentales.
- Al cargar el perfil, `AuthContext` pide `GET /api/v1/me/access` y usa los permisos que el servidor resuelve, más frescos que los claims del token.

Fase 4:

- `AppErrorBoundary` envuelve las rutas (en el layout, alrededor del `Outlet`, con su propio `Suspense`, así el menú se queda mientras carga o si falla una pantalla) y se limpia al cambiar de ruta. Si el error es un archivo de la app que ya no existe (página abierta antes de un despliegue), recarga una vez para tomar la versión nueva (`utils/chunkReload.ts`, también ante `vite:preloadError`), a lo más una vez por minuto.
- Modo enfocado (Fase 5): Vender y Cocina corren solos en una tableta de mostrador o una pantalla de cocina. El botón "Modo enfocado" de esas pantallas quita el menú, la barra superior, el pie y la barra de pestañas, deja una barra mínima (pantalla, negocio, hora y "Salir del modo enfocado"), pide pantalla completa al navegador y sube los objetivos táctiles a 44 px (52 px en cocina). Cocina se ve en tema oscuro aunque el resto de la app sea claro (`data-bs-theme` en `.focus-shell`). El dispositivo lo recuerda por pantalla (`tienditapp.focusedAreas` en `localStorage`), así una tableta que solo vende vuelve a abrir Vender enfocado; un negocio suspendido ve su aviso normal. `components/layout/focusMode.ts` y `FocusBar` lo implementan; `Layout` elige el shell.
- `useArrivalChime` toca un aviso de dos notas con Web Audio, activable por dispositivo: Cocina suena con cada comanda nueva y Pedidos con cada pedido en línea por confirmar (`OrderCreated` lleva `status`). El navegador solo deja sonar después de tocar la pantalla; la pantalla lo pide si hace falta.
- Pedidos recarga en silencio (sin spinner ni perder el scroll) ante los eventos en vivo y al reconectar, y el detalle abierto sigue a su pedido.
- "Abrir otro negocio" (menú de usuario y selector de negocio) crea un negocio con la misma cuenta y cambia a él, que abre el arranque guiado. `PrintButton` imprime documentos de ticket (corte de caja) sin abrir otra página.

Temas y colores (Fase 16, [ADR 0010](../adr/0010-business-themes.md)):

- **El dueño elige el tema** en Configuración › Negocio › Apariencia (`ThemeCard`). Hay ocho, en `utils/businessThemes.ts`: Nopal, Talavera, Jacaranda, Rosa mexicano, Jamaica, Cempasúchil, Café de olla y Grafito.
  - Cada uno define su paleta clara y oscura: acento, acento al pasar el cursor y al presionar, texto sobre el acento, fondo y texto de lo elegido, enlaces y el tinte de la página.
  - Tocar un tema lo muestra en toda la app antes de guardarlo. Guardar llama `PUT /api/v1/auth/my-tenant/theme` y recarga el negocio; Descartar, o salir de la página, regresa al guardado.
  - El negocio solo guarda cuál (`TenantProfileDto.theme`, `PublicMenuDto.theme`).
- **`utils/theme.ts` aplica los colores:**
  - `applyBusinessTheme` pinta el tema del negocio con la sesión. Lo llama `TenantProvider` al cargar el negocio y al cerrar sesión (vuelve a Nopal). Recuerda el tema en `localStorage` (`tienditapp.businessTheme`), y `restoreBusinessTheme` lo pinta antes del primer render para que no parpadee el verde.
  - `previewBusinessTheme` pone otro encima mientras una pantalla lo necesita: el menú en línea con el tema de su negocio, aunque haya una sesión de otro, y la vista previa de Apariencia.
  - Los colores se escriben como variables `--tt-*` en `<html>`, junto con el color de la barra del navegador.
- **`theme.css` va por capas:**
  1. el tema (`--tt-*`, con Nopal por omisión);
  2. los neutros y los colores de estado, iguales en todos los temas;
  3. el mapeo a las variables y componentes de Hyper, un bloque por modo.

  El bloque claro no aplica a una página oscura (`:root:not([data-bs-theme='dark'])`), y el oscuro define todas sus variables. Antes, el fondo de la página quedaba claro en modo oscuro.
- **Lo que la Fase 16 corrigió en todos los temas:**
  - los colores de estado de Hyper, por unos AA en los dos modos;
  - el texto general y los títulos, más oscuros;
  - las versiones oscuras de alertas, chips, la conversación de WhatsApp y las fichas claras;
  - las tarjetas pintadas con el acento con texto blanco, que usan siempre el tono claro del tema;
  - el acento oscuro de Nopal, más claro para leerse como texto en botones con contorno.
- **Reglas de color:**
  - Un estado se pinta con `ta-chip-*` o con los colores de estado, nunca con el acento (`bg-primary`).
  - La acción principal de una pantalla, incluido Cobrar, es `btn-primary`, y es una sola: en la cuenta de Mesas, Agregar ronda es `btn-outline-primary`.
  - Ningún componente fija colores; usa los tokens.
- **Pruebas:**
  - `businessThemes.test.ts` revisa con `utils/contrast.ts` cada par que pinta cada tema. Lee `theme.css` para revisar los colores de estado en los dos modos, que el bloque oscuro defina lo mismo que el claro y que los valores por omisión sean los de Nopal.
  - `theme.test.ts` y `ThemeCard.test.tsx` cubren cómo se aplica, la vista previa y el guardado.
  - En `vitest.config.ts`, `css.include` deja pasar solo `theme.css`.

Pantallas de la Fase 2: Caja en pestañas (Mi caja, Turnos, Corte), Reembolsar y Anular venta en el detalle de Pedidos, Campañas (campañas, plantillas, recordatorios y bajas), ficha del cliente (chat, Clientes y Pedidos) y Nuevo pedido desde el chat, que abre Vender con el cliente y el canal WhatsApp y envía el resumen al chat al guardar.

Inventario (Fase 7D): `pages/inventory/InventoryManagement.tsx` es la pantalla y conserva el estado de las siete pestañas, así que filtros, página, búsqueda y la receta sin guardar siguen ahí al volver a una pestaña. Cada pestaña (`SuppliesTab`, `StocksTab`, `MovementsTab`, `LocationsTab`, `RecipesTab`, `KardexTab`, `AlertsTab`) y cada diálogo (`SupplyModal`, `LocationModal`, `MovementModal`, `MovementDetailModal`) viven en su archivo. `useServerList` lleva una lista paginada en el servidor, `useRecipeEditor` la receta en edición, `inventoryForms.ts` las reglas de los formularios (con pruebas) e `inventoryLabels.ts` las etiquetas de tipos, motivos y unidades. Los diálogos muestran sus errores dentro del diálogo.

## Guardrails

- Backend decide estados, totales, impuestos, saldos y existencias finales.
- Guards y capabilities del cliente solo controlan UX; backend repite autorización.
- Centraliza HTTP en `src/api/axios.ts` y módulos `src/api`; no disperses instancias Axios ni `fetch`.
- No uses `any` sin justificación localizada y documentada.
- Reutiliza componentes comunes y conserva estados loading, empty, error y success.
- No agregues una librería global de estado sin necesidad demostrada.
- No persistas access/refresh tokens en almacenamiento accesible a JavaScript.
- SignalR debe reconectar, cancelar al desmontar, aislar tenant y manejar errores.
- Cambios de contrato requieren actualizar DTO, regenerar `openapi.json` y `schema.ts`, y ajustar el tipo manual, el wrapper y los consumidores que aún no usan el cliente generado.
- Un enum del dominio sale del contrato generado (`src/api/generated/enums.ts`); nunca se copia ni se renumera en el cliente.
- Los colores salen de los tokens de `theme.css` y de la paleta del tema. Un tema nuevo entra a `utils/businessThemes.ts` con todos sus pares AA, y un color de estado no cambia con el tema ([ADR 0010](../adr/0010-business-themes.md)).

## Deuda actual

- `OrdersList.tsx`, `OrdersCart.tsx` y `types/index.ts` superan 1,000 líneas. Son candidatos a dividirse como Inventario (Fase 7D) y Vender (Fase 15), al tocar cada pantalla y sin reescrituras incidentales.
- Vender (Fase 15): `SellPage.tsx` coordina la venta y lo demás vive junto a ella en `pages/sell`: el modelo (`sellModel.ts`), el catálogo con su respaldo sin conexión (`useSellCatalog`), las reglas del ticket (`useSellCart`) y los componentes (`SellHeader`, `CatalogPanel`, `CartPanel` con `CartLines` y `SaleDetailsForm`, `OptionsModal`, `MeasureModal` y `CompletedSaleModal`). `SellPage.test.tsx` fija el flujo de la pantalla: agregar y guardar, variantes y peso, ronda a cocina y el mesero que deja el cobro a la caja.
- `AccessRoute` cubre las rutas del layout; los botones de acción dentro de cada pantalla todavía se condicionan caso por caso (por ahora, órdenes).
- Vender carga hasta 500 productos y, si el catálogo es mayor, busca en el servidor; las recetas de Inventario cargan hasta 500 productos e insumos.
- Accesibilidad: el lint exige etiquetas asociadas (Fase 6F), pero no nombres en los botones de solo icono; se les pone `aria-label` al tocar cada pantalla (Inventario ya los tiene).
- Hyper sigue cargado debajo de `theme.css`: `app.min.css` pesa 436 KB y `vendor.min.css`, 60 KB de estilos que la app no usa (select2 y otros). El CSS global pesa 652 KB, 94 KB con gzip. Retirarlo deja Bootstrap 5.3 con los tokens propios y quita los colores que Hyper compila en sus componentes, que `theme.css` cubre uno por uno.
- En el teléfono, el selector de negocio se recorta a la inicial ("C.."). "Nueva venta" aparece hasta tres veces (barra superior, encabezado e Inicio), y las migas repiten la página en las secciones de primer nivel ("Inicio › Operación diaria › Inicio").
- `client/README.md` aún es el texto genérico del template Vite.
- Los estilos minificados versionados hacen difícil rastrear y revisar cambios visuales.
