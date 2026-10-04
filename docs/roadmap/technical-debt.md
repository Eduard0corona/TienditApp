# Deuda técnica

Actualizada el 2026-10-04 tras las Fases 0, 1 y 2 del roadmap de producto (la auditoría y sus correcciones son del 2026-10-01).

## Crítica antes de producción

1. Credenciales expuestas: cadenas de conexión de Azure SQL y el secreto JWT estuvieron versionados y siguen en el historial de git. El código ya no los contiene y la API rechaza el secreto publicado, pero hay que rotarlos, revisar el firewall de Azure SQL y verificar si la cuenta SUPER_ADMIN sembrada con la contraseña anterior existe en la base remota. Reescribir el historial es destructivo y requiere coordinación.
2. Aislamiento incompleto: `TenantSettings`/`WhatsAppConfig` no implementan `ITenantScoped`; `OrderItem`/`OrderStatusHistory` no tienen `TenantId`; quedan usos de `IgnoreQueryFilters()` por revisar; el override `X-Tenant-Id` de `SUPER_ADMIN` solo se audita en el log.
3. Falta una suite que pruebe aislamiento HTTP, EF contra SQL Server, archivos, SignalR y webhooks.
4. Archivos: el contenido se valida por firma, las fotos se re-codifican y el borrado por URL está confinado a `{área}/{tenantId}` en almacenamiento local y Azure Blob (rechaza traversal, otros tenants/áreas y URLs ajenas al contenedor). Pendiente: `/uploads` es estático y público (las evidencias de pago se descargan sin autorización), los PDF no pasan por un escaneo de malware, `UploadAsync` no valida carpeta y nombre, hay que sanear filas heredadas cuyo `ImageUrl`/`LogoUrl`/`AvatarUrl` apunte fuera de su carpeta (el borrado se rechaza con un warning y el archivo queda huérfano) y falta un namespace por usuario para avatares (el usuario es global y un avatar subido con otra membresía no se borra desde otro tenant).
5. Webhook WhatsApp (actualizado 2026-10-02): la firma `X-Hub-Signature-256` se verifica fail-closed (503 sin `WhatsApp:AppSecret`, 401 sin firma válida; `WhatsApp:AllowUnsignedWebhooks` solo se respeta en Development), `PhoneNumberId` y `ExternalMessageId` tienen índices únicos filtrados (migración `AddWhatsAppWebhookUniqueIndexes`, verificada en SQL Server LocalDB) y las entregas repetidas o concurrentes son idempotentes; si dos primeros mensajes de un número nuevo llegan a la vez, el índice único `(TenantId, CustomerPhone)` deja crear una sola conversación y el otro mensaje se guarda en ella. Lo mismo ocurre si el negocio escribe a un número nuevo mientras llega su primer mensaje, o si dos envíos del negocio coinciden: `POST /api/v1/whatsapp/chat/send` guarda el mensaje, ya entregado, en la conversación ganadora de su tenant en lugar de responder 500 (verificado en SQL Server LocalDB con y sin `READ_COMMITTED_SNAPSHOT`). `WhatsAppConversation` tiene token de concurrencia `rowversion` (migración `AddWhatsAppConversationVersion`, verificada en SQL Server LocalDB con y sin `READ_COMMITTED_SNAPSHOT`; se aplica antes de desplegar el código que la usa): mensajes simultáneos del mismo cliente, auto-respuestas, envíos del negocio y "marcar leído" recargan la conversación y reaplican su cambio, hasta 10 intentos, en lugar de sobrescribir `UnreadCount` y el último mensaje. Cada mensaje del payload se procesa por separado y el `POST` responde 500 si alguno, o la actualización de estados, no se pudo procesar, para que Meta reenvíe la entrega (hasta 7 días); los mensajes ya guardados se ignoran por `ExternalMessageId`. JSON inválido y `PhoneNumberId` desconocido se confirman con 200. Como Meta no firma un timestamp, el replay se mitiga con esa idempotencia. Pendiente: un mensaje que falle siempre se reintenta y registra error durante esos 7 días; el envío del negocio llama a Meta antes de guardar y no hay Outbox, así que si el guardado falla por otra causa el mensaje entregado queda fuera del historial y la API responde 500; si una conversación recibe más de 10 escrituras simultáneas, la que agota los intentos falla (el webhook responde 500 y Meta reenvía, pero un envío del negocio o una auto-respuesta ya entregados quedan fuera del historial); `AppSecret` es global, así que un tenant con app de Meta propia (flujo que hoy sugiere la UI) será rechazado hasta definir secretos por tenant; la propiedad del `PhoneNumberId` no se verifica contra Meta (un admin podría reclamar primero el ID de otro negocio); faltan pruebas automatizadas de pipeline HTTP y contra SQL Server.
6. AutoMapper 13.0.1 tiene un advisory alto (`GHSA-rvv3-g6hj-g44x`, DoS por recursión) que solo se corrige en 15.1.1+/16.1.1+, versiones con licencia comercial. Hay que decidir entre licenciar, migrar a mapeo manual u otra librería. El riesgo práctico actual es bajo: solo mapea entidades de la base con profundidad acotada. `SQLitePCLRaw.lib.e_sqlite3` 2.1.10 (`GHSA-2m69-gcr7-jv3q`) tampoco tiene versión corregida, pero solo lo usan las pruebas con SQLite.

