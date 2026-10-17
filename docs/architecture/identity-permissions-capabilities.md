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
| Plataformas de reparto con sus tasas: verlas y guardarlas (`GetDeliveryPlatformsQuery`, `SaveDeliveryPlatformCommand`, Fase 22F) | dueño o admin (`Manager`) | — |
| Qué plataformas están activas, sin tasas (`GetDeliveryPlatformChannelsQuery`, para Vender, Pedidos, Reportes y, desde la Fase 22F-2, Precios en apps) | `ManageOrders`, `ViewReports` o `ManageCatalog` (`Permission(ManageOrders, ViewReports, ManageCatalog)`) | — |
| Precios en las apps de las plataformas: la lista y guardarlos (`GetPlatformPriceListQuery`, `SavePlatformPricesCommand`, Fase 22F-2) | `ManageCatalog` (`Permission(ManageCatalog)`), como los precios del menú | — |
| Ventas por canal y pedidos de una plataforma (`GetSalesByChannelQuery`, `GetDeliveryPlatformOrdersQuery`) | `ViewReports` (`Permission(ViewReports)`) | — |
| Liquidaciones de las plataformas: la lista, una liquidación, los pedidos pendientes de un periodo y lo que debe cada plataforma (`GetPlatformSettlementsQuery`, `GetPlatformSettlementQuery`, `GetPendingPlatformOrdersQuery`, `GetPlatformReceivablesQuery`, Fase 22G) | `ViewReports` (`Permission(ViewReports)`), como Ventas por canal | — |
| Abrir, guardar, cerrar y anular una liquidación (`CreatePlatformSettlementCommand`, `UpdatePlatformSettlementCommand`, `ClosePlatformSettlementCommand`, `VoidPlatformSettlementCommand`, Fase 22G) | dueño o admin (`Manager`), como las tasas de las plataformas | — |
| Zonas de entrega: la lista (`GetDeliveryZonesQuery`, Fase 22B) | cualquier miembro (`Member`): quien toma un pedido necesita saber a dónde se entrega | — |
| Agregar, guardar y borrar una zona de entrega (`CreateDeliveryZoneCommand`, `UpdateDeliveryZoneCommand`, `DeleteDeliveryZoneCommand`, Fase 22B) | dueño o admin (`Manager`) | — |
| Anotar o corregir el viaje de Paquetenvia de un pedido a domicilio (`RegisterCourierShipmentCommand`, Fase 22C) | `ManageOrders` | — |
| Cotizar el envío a un código postal en el menú público (`GetPublicDeliveryQuoteQuery`, Fase 22C) | anónimo (`[AllowAnonymousRequest]`), con el límite `public-menu`; sin el nombre de la zona | — |
| Mandar un pedido a domicilio con el repartidor, con lo que cobra en efectivo, o corregir su salida (`DispatchOrderCommand`, Fase 22D) | `ManageOrders` | — |
| Lo que debe cada repartidor, sus pedidos pendientes, sus liquidaciones y una liquidación (`GetCourierReceivablesQuery`, `GetPendingCourierOrdersQuery`, `GetCourierSettlementsQuery`, `GetCourierSettlementQuery`, Fase 22D) | `ManageCash` (`Permission(ManageCash)`): Caja › Repartidores | — |
| Liquidar al repartidor (`CreateCourierSettlementCommand`, Fase 22D) | `ChargeOrders`, y además `ManageCash` por contenido (`IRequiresContentPermissions`): cobra los pedidos y mueve la caja propia | — |
| Cotizar una visita a un código postal y la ruta del día (`GetVisitQuoteQuery`, `GetVisitRouteQuery`, Fase 22E) | `ManageAppointments`, como agendar | `Appointments` |
| El tiempo de traslado entre visitas: leerlo (`GetHomeVisitSettingsQuery`, Fase 22E) | cualquier miembro (`Member`), como las zonas | `Appointments` |
| Cambiar el tiempo de traslado (`UpdateHomeVisitSettingsCommand`, Fase 22E) | dueño o admin (`Manager`), como las zonas | `Appointments` |
| Explorar: los negocios que publicaron su menú, por categoría y búsqueda (`GetPublicDirectoryQuery`, Fase 24A) | anónimo (`[AllowAnonymousRequest]`), con el límite `public-menu`; solo lo que el menú de cada uno ya muestra | — |
| Cómo se ve el negocio en Explorar: leerlo y cambiar su categoría, su descripción y «Aparecer en Explorar» (`GetDirectoryProfileQuery`, `UpdateDirectoryProfileCommand`, Fase 24A) | dueño o admin (`Manager`), como el menú en línea | — |
| Las direcciones de la cuenta: leerlas, guardarlas, cambiarlas y borrarlas (`GetMyAddressesQuery`, `SaveMyAddressCommand`, `UpdateMyAddressCommand`, `DeleteMyAddressCommand`, Fase 24B) | sesión (`[RequiresAuthentication]`); solo las de la cuenta de la sesión, y solo una cuenta de cliente activa las guarda (otra lee una lista vacía) | — |
| Enviar un pedido del menú en línea con la sesión de una cuenta de cliente (`PlacePublicOrderCommand`, Fase 24B) | anónimo, como siempre (`[AllowAnonymousRequest]`, límite `public-order`); con la sesión de una cuenta de cliente activa el pedido queda en esa cuenta y guarda su dirección. La sesión del equipo de un negocio compra como invitado | — |
| Cobrar una cita, con su cargo por visita (`CreateOrderCommand` con `AppointmentId`, Fase 22E) | `ManageOrders` (y `ChargeOrders` si trae pagos), y además `ManageAppointments` por contenido (`IRequiresContentPermissions`): la venta completa la cita en la agenda, y Vender la lee como la agenda | — |

