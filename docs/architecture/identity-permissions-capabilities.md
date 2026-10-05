# Identidad, roles, permisos y capabilities

## Conceptos separados

- **Rol:** clasificación general del usuario (`SUPER_ADMIN`, `TENANT_ADMIN`, `STAFF`, `KITCHEN`, `CUSTOMER`) y rol de membresía (`Owner`, `Admin`, etc.).
- **Permiso:** acción específica del usuario dentro del tenant, almacenada como códigos en `TenantUser.PermissionCodes`. El catálogo actual incluye catálogo, tomar pedidos (`ManageOrders`), cobrar (`ChargeOrders`, Fase 4), tablero, inventario, caja, clientes, reportes, WhatsApp, agente, empleados, citas, reembolsos (`ProcessRefunds`) y campañas (`ManageCampaigns`). Los dos últimos no están en los permisos por defecto del personal: el dueño los otorga. `NULL` en `PermissionCodes` significa "los del rol"; el valor `None` es "sin acceso" elegido a propósito.
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
| Crear, cambiar estado y cancelar órdenes | `SUPER_ADMIN`, `TENANT_ADMIN`, `STAFF` | `ManageOrders` | comercio |
| Cobrar (pagos, comprobantes, venta cobrada) | `SUPER_ADMIN`, `TENANT_ADMIN`, `STAFF` | `ChargeOrders` | comercio |
| Categorías y borrar productos | `SUPER_ADMIN`, `TENANT_ADMIN`, `STAFF` | `ManageCatalog` | comercio |
| Respuestas automáticas | `SUPER_ADMIN`, `TENANT_ADMIN`, `STAFF` | `ManageAiAgent` | comercio |
| Equipo | `SUPER_ADMIN`, `TENANT_ADMIN` | `ManageEmployees` (solo administradores) | comercio |
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
- Equipo (`UpdateEmployeeCommand`, `SetEmployeeActiveCommand`, `EmployeeAccessRules`): el dueño no se puede desactivar ni cambiar de rol (su nombre sí); nadie se quita a sí mismo el rol de administrador; un negocio conserva al menos un dueño o admin activo, contado por membresía. El rol de la cuenta solo cambia si la persona no pertenece a otro negocio: de lo contrario solo cambia su membresía en este.
- `POST /api/v1/me/tenants` (`CreateBusinessCommand`, Fase 4) abre otro negocio con la cuenta que lo pide, como dueño, con lo mismo que un negocio del registro (`BusinessProvisioning`: sucursal y almacén predeterminados, público en general, IVA incluido y la prueba del plan para negocios nuevos). Una cuenta de cliente o el administrador de la plataforma no abren negocios y una cuenta es dueña de hasta 10.
- La migración `AlignTenantAdminMemberships` marca como `Admin` las membresías `Staff` de cuentas `TENANT_ADMIN` que solo pertenecen a un negocio, para que nadie que hoy administra su negocio pierda el acceso con la regla nueva. Es irreversible a propósito: su `Down` no degrada a nadie.

## Equipo, cobro y acceso (Fase 4E)

- **Invitaciones en lugar de contraseñas inventadas.** `CreateEmployeeCommand` crea la cuenta sin contraseña y una membresía `Invited`; la respuesta trae `InvitationUrl` (`/aceptar-invitacion?userId=…&token=…`) para copiarla o mandarla por WhatsApp, y el mismo enlace sale por correo cuando está configurado. El token es de un proveedor propio de Identity (`InvitationTokenProvider`, 7 días): no sirve para restablecer una contraseña, y elegir la contraseña cambia el security stamp, así que el enlace sirve una vez. `GET /auth/invitation` muestra quién invitó y el correo con que se entrará; `POST /auth/accept-invitation` fija la contraseña, confirma el correo, activa todas las invitaciones pendientes de la cuenta e inicia sesión. Una cuenta que ya existe se une con su contraseña sin cambios; una cuenta de cliente no se agrega al equipo.
- **Asientos del plan.** Una invitación pendiente ocupa su lugar (`SubscriptionRules.CheckUserLimitAsync` cuenta activos e invitados). Cancelar una invitación es desactivar la membresía; reactivar a alguien que nunca eligió contraseña le devuelve la invitación (`Invited`), no un acceso que no puede usar. `POST /employees/{id}/resend-invitation` da un enlace nuevo mientras espera.
- **Roles con nombre.** `GET /employees/role-presets`: Gerente (administra), Cajero, Mesero, Cocina y Técnico. Elegir uno llena los permisos; cambiar cualquiera lo vuelve "Personalizado" (cuenta `STAFF`). Lo guardado son siempre los permisos.
- **Cobrar es su propio permiso.** `AddOrderPaymentCommand` y el comprobante de pago exigen `ChargeOrders`; `CreateOrderCommand` lo exige solo si trae pagos (`IRequiresContentPermissions`, que `AuthorizationBehavior` revisa después de la regla del caso de uso). El middleware exige `ChargeOrders` en `POST /orders/{id}/payments` y en el comprobante. Un mesero guarda el pedido y la caja lo cobra; la app oculta "Cobrar" sin el permiso. La migración `AddChargePermissionAndSmsCooldown` agrega `ChargeOrders` a quien tenía `ManageOrders` guardado (salvo cuentas de cocina), así nadie pierde lo que hoy hace.
- **"Sin acceso" explícito.** En crear y editar, `Permissions = null` toma los del rol y una lista vacía guarda `None` (sin acceso). `ManageEmployees` ya no se ofrece al personal: quien administra permisos podría darse todos.
- **El teléfono es de la persona.** Recupera la contraseña por SMS, así que el admin ya no lo cambia (`UpdateEmployeeCommand` no tiene teléfono) y el perfil tampoco lo cambia directo: `POST /auth/profile/phone` pide la contraseña y manda un código al número nuevo; `POST /auth/profile/phone/confirm` lo confirma (y mueve el perfil de cliente al número nuevo). La confirmación anónima de `/confirm-phone` solo vale para una cuenta que se está registrando (con contraseña y sin nada confirmado); ahí mismo `POST /auth/change-unconfirmed-phone` corrige un número mal escrito con la contraseña. El nombre de una cuenta que usa otro negocio solo lo cambia la persona.
- **Recuperar por SMS.** `POST /auth/forgot-password/sms` (correo de la cuenta) manda un código al teléfono confirmado y responde igual exista o no; `POST /auth/reset-password/sms` cambia la contraseña con el código y cierra las sesiones abiertas.
- **Límites por cuenta.** Un código SMS por minuto por cuenta (`ApplicationUser.SmsCodeSentAt`, `AccountSecurity.TryReserveSmsCodeAsync`). Contraseñas y códigos equivocados cuentan para el bloqueo de Identity (5 intentos, 15 minutos) en el login, el código SMS, el cambio de teléfono y el cambio de número al registrarse; recuperar la contraseña levanta el bloqueo. El cliente reintenta con espera la renovación de la sesión si recibe 429 o 503.
- Los mensajes de Identity (contraseñas, correo duplicado, enlaces vencidos) salen en español (`SpanishIdentityErrorDescriber`).

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
- El login todavía distingue cuentas sin confirmar o bloqueadas (el bloqueo por intentos ya existe desde la Fase 4E).
- La lista del equipo (`GET /employees`) exige `ManageEmployees`: la agenda la usa para elegir quién atiende y el personal con `ManageAppointments` no la ve.
- No se encontró cobertura API end-to-end para la matriz rol/permiso/capability/tenant.