## Alta

- Movimientos manuales de inventario registrados desde la pantalla antes de la corrección del 2026-10-04: se guardaron con el tipo y el motivo siguientes al elegido. Movieron las existencias en sentido contrario, y las "Salidas" guardadas como `Entry` promediaron su costo como si fueran compras. Falta revisarlos con cada tenant y compensarlos ([procedimiento y consulta de solo lectura](../../database/production/data-repair/20261004_InventoryMovementEnumOffset/README.md)). El dominio no tiene una operación para revaluar el costo promedio.
- El claim `role` es el rol global y no el de la membresía: un `TENANT_ADMIN` tendría todos los permisos en cualquier negocio al que se le agregue como empleado. La autorización v2 ya vive en cada caso de uso, pero el middleware conserva el mapeo por ruta (duplicado) y los controllers sus roles.
- `UserRole.SuperAdmin = 0` es el valor por defecto del enum y de `ApplicationUser.Role`: un rol omitido equivale al privilegio máximo. Cambiar los valores exige migración de datos; mientras tanto, `RoleAssignmentGuardTests` exige que todo request con `UserRole` rechace `SuperAdmin`.
- Sesiones: los access tokens no se pueden revocar antes de expirar (no se valida el security stamp), los refresh tokens se guardan en texto plano sin detección de reuso, el paso de contraseña del login no cuenta intentos fallidos (`lockoutOnFailure: false`; el 2FA sí usa lockout) y el login distingue cuentas sin confirmar o bloqueadas.
- Folios de órdenes y movimientos por conteo (una colisión responde 500); la recepción de compra no tiene idempotency key estable.
- Contratos de error mezclan `ProblemDetails` con objetos `{ error }`; el cliente ya los lee con un solo helper (`src/api/errors.ts`), pero el servidor sigue respondiendo en varias formas y con algunos mensajes de dominio en inglés.
- Controllers no propagan `CancellationToken`.
- Política de contraseña (longitud mínima 6) pendiente de evaluación.
- `OrderItem.Quantity` es entero aunque existen unidades Kg, Litro, Metro y Hora.

## Media

