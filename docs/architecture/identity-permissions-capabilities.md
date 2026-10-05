# Identidad, roles, permisos y capabilities

## Conceptos separados

- **Rol:** clasificación general del usuario (`SUPER_ADMIN`, `TENANT_ADMIN`, `STAFF`, `KITCHEN`, `CUSTOMER`) y rol de membresía (`Owner`, `Admin`, etc.).
- **Permiso:** acción específica del usuario dentro del tenant, almacenada como códigos en `TenantUser.PermissionCodes`. El catálogo actual incluye catálogo, tomar pedidos (`ManageOrders`), cobrar (`ChargeOrders`, Fase 4), tablero, inventario, caja, clientes, reportes, WhatsApp, agente, empleados, citas, reembolsos (`ProcessRefunds`) y campañas (`ManageCampaigns`). Los dos últimos no están en los permisos por defecto del personal: el dueño los otorga. `NULL` en `PermissionCodes` significa "los del rol"; el valor `None` es "sin acceso" elegido a propósito.
- **Capability:** módulo habilitado al tenant (`Retail`, `Service`, `Food`, `Inventory`, `Appointments`, `Kitchen`).

Desde la Fase 3 el plan del negocio limita qué capabilities puede encender el dueño (un negocio sin plan no tiene límites; ver [planes y facturación](../domain/billing-and-platform.md)). Un capability no concede permiso y un permiso no habilita un capability. La autorización efectiva requiere identidad válida, acceso activo al tenant, capability del módulo y permiso/rol adecuados.

## Estado actual

ASP.NET Identity autentica y el JWT incluye contexto del tenant. `TenantAccessService` resuelve membresía, capabilities normalizadas y permisos; si no hay permisos explícitos usa defaults por rol. Desde la Fase 5 la autorización vive en un solo lugar: cada caso de uso declara quién puede enviarlo (permiso, administrador del negocio, miembro…) y qué módulo necesita, y `AuthorizationBehavior` lo aplica. `TenantResolutionMiddleware` solo resuelve el negocio del token, valida la membresía activa y la suspensión; los controllers piden sesión (`[Authorize]`) o son públicos (`[AllowAnonymous]`), sin roles. Los hubs validan membresía y permiso al conectar, con "administra el negocio" leído de la membresía. El frontend usa `ProtectedRoute` y `AccessRoute` para UX.

### Matriz por caso de uso (Fase 5)

`UseCaseRulesTests` fija la regla de cada trabajo; los permisos y los módulos se prueban por separado.

| Área | Regla del caso de uso | Módulo |
| --- | --- | --- |
| Dashboard y reportes | `ViewReports` | — |
| Tablero de cocina (ver, iniciar, marcar listo) | `ManageServiceBoard` | `Kitchen` |
| Ver, crear, cambiar estado y cancelar órdenes | `ManageOrders` | — |
| Mesas y cuentas abiertas; llevar una ronda a la mesa | `ManageOrders` | `Food` |
| Cobrar (pagos, comprobantes, venta cobrada) | `ChargeOrders` | — |
| Reembolsar y anular | `ManageOrders` + `ProcessRefunds` | — |
| Categorías y productos | `ManageCatalog` (leer también `ManageOrders`, `ManageInventory` o `ManageAppointments`) | — |
| Inventario y compras | `ManageInventory` | `Inventory` |
| Citas | `ManageAppointments` | `Appointments` |
| Respuestas automáticas | `ManageAiAgent` | — |
| Chat de WhatsApp | `ManageWhatsApp` | — |
| Campañas, plantillas, bajas y promoción rápida | `ManageCampaigns` | — |
| Equipo, actividad del negocio y configuración | dueño o admin | — |

Un negocio siempre tiene un tipo de venta (productos, servicios o alimentos), como su plan: `TenantCapabilityRules.NormalizeCapabilities` completa el de su giro si su configuración no deja ninguno y `UpdateMyTenantCommand` no guarda módulos sin uno. Por eso ningún caso de uso exige "comercio".

En el cliente, `src/utils/access.ts` (`getAccess`) traduce la misma matriz (permiso, módulo y "administra") para el menú, las rutas (`AccessRoute`), la página de entrada (`getLandingPath`: administradores al Dashboard, el resto a la pantalla de su trabajo: Vender, Cocina o Taller) y los botones de acción. Es ayuda de navegación: la API sigue decidiendo.