Pedidos de plataformas (Fase 22F, [ADR 0013](../adr/0013-delivery-platform-orders.md)):

- **Registrar uno es tomar un pedido** (`CreateOrderCommand`, `ManageOrders`). Si lo cobró la plataforma, la venta no trae pagos y no pide `ChargeOrders`: el servidor registra el pago «Plataforma» y nadie maneja ese dinero. Una cuenta de cocina con `ManageOrders` puede capturar el pedido de la tableta.
- **Si cobra el negocio, cobrarlo pide `ChargeOrders`,** como cualquier cobro, al registrarlo o después.
- **Las tasas de los contratos son de dueños y administradores.** Quien toma pedidos o lee reportes solo sabe qué plataformas están activas. La comisión de cada pedido solo sale en Reportes; `OrderDto`, el ticket, Cocina y el aviso en vivo nunca la llevan.
- **La API lo prueba con personal real** (`CrossTenantDeliveryPlatformsSqlServerTests`):
  - cocina, solo reportes y solo catálogo reciben 403 en `GET` y `PUT /delivery-platforms`;
  - cocina, solo reportes y, desde la Fase 22F-2, solo catálogo reciben 200 en `/channels`;
  - solo reportes recibe 200 en los dos reportes nuevos, y cocina, 403.
- **Sin capability ni módulo:** ningún caso de uso nuevo declara un módulo.

Precios en las apps (Fase 22F-2, [ADR 0014](../adr/0014-delivery-platform-prices.md)):

- **Los cambia quien cambia los precios del menú** (`ManageCatalog`): la lista y el guardado. Las tasas de los contratos siguen siendo de dueños y administradores.
- **Los lee quien puede leer el catálogo:** `GET /products?priceChannel=` pide lo mismo que la lista de productos (catálogo, pedidos, inventario o agenda). Los precios de una app no son secretos: los ve cualquier cliente en ella.
- **La API lo prueba con personal real** (`CrossTenantPlatformPricesSqlServerTests`): solo catálogo recibe 200 en la lista y en `/channels`, 204 en el guardado y 403 en las tasas; un cajero (`ManageOrders`) y solo reportes reciben 403 en la lista y el guardado; el cajero lee el catálogo con los precios de la plataforma.

Liquidaciones de las plataformas (Fase 22G, [ADR 0015](../adr/0015-delivery-platform-settlements.md)):

- **Las lee quien lee los reportes** (`ViewReports`): la lista, el detalle, los pedidos pendientes y lo que debe cada plataforma. En Ventas por canal ya veía la comisión y el neto de cada pedido.
- **Las cambian dueños y administradores:** abrir, guardar, cerrar y anular, como los términos de cada plataforma. Cerrar y anular quedan en la bitácora (`PlatformSettlementClosed` y `PlatformSettlementVoided`) en el mismo guardado.
- **En el cliente**, Reportes › Liquidaciones se abre con el área `reports`, y sus botones de cambio salen solo a `access.isAdmin`; a los demás, la liquidación se ve como está. La API decide igual.
- **La API lo prueba con personal real** (`CrossTenantPlatformSettlementsSqlServerTests`): solo reportes recibe 200 en las cuatro lecturas y 403 en abrir, guardar, cerrar y anular; un cajero (`ManageOrders`) recibe 403 en todo.
- **Sin capability ni módulo:** ningún caso de uso nuevo declara un módulo.

Un negocio siempre tiene un tipo de venta (productos, servicios o alimentos), como su plan: `TenantCapabilityRules.NormalizeCapabilities` completa el de su giro si su configuración no deja ninguno y `UpdateMyTenantCommand` no guarda módulos sin uno. Por eso ningún caso de uso exige "comercio".