- Controllers grandes; `WhatsAppWebhookController` aún accede al DbContext para resolver tenant y estados en el `POST` (el handshake `GET` ya usa `VerifyWhatsAppWebhookTokenQuery`).
- Componentes frontend de 700–2,450 líneas y `types/index.ts` monolítico.
- `types/index.ts` copia a mano los enums numéricos del dominio. `ClientEnumContractTests` compara sus objetos `as const` con el dominio, pero no genera los tipos ni revisa las uniones literales duplicadas (`PurchaseOrderStatus = 0 | 1 | 2 | 3 | 99` y similares).
- Sin pruebas frontend ni de integración con SQL Server.
- No hay ambiente de staging: cada push a `master` despliega directo a producción, sin slot ni swap para probar antes.
- `PendingModelChangesWarning` se ignora en ejecución; el CI lo valida con `has-pending-model-changes`.
- Auto-migrate/seed solo existe en Development contra un servidor local, pero falta el proceso productivo de migraciones.
- Listados pequeños aún materializan colecciones sin paginación (categorías, AI agents, ubicaciones, kitchen).
- Inicio carga en memoria las órdenes y pagos del rango; con volumen alto conviene un modelo de lectura.
- Consultas con varias colecciones incluidas no usan `AsSplitQuery`.
- `client/README.md` sigue siendo template genérico.
- Un pago puede guardarse sin movimiento de caja si el cajero no tiene caja abierta y hay varias o ninguna en la sucursal, salvo que el negocio active `RequireOpenCashSession`.
- “AI Agent” conserva campos de modelo/temperatura/memoria aunque la implementación es por palabras clave, lo que puede inducir a error.
- `Product.StockQuantity` es informativo: las ventas no lo descuentan.
- Un administrador puede activar cualquier capability desde `PUT /auth/my-tenant`; es un bypass si los módulos dependen del plan contratado.
- Las actions de los workflows se fijan por tag y no por SHA. Los workflows que solo existen en `master` no se revisaron en esta rama.
- Inventario, Caja, Compras y Auditoría filtran solo la página cargada; faltan etiquetas asociadas y nombres accesibles en botones de icono en las pantallas que aún no se rediseñan.

## Resuelto el 2026-10-01

- Secretos fuera del repositorio (user-secrets/variables de entorno), llave JWT obligatoria fuera de Development y auto-migración/seed limitados a Development con servidor local.
- CORS por allowlist, cabeceras de seguridad, HSTS, cookie de refresh `Secure` fuera de Development y OTP fuera de los logs de producción.
- Membresía revalidada en "mi tenant", marketing, hubs y `TenantOrdersController`; interceptor que rechaza `TenantId` ajeno o modificado; exclusiones del middleware por prefijo; rol `SUPERADMIN` corregido; override de `SUPER_ADMIN` auditado; refresh token rotado al cambiar de tenant.
- Topes de cantidades, piezas y paginación.
- Caja: signo por tipo de movimiento, efectivo separado de otros métodos, una caja abierta por tenant, token de concurrencia y fechas del servidor.
- Órdenes: cancelación unificada y bloqueada con pagos, cocina con órdenes del POS, consumo de piezas en el mismo guardado, órdenes mixtas, concurrencia entre piezas.
- Inventario y compras: costo promedio solo por entradas, referencias reservadas, recepciones sin líneas repetidas, recetas que cambian de insumo y tokens de concurrencia.
- Reporte operativo sin IVA en la utilidad, costo de ventas de órdenes reconocidas y mes del tenant.
- Uploads validados por firma y re-codificados, sin metadatos, con tope de píxeles; evidencias limitadas a archivos del tenant.
- Frontend: sesión expirada, refresh entre pestañas, cambio de tenant, SignalR, IVA en el carrito, acciones de estado/cancelación, caja, chat, reset de contraseña, página de inicio por permisos y error de lint `prefer-const`.
- CI con build, tests, migraciones, lint, build y auditoría; despliegue condicionado a tests y lint; imagen Docker con SPA y usuario no root; npm sin vulnerabilidades.
- Fuga entre tenants del dashboard (productos más vendidos) y escalamiento a `SUPER_ADMIN`: se eliminó `POST /api/v1/auth/register`.
- Webhook de WhatsApp con firma obligatoria, índices únicos e idempotencia (migración `AddWhatsAppWebhookUniqueIndexes`).
- Login con 2FA en dos pasos mediante un desafío de un solo uso; ya no bloquea la cuenta.
- La subida de imagen de producto conserva sus campos (`Product.SetImage`).
- Registro de negocio/cliente y confirmación de teléfono atómicos con la estrategia de reintentos; las órdenes solo se vinculan a teléfonos verificados por SMS.
- Borrado de archivos confinado al namespace del tenant.
- Logs sin códigos OTP ni PII de mensajería y con el teléfono del cliente enmascarado fuera de Development.

