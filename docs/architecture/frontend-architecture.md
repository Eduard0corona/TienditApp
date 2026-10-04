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
- `HomeRedirect`/`AccessRoute` llevan a cada usuario a la pantalla de su trabajo (`getLandingPath`); `isSafeFileUrl` solo deja enlazar archivos de `/uploads` o https; `estimateOrderTotals` replica la regla de impuesto del backend para el carrito (el total definitivo es el de la API).

## Guardrails

- Backend decide estados, totales, impuestos, saldos y existencias finales.
- Guards y capabilities del cliente solo controlan UX; backend repite autorización.
- Centraliza HTTP en `src/api/axios.ts` y módulos `src/api`; no disperses instancias Axios ni `fetch`.
- No uses `any` sin justificación localizada y documentada.
- Reutiliza componentes comunes y conserva estados loading, empty, error y success.
- No agregues una librería global de estado sin necesidad demostrada.
- No persistas access/refresh tokens en almacenamiento accesible a JavaScript.
- SignalR debe reconectar, cancelar al desmontar, aislar tenant y manejar errores.
- Cambios de contrato requieren actualizar DTO, tipo TypeScript, API wrapper y consumidores.
- Un enum del dominio se copia con sus valores exactos; nunca se renumera en el cliente.

## Deuda actual

- `InventoryManagement.tsx` supera 2,400 líneas; `OrdersCart.tsx`, `OrdersList.tsx` y `types/index.ts` rondan o superan 1,000. Son candidatos a refactor por feature/componente, no deben reescribirse incidentalmente.
- No se encontró framework ni suite de pruebas frontend.
- `AccessRoute` cubre las rutas del layout; los botones de acción dentro de cada pantalla todavía se condicionan caso por caso (por ahora, órdenes).
- La búsqueda de `DataTable` filtra únicamente la página cargada, y el POS carga como máximo 200 productos, servicios y clientes (ahora lo avisa).
- Accesibilidad: muchas etiquetas no están asociadas a su campo y hay botones de solo icono sin nombre accesible.
- `client/README.md` aún es el texto genérico del template Vite.
- Los estilos minificados versionados hacen difícil rastrear y revisar cambios visuales.
