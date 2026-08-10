# Arquitectura frontend

## Estado actual comprobado

`client` usa React 19.2, TypeScript 5.9, Vite 7, React Router 7, Axios, React Hook Form, Bootstrap/React Bootstrap y SignalR. `App.tsx` define rutas con `lazy` y `Suspense`; `ProtectedRoute` comprueba sesión y `CapabilityRoute` decide visibilidad por capabilities.

Estructura principal:

- `src/api`: una instancia Axios y módulos por recurso.
- `src/pages`: pantallas por capacidad funcional.
- `src/components/common` y `layout`: reutilización y shell.
- `src/hooks`: Auth/Tenant y conexiones SignalR.
- `src/contexts`: estado de autenticación y tenant.
- `src/types/index.ts`: contratos TypeScript compartidos.
- `src/utils/tenantCapabilities.ts`: helpers de capabilities.

El access token se mantiene en memoria y se agrega con interceptor. El refresh token usa cookie `HttpOnly`; `localStorage` solo conserva un marcador de sesión. Vite proxya API, uploads y hubs a `localhost:5203`.

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

## Deuda actual

- `InventoryManagement.tsx` supera 2,400 líneas; `OrdersCart.tsx`, `OrdersList.tsx` y `types/index.ts` rondan o superan 1,000. Son candidatos a refactor por feature/componente, no deben reescribirse incidentalmente.
- No se encontró framework ni suite de pruebas frontend.
- Capabilities se aplican a muchas rutas, pero no se observa un guard frontend equivalente para permisos granulares.
- `client/README.md` aún es el texto genérico del template Vite.
- Los estilos minificados versionados hacen difícil rastrear y revisar cambios visuales.