Cambiar de tenant rota el refresh token de la sesión (el anterior se revoca) sin cambiar de sesión.

Sesiones (Fase 8, `UserSessions` y `AccessTokenSessionValidator`):
- Cada inicio de sesión (login, 2FA, registro, invitación aceptada) abre una sesión, cuyo id viaja en el access token (`session_id`) junto con una huella del security stamp.
- Renovar reemplaza el refresh token dentro de la misma sesión.
- Un token ya reemplazado que vuelve termina la sesión (`ReuseDetected`), salvo en los 10 segundos posteriores a su reemplazo, en los que responde 409 sin tokens.
- `logout` termina la sesión. Cambiar o recuperar la contraseña termina todas las de la cuenta.
- El evento `OnTokenValidated` del bearer rechaza el access token de una sesión terminada, de una cuenta inactiva o con otro security stamp.
- Las conexiones a los hubs pasan la misma revisión mientras viven (Fase 9, `HubAccessSweeper`, cada `Hubs:AccessCheckInterval`): se cierran cuando su sesión termina o pierden el acceso al negocio o el permiso del hub.
- Desde la Fase 9 los refresh tokens se buscan solo por su hash (`RefreshToken.HashOf`); las columnas del valor en texto quedan sin usar hasta que se borren.

Cuentas (Fase 9):
- `UserRole.None` (0) es el valor por defecto: no da permisos y no se puede guardar. `SuperAdmin` (9 en el código) se guarda como 0, así que las filas existentes no cambiaron (`UserRoleStorage`).
- El login responde igual, y tarda lo mismo, para un correo sin cuenta, una cuenta inactiva o bloqueada y una contraseña incorrecta (`LoginTiming` verifica contra un hash ficticio); una cuenta bloqueada responde así aunque la contraseña sea correcta. "Debe confirmar su cuenta" solo sale después de la contraseña correcta.
- Contraseñas nuevas: al menos 8 caracteres (`PasswordPolicy.MinimumLength`) con mayúscula, minúscula y dígito; `GuessablePasswordValidator`, registrado en Identity, rechaza las comunes y las que contienen la parte local del correo, el nombre o el apellido. El cliente muestra la regla (`utils/passwordRules.ts`).

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
- Desde la Fase 5 el middleware ya no tiene mapeo por ruta (ver abajo).

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
- "Administra el negocio" (`CurrentAccessContext.IsTenantManagerAsync`, `[RequiresTenantManager]`, todos los permisos en `[RequiresPermission]`, las reglas por recurso de caja y taller, y los hubs) se decide con la membresía leída en cada petición (`TenantAccessSnapshot.IsManager`), no con el claim: un token emitido antes de un cambio de rol no conserva el acceso de administrador.
- `GET /auth/profile` devuelve el rol del token y `GET /api/v1/me/access` el rol efectivo actual (`TenantAccessSnapshot.EffectiveRole`); la app usa el segundo, así el menú y la página de entrada coinciden con lo que decide la API aunque el token sea anterior a un cambio de rol. Al iniciar sesión la app abre la página de entrada del rol (Inicio para administradores; Vender, Cocina o Taller para el resto) en lugar de una ruta fija.
- Equipo (`UpdateEmployeeCommand`, `SetEmployeeActiveCommand`, `EmployeeAccessRules`): el dueño no se puede desactivar ni cambiar de rol (su nombre sí); nadie se quita a sí mismo el rol de administrador; un negocio conserva al menos un dueño o admin activo, contado por membresía. El rol de la cuenta solo cambia si la persona no pertenece a otro negocio: de lo contrario solo cambia su membresía en este.
- `POST /api/v1/me/tenants` (`CreateBusinessCommand`, Fase 4) abre otro negocio con la cuenta que lo pide, como dueño, con lo mismo que un negocio del registro (`BusinessProvisioning`: sucursal y almacén predeterminados, público en general, IVA incluido y la prueba del plan para negocios nuevos). Una cuenta de cliente o el administrador de la plataforma no abren negocios y una cuenta es dueña de hasta 10.
- La migración `AlignTenantAdminMemberships` marca como `Admin` las membresías `Staff` de cuentas `TENANT_ADMIN` que solo pertenecen a un negocio, para que nadie que hoy administra su negocio pierda el acceso con la regla nueva. Es irreversible a propósito: su `Down` no degrada a nadie.

