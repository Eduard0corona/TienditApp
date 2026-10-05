# Multi-tenancy

## Estado actual

Se usa una base SQL Server compartida. `ApplicationDbContext` recorre el modelo y agrega un filtro `TenantId == CurrentTenantId` a cada entidad que implementa `ITenantScoped`. `TenantResolutionMiddleware` toma `tenant_id` del JWT, valida la membresía activa y configura `ICurrentTenantService`. Desde la Fase 5 toda ruta `/api` es del negocio salvo las de la cuenta (inicio de sesión y perfil, `/me`, `/twofactor`, `/platform`, `/public`, el webhook y la lista de negocios de la plataforma): una ruta nueva valida la membresía sin agregarla a una lista. Quién puede hacer qué lo declara cada caso de uso (ver [identidad y permisos](identity-permissions-capabilities.md)).

- `TenantSaveChangesInterceptor` completa un `TenantId` vacío en entidades nuevas, rechaza un `TenantId` ajeno (salvo el de un tenant creado en la misma unidad de trabajo, como en el alta de negocio) y rechaza cambiar el `TenantId` de una entidad existente (`TenantMismatchException`, 403).
- Las exclusiones del middleware se comparan por prefijo y las rutas de "mi tenant" son del negocio: un miembro desactivado ya no puede usarlas con un token vigente.
- `TenantOrdersController` rechaza un tenant de ruta distinto del tenant validado.
- Los hubs validan al conectar membresía activa y permiso (`OrderHub`: gestionar órdenes, tablero o ver reportes; `ChatHub`: WhatsApp), con dueños y admins leídos de la membresía, y se cierran cuando expira el token.
- El override `X-Tenant-Id` de `SUPER_ADMIN` queda registrado como evento de auditoría en el log (usuario, tenant, tenant del token, método y ruta).

- Negocio suspendido (Fase 3): `TenantAccessSnapshot.TenantSuspended` (negocio `Suspended` o `Inactive`) hace que el middleware responda 403 `tenant-suspended` a todo excepto leer el perfil y el plan; los hubs no lo unen. Ver [planes y facturación](../domain/billing-and-platform.md).
- Consola de plataforma (Fase 3): las acciones del administrador de la plataforma sobre un negocio fijan ese negocio en `ICurrentTenantService` antes de leer y escribir, y se auditan en él; los listados de toda la plataforma usan `IgnoreQueryFilters()` solo para leer.
- Trabajo en segundo plano y webhook (Fase 6): un solo helper, `BackgroundScopes`, crea el alcance de un negocio (`CreateTenantScope`: fija `ICurrentTenantService` y el actor) o el de sistema (`CreateSystemScope`, sin negocio, solo para encontrar el trabajo). Los workers de mensajería, outbox, ventas diarias y facturación leen sin filtro únicamente para encontrar qué toca y hacen cada pieza en el alcance de su negocio. El procesador del webhook busca el negocio dueño del `phone_number_id` (la única lectura sin filtro) y procesa esa parte de la entrega en un alcance propio, con su `DbContext`: los mensajes entrantes, las bajas, las respuestas automáticas y los estados de entrega usan el filtro del negocio, y un estado reportado al número de un negocio no cambia mensajes de otro. `ProcessInboundMessageCommand`, `MessagingConsentService` y `WhatsAppService` rechazan trabajar para un negocio distinto del que está en alcance.
- Menú público (Fase 3): `GET /api/v1/public/menu/{slug}` y `POST .../orders` son anónimos y el único escenario pre-tenant que crea datos. El tenant sale del slug de un negocio activo cuyo `TenantSettings.OnlineMenuMode` lo permite (menú, o menú con pedidos) y el caso de uso fija `ICurrentTenantService` con ese negocio antes de consultar; así los filtros globales y `TenantSaveChangesInterceptor` aplican igual que en una sesión. Solo expone el catálogo publicado (productos disponibles, sin servicios ni costos), limita por IP (`public-menu` 120/min, `public-order` 6 cada 10 min) y como máximo 3 pedidos pendientes por teléfono.
- Cobertura completa (Fase 8): `TenantSettings`, `WhatsAppConfig`, `OrderItem` y `OrderStatusHistory` implementan `ITenantScoped`, así que toda entidad con `TenantId` tiene el filtro global y pasa por el interceptor (`ArchitectureTests` lo exige a cualquier entidad nueva; `WhatsAppMessage` no tiene `TenantId` y se filtra por su conversación). Las partidas y el historial de estado guardan el negocio de su orden; la migración `ScopeOrderLinesAndHistoryByTenant` lo copia de la orden y, como se aplica mientras corre la versión anterior, lo que esta agrega en ese lapso toma el valor vacío (el filtro lo oculta) hasta que la versión nueva lo completa al arrancar (`OrderTenantBackfillService`). Las lecturas que necesitan la configuración de un negocio antes de entrar en él (el menú público por slug, el acceso al negocio, qué negocios tienen recordatorios) cruzan el filtro con su predicado explícito y están en `TenantFilterBypassAllowlistTests`.
- Suite HTTP de aislamiento (Fase 8): `CrossTenantHttpSqlServerTests` levanta la API completa (`WebApplicationFactory`) contra SQL Server 2022 con dos negocios registrados por la API. El dueño de uno intenta leer, cambiar y borrar por id el catálogo, los clientes, los proveedores y las órdenes del otro (cambiar su estado, cobrarla), abrir su comprobante privado, usar su encabezado `X-Tenant-Id`, su ruta de órdenes y cambiarse a él: todo responde como si no existiera y nada del otro negocio cambia.

