# Identidad, roles, permisos y capabilities

## Conceptos separados

- **Rol:** clasificación general del usuario (`SUPER_ADMIN`, `TENANT_ADMIN`, `STAFF`, `KITCHEN`, `CUSTOMER`) y rol de membresía (`Owner`, `Admin`, etc.).
- **Permiso:** acción específica del usuario dentro del tenant, almacenada como códigos en `TenantUser.PermissionCodes`. El catálogo actual incluye catálogo, órdenes, tablero, inventario, caja, clientes, reportes, WhatsApp, agente, empleados, citas, reembolsos (`ProcessRefunds`) y campañas (`ManageCampaigns`). Los dos últimos no están en los permisos por defecto del personal: el dueño los otorga.
- **Capability:** módulo habilitado al tenant (`Retail`, `Service`, `Food`, `Inventory`, `Appointments`, `Kitchen`).

Desde la Fase 3 el plan del negocio limita qué capabilities puede encender el dueño (un negocio sin plan no tiene límites; ver [planes y facturación](../domain/billing-and-platform.md)). Un capability no concede permiso y un permiso no habilita un capability. La autorización efectiva requiere identidad válida, acceso activo al tenant, capability del módulo y permiso/rol adecuados.

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

## Rol efectivo por negocio (Fase 4)

Una persona puede ser dueña de un negocio y empleada en otro, con una sola cuenta. El rol que cuenta dentro de un negocio sale de su membresía (`TenantUser.Role`), no del rol de la cuenta (`ApplicationUser.Role`):

| Cuenta | Membresía | Rol efectivo en ese negocio |
| --- | --- | --- |
| `SUPER_ADMIN` | cualquiera | `SUPER_ADMIN` |
| cualquier empleado | `Owner` o `Admin` | `TENANT_ADMIN` |
| `KITCHEN` | `Staff` | `KITCHEN` |
| `TENANT_ADMIN` o `STAFF` | `Staff` | `STAFF` |

- `TenantPermissionCatalog.EffectiveRole` fija la tabla; los permisos por defecto (`GetDefaultCodes`) la usan.
- Con un negocio seleccionado, el claim `role` del JWT es el rol efectivo (`tenant_role` sigue llevando la membresía). Sin negocio, es el de la cuenta.
- "Administra el negocio" (`CurrentAccessContext.IsTenantManagerAsync`, `[RequiresTenantManager]`, todos los permisos en `[RequiresPermission]` y el salto de permisos del middleware) se decide con la membresía leída en cada petición (`TenantAccessSnapshot.IsManager`), no con el claim: un token emitido antes de un cambio de rol no conserva el acceso de administrador.
- `GET /auth/profile` devuelve el rol del token y `GET /api/v1/me/access` el rol efectivo actual (`TenantAccessSnapshot.EffectiveRole`); la app usa el segundo, así el menú y la página de entrada coinciden con lo que decide la API aunque el token sea anterior a un cambio de rol. Al iniciar sesión la app abre la página de entrada del rol (Inicio para administradores; Vender, Cocina o Taller para el resto) en lugar de una ruta fija.
- Equipo (`UpdateEmployeeCommand`, `SetEmployeeActiveCommand`, `EmployeeAccessRules`): el dueño no se puede desactivar ni cambiar de rol (su nombre y teléfono sí); nadie se quita a sí mismo el rol de administrador; un negocio conserva al menos un dueño o admin activo, contado por membresía. El rol de la cuenta solo cambia si la persona no pertenece a otro negocio: de lo contrario solo cambia su membresía en este.
- `POST /api/v1/me/tenants` (`CreateBusinessCommand`, Fase 4) abre otro negocio con la cuenta que lo pide, como dueño, con lo mismo que un negocio del registro (`BusinessProvisioning`: sucursal y almacén predeterminados, público en general, IVA incluido y la prueba del plan para negocios nuevos). Una cuenta de cliente o el administrador de la plataforma no abren negocios y una cuenta es dueña de hasta 10.
- La migración `AlignTenantAdminMemberships` marca como `Admin` las membresías `Staff` de cuentas `TENANT_ADMIN` que solo pertenecen a un negocio, para que nadie que hoy administra su negocio pierda el acceso con la regla nueva. Es irreversible a propósito: su `Down` no degrada a nadie.

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
- `SUPER_ADMIN` y quien administra el negocio por membresía omiten las comprobaciones granulares del middleware (todos sus permisos son implícitos). Algunos handlers además comparan el texto del claim `role` (`TENANT_ADMIN`); desde la Fase 4 es el rol efectivo del negocio, pero esas comparaciones deben retirarse a favor de la regla v2.
- La autorización por recurso (por ejemplo, cerrar solo la caja propia) sigue en los handlers.
- Los access tokens no se pueden revocar antes de expirar (no se valida el security stamp) y los refresh tokens se guardan en texto plano, sin detección de reuso.
- No hay bloqueo de cuenta por intentos fallidos y el login distingue cuentas sin confirmar o bloqueadas; se atiende junto con la corrección del 2FA, en una tarea separada.
- No se encontró cobertura API end-to-end para la matriz rol/permiso/capability/tenant.