## Equipo, cobro y acceso (Fase 4E)

- **Invitaciones en lugar de contraseñas inventadas.** `CreateEmployeeCommand` crea la cuenta sin contraseña y una membresía `Invited`; la respuesta trae `InvitationUrl` (`/aceptar-invitacion?userId=…&token=…`) para copiarla o mandarla por WhatsApp, y el mismo enlace sale por correo cuando está configurado. El token es de un proveedor propio de Identity (`InvitationTokenProvider`, 7 días): no sirve para restablecer una contraseña, y elegir la contraseña cambia el security stamp, así que el enlace sirve una vez. `GET /auth/invitation` muestra quién invitó y el correo con que se entrará; `POST /auth/accept-invitation` fija la contraseña, confirma el correo, activa todas las invitaciones pendientes de la cuenta e inicia sesión. Una cuenta que ya existe se une con su contraseña sin cambios; una cuenta de cliente no se agrega al equipo.
- **Asientos del plan.** Una invitación pendiente ocupa su lugar (`SubscriptionRules.CheckUserLimitAsync` cuenta activos e invitados). Cancelar una invitación es desactivar la membresía; reactivar a alguien que nunca eligió contraseña le devuelve la invitación (`Invited`), no un acceso que no puede usar. `POST /employees/{id}/resend-invitation` da un enlace nuevo mientras espera.
- **Roles con nombre.** `GET /employees/role-presets`: Gerente (administra), Cajero, Mesero, Cocina y Técnico. Elegir uno llena los permisos; cambiar cualquiera lo vuelve "Personalizado" (cuenta `STAFF`). Lo guardado son siempre los permisos.
- **Cobrar es su propio permiso.** `AddOrderPaymentCommand` y el comprobante de pago exigen `ChargeOrders`; `CreateOrderCommand` lo exige solo si trae pagos (`IRequiresContentPermissions`, que `AuthorizationBehavior` revisa después de la regla del caso de uso). Un mesero guarda el pedido y la caja lo cobra; la app oculta "Cobrar" sin el permiso. La migración `AddChargePermissionAndSmsCooldown` agrega `ChargeOrders` a quien tenía `ManageOrders` guardado (salvo cuentas de cocina), así nadie pierde lo que hoy hace.
- **"Sin acceso" explícito.** En crear y editar, `Permissions = null` toma los del rol y una lista vacía guarda `None` (sin acceso). `ManageEmployees` ya no se ofrece al personal: quien administra permisos podría darse todos.
- **El teléfono es de la persona.** Recupera la contraseña por SMS, así que el admin ya no lo cambia (`UpdateEmployeeCommand` no tiene teléfono) y el perfil tampoco lo cambia directo: `POST /auth/profile/phone` pide la contraseña y manda un código al número nuevo; `POST /auth/profile/phone/confirm` lo confirma (y mueve el perfil de cliente al número nuevo). La confirmación anónima de `/confirm-phone` solo vale para una cuenta que se está registrando (con contraseña y sin nada confirmado); ahí mismo `POST /auth/change-unconfirmed-phone` corrige un número mal escrito con la contraseña. El nombre de una cuenta que usa otro negocio solo lo cambia la persona.
- **Recuperar por SMS.** `POST /auth/forgot-password/sms` (correo de la cuenta) manda un código al teléfono confirmado y responde igual exista o no; `POST /auth/reset-password/sms` cambia la contraseña con el código y cierra las sesiones abiertas.
- **Límites por cuenta.** Un código SMS por minuto por cuenta (`ApplicationUser.SmsCodeSentAt`, `AccountSecurity.TryReserveSmsCodeAsync`). Contraseñas y códigos equivocados cuentan para el bloqueo de Identity (5 intentos, 15 minutos) en el login, el código SMS, el cambio de teléfono y el cambio de número al registrarse; recuperar la contraseña levanta el bloqueo. El cliente reintenta con espera la renovación de la sesión si recibe 429 o 503.
- Los mensajes de Identity (contraseñas, correo duplicado, enlaces vencidos) salen en español (`SpanishIdentityErrorDescriber`).