Identity es global; `TenantUser` vincula usuario y tenant. Capabilities se leen de `TenantSettings`. SignalR agrupa conexiones por el claim de tenant. Muchas claves e índices incluyen `TenantId`.

## Reglas obligatorias

- Toda entidad propiedad de un tenant implementa `ITenantScoped`.
- `TenantId` es obligatorio y no cambia después de crear la entidad.
- Una nueva entidad tenant-scoped debe quedar bajo filtro global y tener pruebas de modelo/aislamiento.
- El frontend nunca es fuente confiable del tenant. El tenant efectivo viene de identidad autenticada y acceso validado.
- Está prohibido usar `IgnoreQueryFilters()` como solución rápida. Cada uso aceptado debe filtrar explícitamente por tenant/usuario, justificar el escenario pre-tenant o administrativo y probar aislamiento.
- `IgnoreQueryFilters()` desactiva los filtros de toda la consulta (joins, `Include`, navegaciones y subconsultas), no solo del `DbSet` donde se invoca; cada entidad tenant-scoped que participe necesita su predicado explícito.
- Toda operación cross-tenant es administrativa, explícita, autorizada y auditada.
- Las pruebas cubren leer, modificar, eliminar y descargar desde un tenant distinto, además de IDs adivinados.
- Archivos, blobs, cachés, jobs, SignalR y notificaciones mantienen namespace/partición por tenant.
- `SUPER_ADMIN` no implica bypass silencioso: requiere selección explícita, autorización, auditoría y límites.

## Riesgos comprobados

1. Resuelto en la Fase 8: `TenantSettings` y `WhatsAppConfig` implementan `ITenantScoped` y tienen el filtro global (la auditoría del 2026-10-01 había encontrado una consulta del reporte operativo que no filtraba por negocio).
2. Hay múltiples `IgnoreQueryFilters()` en autenticación, empleados, tenants, webhooks e integraciones. `TenantFilterBypassAllowlistTests` (Fase 4) fija cada archivo que lo usa, cuántas veces y por qué (flujos sin negocio, membresías del usuario, consola de plataforma, workers, webhooks): un uso nuevo o adicional, o una entrada que ya no se usa, rompe CI hasta revisarlo y anotar su motivo; desde la Fase 6 no cuenta las menciones en comentarios. Son 97 usos en 50 archivos (el diagnóstico contó 70 en 40 antes de la consola de plataforma, las invitaciones, los folios y la mensajería; la Fase 8 sumó el menú público, que encuentra su negocio por slug con su configuración, y los recordatorios del worker de mensajería, que buscan qué negocios los tienen activos): la mayoría son flujos de cuenta y de plataforma que cruzan negocios por naturaleza, 9 son datos de desarrollo y los de workers y webhook solo encuentran el trabajo. Cada uso sigue necesitando su predicado explícito.
3. El override `X-Tenant-Id` de `SUPER_ADMIN` ya se audita en el log, pero no exige justificación, no tiene límites y no se persiste en `AuditLog`.
4. Resuelto en la Fase 5: toda ruta `/api` es del negocio salvo las de la cuenta, que sí son una lista; una ruta nueva valida la membresía sin agregarla a ninguna lista.
5. Resuelto en la Fase 8: `OrderItem` y `OrderStatusHistory` tienen `TenantId` (el de su orden) y el filtro global; una consulta directa a sus `DbSet` ya solo ve el negocio en contexto.
6. La suite HTTP de aislamiento (Fase 8) cubre API y archivos privados; los hubs de SignalR y los webhooks aún no están en ella (tienen pruebas del middleware, de los hubs, del procesador del webhook y del interceptor).

Los puntos 2, 3 y 6 siguen siendo deuda/riesgo.
