# Deuda técnica

Actualizada el 2026-10-01 tras la auditoría del proyecto y sus correcciones.

## Crítica antes de producción

1. Credenciales expuestas: cadenas de conexión de Azure SQL y el secreto JWT estuvieron versionados y siguen en el historial de git. El código ya no los contiene y la API rechaza el secreto publicado, pero hay que rotarlos, revisar el firewall de Azure SQL y verificar si la cuenta SUPER_ADMIN sembrada con la contraseña anterior existe en la base remota. Reescribir el historial es destructivo y requiere coordinación.
2. Aislamiento incompleto: `TenantSettings`/`WhatsAppConfig` no implementan `ITenantScoped`; `OrderItem`/`OrderStatusHistory` no tienen `TenantId`; quedan usos de `IgnoreQueryFilters()` por revisar; el override `X-Tenant-Id` de `SUPER_ADMIN` solo se audita en el log.
3. Falta una suite que pruebe aislamiento HTTP, EF contra SQL Server, archivos, SignalR y webhooks.
4. Archivos: el contenido se valida por firma, las fotos se re-codifican y el borrado por URL está confinado a `{área}/{tenantId}` en almacenamiento local y Azure Blob (rechaza traversal, otros tenants/áreas y URLs ajenas al contenedor). Pendiente: `/uploads` es estático y público (las evidencias de pago se descargan sin autorización), los PDF no pasan por un escaneo de malware, `UploadAsync` no valida carpeta y nombre, hay que sanear filas heredadas cuyo `ImageUrl`/`LogoUrl`/`AvatarUrl` apunte fuera de su carpeta (el borrado se rechaza con un warning y el archivo queda huérfano) y falta un namespace por usuario para avatares (el usuario es global y un avatar subido con otra membresía no se borra desde otro tenant).
5. Webhook WhatsApp (actualizado 2026-10-02): la firma `X-Hub-Signature-256` se verifica fail-closed (503 sin `WhatsApp:AppSecret`, 401 sin firma válida; `WhatsApp:AllowUnsignedWebhooks` solo se respeta en Development), `PhoneNumberId` y `ExternalMessageId` tienen índices únicos filtrados (migración `AddWhatsAppWebhookUniqueIndexes`, verificada en SQL Server LocalDB) y las entregas repetidas o concurrentes son idempotentes; si dos primeros mensajes de un número nuevo llegan a la vez, el índice único `(TenantId, CustomerPhone)` deja crear una sola conversación y el otro mensaje se guarda en ella. Lo mismo ocurre si el negocio escribe a un número nuevo mientras llega su primer mensaje, o si dos envíos del negocio coinciden: `POST /api/v1/whatsapp/chat/send` guarda el mensaje, ya entregado, en la conversación ganadora de su tenant en lugar de responder 500 (verificado en SQL Server LocalDB con y sin `READ_COMMITTED_SNAPSHOT`). `WhatsAppConversation` tiene token de concurrencia `rowversion` (migración `AddWhatsAppConversationVersion`, verificada en SQL Server LocalDB con y sin `READ_COMMITTED_SNAPSHOT`; se aplica antes de desplegar el código que la usa): mensajes simultáneos del mismo cliente, auto-respuestas, envíos del negocio y "marcar leído" recargan la conversación y reaplican su cambio, hasta 10 intentos, en lugar de sobrescribir `UnreadCount` y el último mensaje. Cada mensaje del payload se procesa por separado y el `POST` responde 500 si alguno, o la actualización de estados, no se pudo procesar, para que Meta reenvíe la entrega (hasta 7 días); los mensajes ya guardados se ignoran por `ExternalMessageId`. JSON inválido y `PhoneNumberId` desconocido se confirman con 200. Como Meta no firma un timestamp, el replay se mitiga con esa idempotencia. Pendiente: un mensaje que falle siempre se reintenta y registra error durante esos 7 días; el envío del negocio llama a Meta antes de guardar y no hay Outbox, así que si el guardado falla por otra causa el mensaje entregado queda fuera del historial y la API responde 500; si una conversación recibe más de 10 escrituras simultáneas, la que agota los intentos falla (el webhook responde 500 y Meta reenvía, pero un envío del negocio o una auto-respuesta ya entregados quedan fuera del historial); `AppSecret` es global, así que un tenant con app de Meta propia (flujo que hoy sugiere la UI) será rechazado hasta definir secretos por tenant; la propiedad del `PhoneNumberId` no se verifica contra Meta (un admin podría reclamar primero el ID de otro negocio); faltan pruebas automatizadas de pipeline HTTP y contra SQL Server.
6. AutoMapper 13.0.1 tiene un advisory alto (`GHSA-rvv3-g6hj-g44x`, DoS por recursión) que solo se corrige en 15.1.1+/16.1.1+, versiones con licencia comercial. Hay que decidir entre licenciar, migrar a mapeo manual u otra librería. El riesgo práctico actual es bajo: solo mapea entidades de la base con profundidad acotada. `SQLitePCLRaw.lib.e_sqlite3` 2.1.10 (`GHSA-2m69-gcr7-jv3q`) tampoco tiene versión corregida, pero solo lo usan las pruebas con SQLite.
7. .NET 9 dejó de tener soporte en mayo de 2026: planear la migración de la API a .NET 10 (LTS) junto con EF Core y ASP.NET Core 10.

