# Identidad, roles, permisos y capabilities

## Conceptos separados

- **Rol:** clasificación general del usuario (`SUPER_ADMIN`, `TENANT_ADMIN`, `STAFF`, `KITCHEN`, `CUSTOMER`) y rol de membresía (`Owner`, `Admin`, etc.).
- **Permiso:** acción específica del usuario dentro del tenant, almacenada como códigos en `TenantUser.PermissionCodes`. El catálogo actual incluye catálogo, órdenes, tablero, inventario, caja, clientes, reportes, WhatsApp, agente, empleados, citas, reembolsos (`ProcessRefunds`) y campañas (`ManageCampaigns`). Los dos últimos no están en los permisos por defecto del personal: el dueño los otorga.
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

La verificación de teléfono identifica la cuenta por el `userId` que devuelve el registro: viaja en la URL de `/confirm-phone` (recargar ya no pierde el estado) y `POST /auth/resend-phone-confirmation` acepta `{ userId }` para reenviar el código (o un teléfono E.164), sin revelar si la cuenta existe y con el límite `auth`. El enlace del correo de confirmación abre la página `/confirm-email` de la app, que confirma contra `GET /auth/confirm-email` y, si el enlace venció o está incompleto, ofrece pedir uno nuevo.

## Autorización v2 (Fase 2)

La regla vive en cada caso de uso: todo `Command`/`Query` declara exactamente uno de estos atributos (`Application/Common/Security`) y `AuthorizationBehavior` lo valida antes de la validación y del handler.

| Atributo | Quién pasa |
| --- | --- |
| `[RequiresPermission(A, B…)]` | Miembros activos del negocio con cualquiera de los permisos; dueños, admins y el administrador de plataforma tienen todos. |
| `[RequiresTenantManager]` | Dueños y admins del negocio (configuración), o el administrador de plataforma. |
| `[RequiresTenantMember]` | Cualquier miembro activo del negocio. |
| `[RequiresAuthentication]` | Cualquier usuario con sesión (perfil, 2FA, cambiar de negocio). |
| `[RequiresPlatformAdmin]` | Solo `SUPER_ADMIN`. |
| `[AllowAnonymousRequest("motivo")]` | Flujos públicos (inicio de sesión, registro, confirmaciones) y lo que envía el sistema sin usuario (webhook). |

- `[RequiresCapability(X)]` se combina con la regla anterior y aplica a todos, también al administrador de plataforma: un módulo apagado está apagado.
- **Deny by default:** una solicitud sin regla (o con más de una) no se ejecuta. `AuthorizationBehaviorTests` exige una regla en cada caso de uso (147) y fija la lista de anónimos; un caso de uso nuevo sin regla rompe CI.
- `CurrentAccessContext` lee una vez por petición la membresía, permisos y módulos; `TenantResolutionMiddleware` le pasa lo que ya consultó.
- `ForbiddenAccessException` responde 403 con un mensaje en español.
- `GET /api/v1/me/access` devuelve rol, permisos efectivos y módulos según el servidor. El cliente lo usa al cargar el perfil, así un cambio de permisos aplica sin volver a iniciar sesión (el token anterior conserva sus claims hasta expirar, pero la API ya decide con los permisos actuales).
- El mapeo por prefijo de ruta del middleware se conserva como defensa en profundidad; se retirará cuando la API tenga pruebas end-to-end de la matriz.

## Guardrails

- Los guards de React y menús ocultos no son seguridad.
- Cada operación sensible se valida en backend, preferentemente cerca del caso de uso además del middleware.
- Revisa acceso al objeto, no solo acceso al endpoint.
- `SUPER_ADMIN` requiere control cross-tenant explícito; nunca se interpreta como permiso universal implícito.
- Los códigos desconocidos se rechazan/normalizan mediante `TenantPermissionCatalog`.
- Cambios de rol, permiso o capability se auditan y deben invalidar o limitar sesiones/tokens según riesgo.
- Casos de uso nuevos declaran su regla con los atributos de autorización v2; el middleware no es la fuente de verdad.

## Deuda actual

- Los controllers conservan `[Authorize(Roles=...)]` además de la regla del caso de uso; la regla v2 es la autoridad y los roles de controller deben retirarse con pruebas de API.
- `TENANT_ADMIN` y `SUPER_ADMIN` omiten comprobaciones granulares en el middleware. El claim `role` del JWT es el rol global del usuario, no el de su membresía: un `TENANT_ADMIN` obtendría todos los permisos en cualquier tenant al que se le agregue como empleado, y `UpdateEmployeeCommand` sobrescribe ese rol global. Hoy es latente porque ningún flujo agrega un usuario existente a un segundo tenant.
- La autorización por recurso (por ejemplo, cerrar solo la caja propia) sigue en los handlers.
- Los access tokens no se pueden revocar antes de expirar (no se valida el security stamp) y los refresh tokens se guardan en texto plano, sin detección de reuso.
- No hay bloqueo de cuenta por intentos fallidos y el login distingue cuentas sin confirmar o bloqueadas; se atiende junto con la corrección del 2FA, en una tarea separada.
- No se encontró cobertura API end-to-end para la matriz rol/permiso/capability/tenant.
