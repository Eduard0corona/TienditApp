# Arquitectura frontend

## Estado actual comprobado

`client` usa React 19.2, TypeScript 5.9, Vite 7, React Router 7, Axios, React Hook Form, Bootstrap/React Bootstrap y SignalR. `App.tsx` define rutas con `lazy` y `Suspense`; `ProtectedRoute` comprueba sesión y `AccessRoute` decide si la pantalla aplica al usuario con la matriz de `src/utils/access.ts` (rol, permiso y capability, igual que la API).

Estructura principal:

- `src/api`: una instancia Axios y módulos por recurso.
- `src/pages`: pantallas por capacidad funcional.
- `src/components/common` y `layout`: reutilización y shell.
- `src/hooks`: Auth/Tenant y conexiones SignalR.
- `src/contexts`: estado de autenticación y tenant.
- `src/types/index.ts`: contratos TypeScript compartidos. Los enums del dominio se copian como objetos `as const` con los mismos números, porque la API los intercambia como enteros; `ClientEnumContractTests` (WebApi.Tests) los compara con el dominio.
- `src/utils/tenantCapabilities.ts`: helpers de capabilities.

El access token se mantiene en memoria y se agrega con interceptor. El refresh token usa cookie `HttpOnly`; `localStorage` solo conserva un marcador de sesión. Vite proxya API, uploads y hubs a `localhost:5203`.

Sesión y tiempo real:

- `refreshAccessToken` (en `src/api/axios.ts`) es single-flight y se serializa entre pestañas con la Web Locks API, porque el refresh token rota en cada uso.
- Un refresh rechazado (400, 401 o 403) emite `expired` y la sesión se cierra; un 429, un error del servidor o uno de red la conservan y la siguiente llamada reintenta. Si un refresh devuelve otro tenant (otra pestaña cambió de tenant), la pestaña recarga.
- `useSessionSync` propaga logout y cambio de tenant a las demás pestañas con `BroadcastChannel`; cambiar de tenant recarga la aplicación.
- `src/api/signalr.ts` crea las conexiones: reintenta indefinidamente, reintenta el primer arranque, reinicia tras un cierre del servidor y pide un token vigente en cada (re)conexión.
- `src/utils/labels.ts` concentra las etiquetas en español de estados, métodos de pago, tipos y roles (acepta el número de REST o el nombre del enum de SignalR); `src/api/errors.ts` (`getApiErrorMessage`, `getApiFieldErrors`) es la única forma de leer errores de la API.
- `HomeRedirect`/`AccessRoute` llevan a cada usuario a la pantalla de su trabajo (`getLandingPath`); un administrador cuyo negocio no terminó el arranque guiado va a `/setup`. `isSafeFileUrl` solo deja enlazar archivos de `/uploads` o https; `estimateOrderTotals` replica el desglose de IVA del backend en centavos enteros (el total definitivo es el de la API).

Shell y diseño (Fase 1):

