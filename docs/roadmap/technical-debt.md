# Deuda técnica

## Crítica antes de producción

1. CORS acepta cualquier origen con credenciales.
2. Aislamiento incompleto: `TenantSettings`/`WhatsAppConfig` no implementan `ITenantScoped`; usos amplios de `IgnoreQueryFilters()` y override `SUPER_ADMIN` sin auditoría suficiente.
3. Falta suite que pruebe aislamiento HTTP, EF, archivos, SignalR y webhooks.
4. Secretos/credenciales de ejemplo están versionados en compose/scripts/configuración de desarrollo; deben aislarse y rotarse según entorno.
5. Uploads no demuestran un pipeline uniforme de magic bytes, autorización de descarga ni malware. El borrado por URL ya está confinado a `{área}/{tenantId}` en almacenamiento local y Azure Blob (rechaza traversal, otros tenants/áreas y URLs ajenas al contenedor) y `POST /api/v1/products` ya no acepta `imageUrl`. Pendiente: validar carpeta y nombre dentro de `UploadAsync`; sanear filas heredadas cuyo `ImageUrl`/`LogoUrl`/`AvatarUrl` apunte fuera de su carpeta (el borrado se rechaza con un warning y el archivo queda huérfano); y un namespace por usuario para avatares, porque el usuario es global y un avatar subido con otra membresía no se borra desde otro tenant.
6. Webhook WhatsApp (actualizado 2026-10-02): la firma `X-Hub-Signature-256` se verifica fail-closed (503 sin `WhatsApp:AppSecret`, 401 sin firma válida; `WhatsApp:AllowUnsignedWebhooks` solo se respeta en Development), `PhoneNumberId` y `ExternalMessageId` tienen índices únicos filtrados (migración `AddWhatsAppWebhookUniqueIndexes`, verificada en SQL Server LocalDB) y las entregas repetidas o concurrentes son idempotentes; si dos primeros mensajes de un número nuevo llegan a la vez, el índice único `(TenantId, CustomerPhone)` deja crear una sola conversación y el otro mensaje se guarda en ella. `WhatsAppConversation` tiene token de concurrencia `rowversion` (migración `AddWhatsAppConversationVersion`, verificada en SQL Server LocalDB con y sin `READ_COMMITTED_SNAPSHOT`; se aplica antes de desplegar el código que la usa): mensajes simultáneos del mismo cliente, auto-respuestas, envíos del negocio y "marcar leído" recargan la conversación y reaplican su cambio, hasta 10 intentos, en lugar de sobrescribir `UnreadCount` y el último mensaje. Cada mensaje del payload se procesa por separado y el `POST` responde 500 si alguno, o la actualización de estados, no se pudo procesar, para que Meta reenvíe la entrega (hasta 7 días); los mensajes ya guardados se ignoran por `ExternalMessageId`. JSON inválido y `PhoneNumberId` desconocido se confirman con 200. Como Meta no firma un timestamp, el replay se mitiga con esa idempotencia. Pendiente: un mensaje que falle siempre se reintenta y registra error durante esos 7 días; si una conversación recibe más de 10 escrituras simultáneas, la que agota los intentos falla (el webhook responde 500 y Meta reenvía, pero un envío del negocio o una auto-respuesta ya entregados quedan fuera del historial); `AppSecret` es global, así que un tenant con app de Meta propia (flujo que hoy sugiere la UI) será rechazado hasta definir secretos por tenant; la propiedad del `PhoneNumberId` no se verifica contra Meta (un admin podría reclamar primero el ID de otro negocio); faltan pruebas automatizadas de pipeline HTTP y contra SQL Server.
7. La validación del 2026-08-10 reportó advisories de severidad alta para AutoMapper 13.0.1 (`GHSA-rvv3-g6hj-g44x`) y SQLitePCLRaw.lib.e_sqlite3 2.1.10 (`GHSA-2m69-gcr7-jv3q`), además de 20 vulnerabilidades npm (2 bajas, 2 moderadas y 16 altas). La remediación de dependencias requiere una tarea separada con pruebas; no debe aplicarse `audit fix` ciegamente.

## Alta

- Eventos se limpian/publican después del commit sin Outbox; side effects pueden perderse o duplicarse.
- Impuesto fijo `subtotal × 16%`, redondeos distintos y sin desglose fiscal persistido.
- `SUPER_ADMIN` y `TENANT_ADMIN` omiten permisos/capabilities en partes del middleware; autorización distribuida por strings de ruta.
- `OrderHub` usa el rol inconsistente `SUPERADMIN`.
- `UserRole.SuperAdmin = 0` es el valor por defecto del enum y de `ApplicationUser.Role`: un rol omitido equivale al privilegio máximo. Cambiar los valores exige migración de datos; mientras tanto, `RoleAssignmentGuardTests` exige que todo request con `UserRole` rechace `SuperAdmin`.
- Stock sin token de concurrencia; folios por conteo y recepción de compra sin idempotency key estable.
- Contratos de error mezclan `ProblemDetails` con objetos `{ error }`.
- Controllers no propagan `CancellationToken`.
- Política de contraseña (longitud mínima 6) pendiente de evaluación.

## Media

- Controllers grandes; `WhatsAppWebhookController` aún accede al DbContext para resolver tenant y estados en el `POST` (el handshake `GET` ya usa `VerifyWhatsAppWebhookTokenQuery`).
- Componentes frontend de 700–2,450 líneas y `types/index.ts` monolítico.
- Sin pruebas frontend, WebApi o integración SQL Server dedicada.
- Producción .NET 9 y tests `net10.0`; CI configura solo .NET 9 y no ejecuta tests/lint.
- `PendingModelChangesWarning` ignorado.
- Auto-migrate/seed solo está limitado a Development, pero falta un proceso productivo documentado/automatizado.
- Listados pequeños aún materializan colecciones sin paginación (categorías, AI agents, ubicaciones, kitchen).
- `client/README.md` sigue siendo template genérico.
- Cálculo de cancelación difiere entre `Cancel()` y transición a `Cancelled`; motivo opcional/historial incompleto.
- Pago puede persistirse sin movimiento de caja cuando no hay sesión abierta; requiere definición contable.
- “AI Agent” conserva campos de modelo/temperatura/memoria aunque la implementación es por palabras clave, lo que puede inducir a error.
- El lint actual falla en `client/src/hooks/useOrderSignalR.ts:64` por `prefer-const` (`startTimeoutId`); el build TypeScript/Vite sí pasa. Es un fallo preexistente y debe corregirse en una tarea de código, no documental.

## Decisiones humanas pendientes

- Modelo de autorización definitivo: policies/resource handlers y alcance administrativo cross-tenant.
- Flujo administrativo y auditado para alta, promoción y baja de `SUPER_ADMIN`; hoy solo existe el seed de Development.
- Política fiscal/redondeo y responsable legal-contable.
- Semántica cuando no hay caja abierta y diseño de ajustes/CxC/Wallet.
- Estrategia para reconciliar `master`/`develop` y migraciones.
- Versión objetivo de SDK/tests y gates de CI.
- Prioridad de Outbox frente a integraciones críticas.