En el cliente, `src/utils/access.ts` (`getAccess`) traduce la misma matriz (permiso, módulo y "administra") para el menú, las rutas (`AccessRoute`), la página de entrada (`getLandingPath`: administradores al Dashboard, el resto a la pantalla de su trabajo: Vender, Cocina o Taller) y los botones de acción. Es ayuda de navegación: la API sigue decidiendo.

Cambiar de tenant rota el refresh token de la sesión (el anterior se revoca) sin cambiar de sesión.

Sesiones (Fase 8, `UserSessions` y `AccessTokenSessionValidator`):
- Cada inicio de sesión (login, 2FA, invitación aceptada) abre una sesión, cuyo id viaja en el access token (`session_id`) junto con una huella del security stamp. El registro no abre sesión: la cuenta inicia sesión después de activarla con el enlace del correo.
- Renovar reemplaza el refresh token dentro de la misma sesión.
- Un token ya reemplazado que vuelve termina la sesión (`ReuseDetected`), salvo en los 10 segundos posteriores a su reemplazo, en los que responde 409 sin tokens.
- `logout` termina la sesión. Cambiar o recuperar la contraseña termina todas las de la cuenta.
- El evento `OnTokenValidated` del bearer rechaza el access token de una sesión terminada, de una cuenta inactiva o con otro security stamp.
- Las conexiones a los hubs pasan la misma revisión mientras viven (Fase 9, `HubAccessSweeper`, cada `Hubs:AccessCheckInterval`): se cierran cuando su sesión termina o pierden el acceso al negocio o el permiso del hub.
- Desde la Fase 9 los refresh tokens se buscan solo por su hash (`RefreshToken.HashOf`); las columnas del valor en texto quedan sin usar hasta que se borren.

Cuentas (Fase 9):
- `UserRole.None` (0) es el valor por defecto: no da permisos y no se puede guardar. `SuperAdmin` (9 en el código) se guarda como 0, así que las filas existentes no cambiaron (`UserRoleStorage`).
- El login responde igual, y tarda lo mismo, para un correo sin cuenta, una cuenta inactiva o bloqueada y una contraseña incorrecta (`LoginTiming` verifica contra un hash ficticio); una cuenta bloqueada responde así aunque la contraseña sea correcta. Desde la Fase 23A una cuenta sin correo ni teléfono confirmado responde lo mismo, aun con la contraseña correcta: el mensaje dice que el correo o la contraseña no son correctos o que la cuenta aún no está activa, porque el registro responde igual exista o no la cuenta y una respuesta distinta a la contraseña recién elegida lo delataría.
- Contraseñas nuevas: al menos 8 caracteres (`PasswordPolicy.MinimumLength`) con mayúscula, minúscula y dígito; `GuessablePasswordValidator`, registrado en Identity, rechaza las comunes y las que contienen la parte local del correo, el nombre o el apellido. El cliente muestra la regla (`utils/passwordRules.ts`).

Activación por correo: `POST /auth/register/business` y `POST /auth/register/customer` crean la cuenta, piden el enlace de confirmación del correo y responden solo un mensaje (`{ message }`), sin tokens, sin cookie de sesión y sin código por SMS. Desde la Fase 23A responden lo mismo si el correo ya tiene cuenta: no crean nada y esa cuenta recibe un correo que dice que ya existe, con cómo entrar y cómo recuperar la contraseña (y, a una cuenta de cliente, que un negocio se registra con otro correo). Una cuenta empezada y sin activar recibe en cambio que lo llenado esta vez no se guardó y el enlace que activa la primera, con la contraseña de entonces y el aviso de no abrirlo si no la empezó; una invitada al equipo que aún no elige contraseña, su invitación (también si pide otro enlace de activación); una desactivada, nada. La contraseña y, en el de cliente, un teléfono confirmado por otra cuenta se revisan antes de buscar el correo, así que tampoco lo delatan, y en ese caso también se hashea, como al crear la cuenta (`SignUpTiming`). Los correos de las cuentas (activación, recuperación, "ya tienes una cuenta" e invitación) salen del outbox después de responder (`AccountEmailRequestedEvent`, que no guarda el enlace: se crea al enviar), con reintentos si el proveedor falla; el de la activación se guarda en la misma transacción que la cuenta. La app muestra `/check-email` ("Revisa tu correo"), que no nombra la dirección y permite pedir un enlace nuevo con `POST /auth/resend-confirmation-email` (misma respuesta exista o no la cuenta). El enlace abre `/confirm-email`, que confirma contra `GET /auth/confirm-email` y manda al login con el aviso de cuenta activada; si el enlace venció (un día, el proveedor por defecto de Identity), está incompleto o no es válido, responde un solo mensaje (`ConfirmEmailCommandHandler.InvalidLinkMessage`) y ofrece pedir uno nuevo. Abrir otra vez un enlace ya usado responde éxito. El login exige el correo o el teléfono confirmado.

