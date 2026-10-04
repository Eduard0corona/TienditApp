# Identidad, roles, permisos y capabilities

## Conceptos separados

- **Rol:** clasificación general del usuario (`SUPER_ADMIN`, `TENANT_ADMIN`, `STAFF`, `KITCHEN`, `CUSTOMER`) y rol de membresía (`Owner`, `Admin`, etc.).
- **Permiso:** acción específica del usuario dentro del tenant, almacenada como códigos en `TenantUser.PermissionCodes`. El catálogo actual incluye catálogo, órdenes, tablero, inventario, caja, clientes, reportes, WhatsApp, agente, empleados y citas.
- **Capability:** módulo habilitado al tenant (`Retail`, `Service`, `Food`, `Inventory`, `Appointments`, `Kitchen`).

Un capability no concede permiso y un permiso no habilita un capability. La autorización efectiva requiere identidad válida, acceso activo al tenant, capability del módulo y permiso/rol adecuados.

## Estado actual

ASP.NET Identity autentica y el JWT incluye contexto del tenant. `TenantAccessService` resuelve membresía, capabilities normalizadas y permisos; si no hay permisos explícitos usa defaults por rol. `TenantResolutionMiddleware` aplica permisos/capabilities por prefijos de ruta y los controllers además usan `[Authorize]` y, con frecuencia, `[Authorize(Roles=...)]`. Los hubs repiten la validación de membresía y permiso al conectar. El frontend usa `ProtectedRoute` y `AccessRoute` para UX.

### Matriz alineada (Fase 0)

El rol de un controller no debe contradecir el permiso que exige el middleware: si el menú ofrece una pantalla por permiso, el endpoint acepta ese rol. `ControllerRoleAuthorizationTests` fija estos casos.

| Área | Roles del controller | Permiso (middleware) | Capability |
| --- | --- | --- | --- |
| Dashboard y reportes | `SUPER_ADMIN`, `TENANT_ADMIN`, `STAFF` | `ViewReports` | comercio |
| Cocina | `SUPER_ADMIN`, `TENANT_ADMIN`, `STAFF`, `KITCHEN` | `ManageServiceBoard` | `Kitchen` |
| Ver órdenes | `SUPER_ADMIN`, `TENANT_ADMIN`, `STAFF`, `KITCHEN` | `ManageOrders` | comercio |
| Crear, cobrar, cambiar estado y cancelar órdenes | `SUPER_ADMIN`, `TENANT_ADMIN`, `STAFF` | `ManageOrders` | comercio |
| Chat de WhatsApp | `SUPER_ADMIN`, `TENANT_ADMIN`, `STAFF` | `ManageWhatsApp` | comercio |

En el cliente, `src/utils/access.ts` (`getAccess`) traduce la misma matriz para el menú, las rutas (`AccessRoute`), la página de entrada (`getLandingPath`: administradores al Dashboard, el resto a la pantalla de su trabajo: Vender, Cocina o Taller) y los botones de acción. Es ayuda de navegación: la API sigue decidiendo.

Cambiar de tenant rota el refresh token de la sesión (el anterior se revoca).

## Guardrails

- Los guards de React y menús ocultos no son seguridad.
- Cada operación sensible se valida en backend, preferentemente cerca del caso de uso además del middleware.
- Revisa acceso al objeto, no solo acceso al endpoint.
- `SUPER_ADMIN` requiere control cross-tenant explícito; nunca se interpreta como permiso universal implícito.
- Los códigos desconocidos se rechazan/normalizan mediante `TenantPermissionCatalog`.
- Cambios de rol, permiso o capability se auditan y deben invalidar o limitar sesiones/tokens según riesgo.
- Rutas nuevas deben declarar requisitos de permiso/capability sin depender de listas frágiles por string.

## Deuda actual

- Muchos controllers dependen de roles aunque ya existe un modelo granular; el middleware mitiga parte, pero la política está dispersa.
- `TENANT_ADMIN` y `SUPER_ADMIN` omiten comprobaciones granulares en el middleware. El claim `role` del JWT es el rol global del usuario, no el de su membresía: un `TENANT_ADMIN` obtendría todos los permisos en cualquier tenant al que se le agregue como empleado, y `UpdateEmployeeCommand` sobrescribe ese rol global. Hoy es latente porque ningún flujo agrega un usuario existente a un segundo tenant.
- El mapeo por prefijo y método no expresa recursos ni condiciones de negocio.
- Los access tokens no se pueden revocar antes de expirar (no se valida el security stamp) y los refresh tokens se guardan en texto plano, sin detección de reuso.
- No hay bloqueo de cuenta por intentos fallidos y el login distingue cuentas sin confirmar o bloqueadas; se atiende junto con la corrección del 2FA, en una tarea separada.
- No se encontró cobertura API end-to-end para la matriz rol/permiso/capability/tenant.