- `src/components/layout/navigation.ts` define el menú por trabajo (Operación diaria, Gestión, Configuración) a partir de la matriz de acceso; `LeftSidebar`, la búsqueda global y la barra inferior del celular (`BottomTabBar`) lo leen de ahí.
- `Topbar` muestra el selector de negocio cuando la persona trabaja en más de uno, la búsqueda global (`GlobalSearch`, Ctrl K: pantallas más pedidos, clientes y productos buscados en el servidor según el acceso) y "Nueva venta" (F2).
- `src/assets/css/theme.css` es el tema propio sobre Bootstrap 5.3: tokens `--ta-*`, remapeo de las variables `--ct-*` de Hyper, fuente Manrope, botones táctiles (`.btn-touch`), chips de estado y estilos de impresión del ticket. Se carga después de `app.min.css`; Hyper se retira pantalla por pantalla.
- `ToastProvider`/`useToast` y `ConfirmProvider`/`useConfirm` reemplazan `alert`, `window.confirm` y `window.prompt` (`confirm` y `prompt` devuelven promesas).
- `DataTable` busca en el servidor cuando la pantalla le pasa `serverSide.onSearchChange` (con espera de 300 ms) y acepta `initialSearch` para abrir una lista ya filtrada (`/orders?search=…`, `/customers?search=…`).
- Vender (`/sell`) concentra la venta: catálogo táctil con categorías, favoritos y lector de código, ticket con notas por artículo guardado por negocio en `localStorage` (`tienditapp.sell.draft.v1.{tenantId}`), cobro con cambio y pago mixto (`ChargePanel`) y ticket imprimible o por WhatsApp (`SaleTicket`, `utils/ticket.ts`). Sin red (Fase 3), `src/offline/` guarda en IndexedDB el último catálogo del negocio y las ventas cobradas pendientes de enviar (por negocio y cajero); `PendingSalesSync` (montado en el layout) las envía al volver la conexión con su clave de idempotencia y `PendingSalesModal` muestra las pendientes y las no aceptadas. Ver [ventas sin conexión](../domain/order-lifecycles.md#ventas-sin-conexión-fase-3).
- El arranque guiado (`/setup`) pide módulos, política de IVA y catálogo de ejemplo, y deja una lista de primeros pasos que Inicio sigue mostrando.

Datos del servidor y cliente generado (Fase 2):

- `client/openapi/openapi.json` es el documento OpenAPI de la API (rutas en minúsculas, propiedades no nulas como requeridas). `npm run api:pull` lo descarga de una API corriendo en Development y `npm run api:types` genera `src/api/generated/schema.ts` con openapi-typescript; ambos archivos se versionan y se regeneran cuando cambia un contrato.
- `src/api/typedClient.ts` (`apiRequest('get', '/api/v1/…', { path, query, body })`) tipa ruta, parámetros, cuerpo y respuesta desde ese esquema y usa la misma instancia axios (token, refresh, negocio). `Schemas['CampaignDto']` da los tipos de los DTOs.
- TanStack Query maneja el estado del servidor de las pantallas migradas: `src/api/queryClient.ts` (un reintento, 30 s de frescura) y hooks en `src/api/queries.ts` (`useMyAccess`, `useCustomerCard`, `useCampaigns`, `useCampaignMessages`, `useCreateCampaign`, `useCancelCampaign`). Las mutaciones invalidan sus claves; cerrar sesión limpia la caché.
- Migradas: ficha del cliente y campañas (lista, detalle, crear y cancelar). El resto sigue con los módulos de `src/api` y `types/index.ts`; se migran al tocar cada pantalla, sin reescrituras incidentales.
- Al cargar el perfil, `AuthContext` pide `GET /api/v1/me/access` y usa los permisos que el servidor resuelve, más frescos que los claims del token.

Pantallas de la Fase 2: Caja en pestañas (Mi caja, Turnos, Corte), Reembolsar y Anular venta en el detalle de Pedidos, Campañas (campañas, plantillas, recordatorios y bajas), ficha del cliente (chat, Clientes y Pedidos) y Nuevo pedido desde el chat, que abre Vender con el cliente y el canal WhatsApp y envía el resumen al chat al guardar.

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
- Un enum del dominio se copia con sus valores exactos; nunca se renumera en el cliente.

## Deuda actual

- `InventoryManagement.tsx` supera 2,400 líneas; `OrdersCart.tsx`, `OrdersList.tsx` y `types/index.ts` rondan o superan 1,000. Son candidatos a refactor por feature/componente, no deben reescribirse incidentalmente.
- No se encontró framework ni suite de pruebas frontend.
- `AccessRoute` cubre las rutas del layout; los botones de acción dentro de cada pantalla todavía se condicionan caso por caso (por ahora, órdenes).
- Inventario, Caja, Compras y Auditoría todavía filtran solo la página cargada en sus tablas; Vender carga hasta 500 productos y, si el catálogo es mayor, busca en el servidor.
- Accesibilidad: muchas etiquetas no están asociadas a su campo y hay botones de solo icono sin nombre accesible.
- `client/README.md` aún es el texto genérico del template Vite.
- Los estilos minificados versionados hacen difícil rastrear y revisar cambios visuales.