El teléfono se verifica después desde el perfil (`POST /auth/profile/phone` y `/confirm`), que también vincula los pedidos de invitado de un cliente. La confirmación anónima de `/confirm-phone` sigue disponible para una cuenta sin nada confirmado: identifica la cuenta por su `userId` en la URL y `POST /auth/resend-phone-confirmation` acepta `{ userId }` para reenviar el código (o un teléfono E.164), sin revelar si la cuenta existe y con el límite `auth`; el registro ya no lleva a esa pantalla.

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
- **Deny by default:** una solicitud sin regla (o con más de una) no se ejecuta. `AuthorizationBehaviorTests` exige una regla en cada caso de uso (254 con la Fase 24B) y fija la lista de anónimos; un caso de uso nuevo sin regla rompe CI.
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

- **Invitaciones en lugar de contraseñas inventadas.** `CreateEmployeeCommand` crea la cuenta sin contraseña y una membresía `Invited`; la respuesta trae `InvitationUrl` (`/aceptar-invitacion?userId=…&token=…`) para copiarla o mandarla por WhatsApp, y desde la Fase 23A un enlace propio sale por correo desde el outbox cuando hay proveedor (los dos valen; si la invitación se acepta o se quita antes, el correo ya no sale). El token es de un proveedor propio de Identity (`InvitationTokenProvider`, 7 días): no sirve para restablecer una contraseña, y elegir la contraseña cambia el security stamp, así que el enlace sirve una vez. `GET /auth/invitation` muestra quién invitó y el correo con que se entrará; `POST /auth/accept-invitation` fija la contraseña, confirma el correo, activa todas las invitaciones pendientes de la cuenta e inicia sesión. Una cuenta que ya existe se une con su contraseña sin cambios; una cuenta de cliente no se agrega al equipo.
- **Asientos del plan.** Una invitación pendiente ocupa su lugar (`SubscriptionRules.CheckUserLimitAsync` cuenta activos e invitados). Cancelar una invitación es desactivar la membresía; reactivar a alguien que nunca eligió contraseña le devuelve la invitación (`Invited`), no un acceso que no puede usar. `POST /employees/{id}/resend-invitation` da un enlace nuevo mientras espera.
- **Roles con nombre.** `GET /employees/role-presets`: Gerente (administra), Cajero, Mesero, Cocina y Técnico. Elegir uno llena los permisos; cambiar cualquiera lo vuelve "Personalizado" (cuenta `STAFF`). Lo guardado son siempre los permisos.
- **Cobrar es su propio permiso.** `AddOrderPaymentCommand` y el comprobante de pago exigen `ChargeOrders`; `CreateOrderCommand` lo exige solo si trae pagos (`IRequiresContentPermissions`, que `AuthorizationBehavior` revisa después de la regla del caso de uso). Un mesero guarda el pedido y la caja lo cobra; la app oculta "Cobrar" sin el permiso. Desde la Fase 22F, un pedido que cobró la plataforma no trae pagos, así que no pide `ChargeOrders` (ver arriba). La migración `AddChargePermissionAndSmsCooldown` agrega `ChargeOrders` a quien tenía `ManageOrders` guardado (salvo cuentas de cocina), así nadie pierde lo que hoy hace.
- **"Sin acceso" explícito.** En crear y editar, `Permissions = null` toma los del rol y una lista vacía guarda `None` (sin acceso). `ManageEmployees` ya no se ofrece al personal: quien administra permisos podría darse todos.
- **El teléfono es de la persona.** Recupera la contraseña por SMS, así que el admin ya no lo cambia (`UpdateEmployeeCommand` no tiene teléfono) y el perfil tampoco lo cambia directo: `POST /auth/profile/phone` pide la contraseña y manda un código al número nuevo; `POST /auth/profile/phone/confirm` lo confirma (y mueve el perfil de cliente al número nuevo). La confirmación anónima de `/confirm-phone` solo vale para una cuenta que se está registrando (con contraseña y sin nada confirmado); ahí mismo `POST /auth/change-unconfirmed-phone` corrige un número mal escrito con la contraseña. El nombre de una cuenta que usa otro negocio solo lo cambia la persona.
- **Recuperar por SMS.** `POST /auth/forgot-password/sms` (correo de la cuenta) manda un código al teléfono confirmado y responde igual exista o no; desde la Fase 11 solo guarda la solicitud en el outbox y `VerificationCodeRequestedHandler` busca la cuenta y envía el código después de responder (lo mismo el reenvío de la confirmación del teléfono). `POST /auth/reset-password/sms` cambia la contraseña con el código y cierra las sesiones abiertas.
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