## Alta

- Eventos se limpian/publican después del commit sin Outbox; side effects pueden perderse o duplicarse.
- Impuesto fijo `subtotal × 16%`, sin desglose fiscal persistido y sin decidir si los precios del catálogo incluyen IVA (si lo incluyen, se sobrecobra).
- `SUPER_ADMIN` y `TENANT_ADMIN` omiten permisos/capabilities en partes del middleware; el claim `role` es el rol global y no el de la membresía; autorización distribuida por strings de ruta.
- `UserRole.SuperAdmin = 0` es el valor por defecto del enum y de `ApplicationUser.Role`: un rol omitido equivale al privilegio máximo. Cambiar los valores exige migración de datos; mientras tanto, `RoleAssignmentGuardTests` exige que todo request con `UserRole` rechace `SuperAdmin`.
- Sesiones: los access tokens no se pueden revocar antes de expirar (no se valida el security stamp), los refresh tokens se guardan en texto plano sin detección de reuso, el paso de contraseña del login no cuenta intentos fallidos (`lockoutOnFailure: false`; el 2FA sí usa lockout) y el login distingue cuentas sin confirmar o bloqueadas.
- Folios de órdenes y movimientos por conteo (una colisión responde 500); la recepción de compra no tiene idempotency key estable.
- No existen reembolsos: una orden con pagos no se puede cancelar.
- Contratos de error mezclan `ProblemDetails` con objetos `{ error }`.
- Controllers no propagan `CancellationToken`.
- Política de contraseña (longitud mínima 6) pendiente de evaluación.
- `OrderItem.Quantity` es entero aunque existen unidades Kg, Litro, Metro y Hora.

## Media

- Controllers grandes; `WhatsAppWebhookController` aún accede al DbContext para resolver tenant y estados en el `POST` (el handshake `GET` ya usa `VerifyWhatsAppWebhookTokenQuery`).
- Componentes frontend de 700–2,450 líneas y `types/index.ts` monolítico.
- Sin pruebas frontend ni de integración con SQL Server.
- Producción .NET 9 y tests `net10.0` (el CI instala ambos SDK); no hay `global.json`.
- `PendingModelChangesWarning` se ignora en ejecución; el CI lo valida con `has-pending-model-changes`.
- Auto-migrate/seed solo existe en Development contra un servidor local, pero falta el proceso productivo de migraciones.
- Listados pequeños aún materializan colecciones sin paginación (categorías, AI agents, ubicaciones, kitchen).
- El dashboard carga en memoria todas las órdenes del rango y su ticket promedio incluye órdenes canceladas y pendientes.
- Consultas con varias colecciones incluidas no usan `AsSplitQuery`.
- `client/README.md` sigue siendo template genérico.
- Pago puede persistirse sin movimiento de caja cuando no hay sesión abierta; solo hay una caja abierta por tenant, no por sucursal.
- “AI Agent” conserva campos de modelo/temperatura/memoria aunque la implementación es por palabras clave, lo que puede inducir a error.
- `Product.StockQuantity` es informativo: las ventas no lo descuentan.
- Un administrador puede activar cualquier capability desde `PUT /auth/my-tenant`; es un bypass si los módulos dependen del plan contratado.
- Las actions de los workflows se fijan por tag y no por SHA. Los workflows que solo existen en `master` no se revisaron en esta rama.
- La búsqueda de `DataTable` filtra solo la página cargada; faltan etiquetas asociadas y nombres accesibles en botones de icono.

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

## Decisiones humanas pendientes

- Modelo de autorización definitivo: policies/resource handlers, rol por tenant en el token y alcance administrativo cross-tenant.
- Flujo administrativo y auditado para alta, promoción y baja de `SUPER_ADMIN`; hoy solo existe el seed de Development.
- Política fiscal/redondeo, si los precios incluyen IVA y responsable legal-contable.
- Semántica cuando no hay caja abierta, caja por sucursal y diseño de reembolsos/ajustes/CxC/Wallet.
- Estrategia para reconciliar `master`/`develop` y migraciones.
- Versión objetivo de .NET (9 ya sin soporte) y del SDK de tests.
- Prioridad de Outbox frente a integraciones críticas.
- AutoMapper: licencia comercial o reemplazo.
- Cantidades fraccionarias y control de stock por producto.
- Capabilities ligadas al plan contratado.