## Resuelto el 2026-10-04 (Fase 0 del roadmap de producto)

- La pantalla de inventario usa los mismos números que el dominio para tipos y motivos de movimiento (desde 1, `Other = 99`). Ya se registran "Entrada" y "Compra"; cada tipo mueve las existencias en su sentido; filtros y etiquetas coinciden con lo guardado, incluidos los movimientos del sistema. `ClientEnumContractTests` falla si una copia de enum en el cliente difiere del dominio, e `InventoryEnumValuesTests` fija los valores persistidos.
- Cocina en vivo: eventos de `OrderHub` del grupo del tenant, recarga al reconectar, sondeo cada 30 segundos y solo pedidos de alimentos (sin órdenes de servicio); notas del pedido y por artículo visibles.
- Menú deslizable en celular (modo `full` de Hyper, fondo y cierre al navegar) y tableta contraída; montos de caja con signo.
- Permisos alineados: Dashboard para empleados con `ViewReports`, Cocina para cualquier rol con `ManageServiceBoard`, cambio de estado y cancelación para el mostrador con `ManageOrders`, chat visible para empleados con `ManageWhatsApp`; matriz única en el cliente (`src/utils/access.ts`) para menú, rutas, página de entrada y botones.
- Ventas a público en general sin teléfono (migración `AllowWalkInSalesWithoutCustomer`) y 400 en español para teléfonos inválidos.
- Estados en español en la app y en las plantillas de WhatsApp (`OrderStatusLabels`, `src/utils/labels.ts`).
- Marca TienditApp en lugar de los restos de Hyper y FoodPark Digital; encabezado sin campana ni enlaces muertos.
- Límite de peticiones solo en credenciales y códigos; el cliente ya no cierra sesión ante un 429.
- Reenvío del código de teléfono por `userId` y página `/confirm-email` para el enlace del correo.
- Un solo helper de errores en el cliente.
- Desactivar el 2FA exige un código TOTP o de recuperación, con el lockout de Identity y el rate limit `auth`; Perfil pide el código.

Pendientes detectados en la Fase 0:

- El emisor que ven las apps autenticadoras sale del `JwtSettings:Issuer` ("FoodParkDigital"); cambiarlo exige separar el nombre visible del emisor del JWT.
- La confirmación de teléfono no permite corregir el número capturado.
- El tema visual sigue siendo el CSS de Hyper (fase 1: tema propio).

## Resuelto el 2026-10-04 (Fase 1: plataforma)

- API, pruebas e imagen Docker en .NET 10 (LTS) con EF Core y ASP.NET Core 10.0.12 y Serilog.AspNetCore 10, antes del fin de soporte de .NET 9 (10 de noviembre de 2026). El App Service debe usar el stack .NET 10 porque el despliegue es dependiente del framework.
- Solo `master` despliega: `develop` salió de los disparadores y una ejecución manual desde otra rama se omite.

## Resuelto el 2026-10-04 (Fase 1: vender rápido)

- Política de IVA por negocio (precios con IVA incluido o sumado), tasa por producto (16, 8, 0 % o exento) y desglose por tasa calculado en el servidor y guardado por línea (migración `AddTaxPolicyAndCounterSaleClosing`). Los negocios existentes conservan el IVA sumado al 16 %, con los mismos totales.
- Vender cobra en el mismo paso: efectivo con cambio, pago mixto, comprobante de transferencia y ticket impreso o por WhatsApp; la venta y sus pagos se guardan juntos y la API rechaza la venta si el total mostrado ya no coincide.
- Las ventas de mostrador pagadas se cierran solas (configurable) y Inicio reporta ingresos por pagos registrados, ticket promedio por pedido pagado y saldo por cobrar.
- Nuevo shell: menú por trabajo, "Nueva venta" visible, búsqueda global (Ctrl K) con búsqueda en servidor de pedidos, clientes y productos, selector de negocio en cualquier tamaño y barra inferior en celular.
- Tema propio sobre Bootstrap (tokens, fuente, botones táctiles), avisos (toasts), diálogo de confirmación y `DataTable` con búsqueda en servidor en pedidos, clientes, productos, servicios, empleados, proveedores y citas.
- Arranque guiado con módulos, IVA, catálogos de ejemplo por giro y lista de primeros pasos.