## Autorización en un solo lugar (Fase 5)

- **Middleware sin mapeo por ruta.** `TenantResolutionMiddleware` resuelve el negocio (del token, o el override auditado del administrador de plataforma), valida la membresía activa y la suspensión, y prepara `CurrentAccessContext`. Toda ruta `/api` es del negocio salvo las de la cuenta (`/auth` salvo `/auth/my-tenant`, `/me`, `/twofactor`, `/platform`, `/public`, el webhook y `GET/POST /tenants`): una ruta nueva queda protegida sin agregarla a una lista, y quien perdió el acceso a un negocio todavía llega a su perfil y al cambio de negocio.
- **Controllers sin roles.** Solo `[Authorize]` o `[AllowAnonymous]`; `ArchitectureTests.Controllers_ShouldLeaveWhoMayDoWhatToTheUseCases` lo exige. Una cuenta `KITCHEN` con `ManageOrders` ya puede vender, como dicen sus permisos.
- **Sin comparar el texto del rol.** Los handlers que repetían "solo `TENANT_ADMIN`" (logo, WhatsApp, política de ventas, catálogo inicial, equipo) usan la regla declarada; los que deciden por recurso (cerrar la caja de otro, mover movimientos de otra caja, reasignar piezas del taller) preguntan a `CurrentAccessContext`, que lee la membresía. Reembolsar y anular declaran `ProcessRefunds` en la solicitud (`IRequiresContentPermissions`).
- **Módulos declarados.** Inventario, compras y citas declaran su módulo; los handlers ya no lo revalidan (45 revisiones menos). Siguen en el handler las reglas de contenido: unidades de servicio, tipos de pedido y lo que muestra cada giro.
- **Cocina separada de pedidos.** El tablero usa `MoveKitchenOrderCommand` (solo en preparación o listo) y `UpdateKitchenTicketStatusCommand`, con `ManageServiceBoard` y el módulo `Kitchen`; quien toma pedidos usa `UpdateOrderStatusCommand` (cualquier estado, cancelar incluido) y `MarkRoundServedCommand`, con `ManageOrders`. La cocina no cancela pedidos.
- Equipo y actividad del negocio quedan para dueños y admins (`[RequiresTenantManager]`), como mostraba la app; la promoción rápida pide `ManageCampaigns` como las campañas.

## Guardrails

- Los guards de React y menús ocultos no son seguridad.
- Cada operación sensible se valida en backend, en su caso de uso (regla declarada y, si aplica, regla por recurso en el handler).
- Revisa acceso al objeto, no solo acceso al endpoint.
- `SUPER_ADMIN` requiere control cross-tenant explícito; nunca se interpreta como permiso universal implícito.
- Los códigos desconocidos se rechazan/normalizan mediante `TenantPermissionCatalog`.
- Cambios de rol, permiso o capability se auditan y deben invalidar o limitar sesiones/tokens según riesgo.
- Casos de uso nuevos declaran su regla con los atributos de autorización v2; el middleware no es la fuente de verdad.

## Deuda actual

- `SUPER_ADMIN` tiene todos los permisos en cualquier negocio (no los módulos). `UpdateOwnerWhatsAppConfigCommand` (WhatsApp personal de la cuenta, sin negocio) todavía compara el rol de la cuenta en el handler.
- La autorización por recurso (por ejemplo, cerrar solo la caja propia) sigue en los handlers, con la membresía de `CurrentAccessContext`.
- Una conexión SignalR ya abierta sigue hasta que vence su token aunque su sesión termine (los access tokens y las conexiones nuevas se rechazan al instante).
- El login todavía distingue cuentas sin confirmar o bloqueadas (el bloqueo por intentos ya existe desde la Fase 4E).
- La lista del equipo (`GET /employees`) es de dueños y admins: la agenda la usa para elegir quién atiende y el personal con `ManageAppointments` no la ve.
- La matriz se prueba por caso de uso (`UseCaseRulesTests`, `AuthorizationBehaviorTests`) y la Fase 5 la comprobó contra la API con SQL Server (cocina, mesero y dueño); esa prueba de API no corre en CI.