Pendientes detectados en la Fase 1:

- El efectivo recibido y el cambio no se guardan en la venta (solo salen en el ticket).
- No hay staging: la decisión fue publicar solo desde `master` sin slot de prueba.
- Una venta sin conexión no se puede registrar; Vender solo conserva el borrador.

## Resuelto el 2026-10-04 (Fase 2: operación confiable)

- Outbox: los efectos durables de los eventos (auditoría y avisos de WhatsApp) se guardan en la misma transacción y los procesa un worker con reintentos, registro por handler y dead-letter (migración `AddOutboxAndWebhookInbox`); bandeja del webhook (`InboxMessages`) y resiliencia HTTP (timeouts y circuit breaker, sin reintentos inmediatos de POST).
- Observabilidad: OpenTelemetry (trazas, métricas y logs) hacia Azure Monitor u OTLP, métricas propias por negocio y `/health/live` y `/health/ready` con el estado de las colas.
- Caja por cajero y sucursal, historial de turnos con diferencias y notas, conteo por denominación y política `RequireOpenCashSession` (migración `CashSessionsPerCashier`).
- Reembolsos parciales con devolución de unidades e insumos, y anulación de ventas pagadas (migración `AddOrderRefunds`, permiso `ProcessRefunds`).
- Campañas, plantillas, bajas BAJA/ALTA y recordatorios de citas por WhatsApp, enviados en segundo plano con historial por mensaje (migración `AddMessagingCampaignsAndReminders`, permiso `ManageCampaigns`).
- Pedido desde el chat (canal WhatsApp en la orden, migración `AddOrderChannel`) y ficha del cliente.
- Autorización v2: regla declarada en cada caso de uso, deny by default con prueba de cobertura, `GET /api/v1/me/access`; cliente de API generado desde OpenAPI con TanStack Query.

Pendientes detectados en la Fase 2:

- El mismo celular mexicano se guarda como `+52…` (capturado) o `+521…` (WhatsApp): la ficha del cliente y las campañas los reconcilian, pero el cliente temporal y la conversación pueden quedar duplicados. Unificar la normalización exige migrar datos y revisar la búsqueda de conversaciones.
- Un envío a Meta que expira puede haberse entregado: el reintento puede duplicar un aviso o un mensaje de campaña (Meta no ofrece idempotencia en envíos).
- Los índices únicos filtrados nuevos (deduplicación de mensajes, caja abierta por cajero, bajas por número) se validan con las migraciones del CI y pruebas InMemory; faltan pruebas de concurrencia contra SQL Server.
- `OutgoingMessages` no tiene retención y las campañas no respetan horario de envío.
- Pantallas aún sin TanStack Query ni tipos generados; conviven tipos manuales (`types/index.ts`) y generados.
- La actualización de pantallas por SignalR sigue siendo best-effort.

## Decisiones humanas pendientes

- Quién confirma con cada tenant los movimientos de inventario desfasados y si se revalúa el costo promedio que alteraron.
- Modelo de autorización definitivo: policies/resource handlers, rol por tenant en el token y alcance administrativo cross-tenant.
- Flujo administrativo y auditado para alta, promoción y baja de `SUPER_ADMIN`; hoy solo existe el seed de Development.
- Responsable legal-contable de la política fiscal (descuentos, IEPS, retenciones y CFDI).
- Cuentas por cobrar, saldo a favor y Wallet (los reembolsos ya existen; los ajustes contables no).
- Estrategia para reconciliar `master`/`develop` y migraciones.
- Cuándo agregar staging con swap antes de producción.
- AutoMapper: licencia comercial o reemplazo.
- Cantidades fraccionarias y control de stock por producto.
- Capabilities ligadas al plan contratado.
