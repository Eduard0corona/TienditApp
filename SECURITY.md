# Seguridad

## Activos y fronteras de confianza

Activos sensibles: credenciales y hashes de Identity, JWT y refresh tokens, códigos 2FA, secretos de WhatsApp/SMS/email/Azure, PII de clientes, archivos y evidencias de pago, importes, existencias, auditoría y datos de cada tenant. Las fronteras principales son navegador/API, JWT/API, SignalR/API, webhooks/API, API/SQL Server, API/almacenamiento y API/proveedores externos.

## Riesgos y controles

### Multi-tenant

El tenant efectivo debe derivarse de identidad autenticada y acceso validado, nunca de un valor de frontend. Toda entidad tenant-owned debe implementar `ITenantScoped`; nuevas entidades deben quedar cubiertas por filtros globales. Operaciones cross-tenant solo pueden ser administrativas, explícitas, autorizadas y auditadas.

Riesgos actuales comprobados:

- `TenantSettings` y `WhatsAppConfig` contienen `TenantId` pero no implementan `ITenantScoped`; por ello el filtro global automático no las cubre y cada consulta debe filtrar por tenant de forma explícita. `OrderItem` y `OrderStatusHistory` no tienen `TenantId`.
- Existen usos de `IgnoreQueryFilters()` en autenticación, administración, webhooks y servicios. Cada uso debe revisarse por alcance y autorización; está prohibido añadirlo como atajo. El del dashboard, que exponía los productos más vendidos de todos los tenants, ya se corrigió.
- `TenantResolutionMiddleware` permite a `SUPER_ADMIN` sustituir el tenant mediante `X-Tenant-Id` sin comprobar membresía/capability. El uso queda registrado en el log como evento de auditoría, pero falta justificación, límites y persistencia antes de considerarlo un control cross-tenant suficiente.
- El interceptor de guardado rechaza entidades nuevas con un `TenantId` ajeno y cambios de `TenantId`; el middleware revalida la membresía en "mi tenant", marketing y órdenes por tenant, y los hubs la validan al conectar.
- Las pruebas existentes no constituyen una suite completa que demuestre imposibilidad de consultar, modificar, eliminar o descargar datos de otro tenant.

Los grupos SignalR y rutas/nombres de archivos deben incluir aislamiento verificable por tenant. Un rol global, incluido `SUPER_ADMIN`, no concede por sí solo permiso para ignorar ese aislamiento.

### Autenticación y autorización

El backend usa ASP.NET Identity, JWT HMAC, refresh tokens persistidos con rotación/revocación y 2FA. El access token vive en memoria del cliente; el refresh token se entrega como cookie `HttpOnly` (`Secure` fuera de Development) y no se retorna al cliente después del login normal ni del login con 2FA; cambiar de tenant también lo rota. La llave JWT es obligatoria fuera de Development, debe tener al menos 32 bytes y la API rechaza la que estuvo publicada en el repositorio.

El login con 2FA tiene dos pasos y no usa cookies de Identity. `POST /api/v1/auth/login` valida la contraseña y, si el usuario tiene 2FA, solo devuelve `twoFactorChallengeToken`: un JWT de 5 minutos firmado con una clave derivada (HKDF) del secreto JWT y con audiencia propia, por lo que no se acepta como access token. Su huella (`jti` + security stamp) se guarda en `AspNetUserTokens`; así es de un solo uso, un login posterior lo reemplaza y deja de valer si cambia el security stamp. `POST /api/v1/twofactor/login` exige ese desafío más un TOTP o un código de recuperación de un solo uso; cada código fallido cuenta con el lockout de Identity (por defecto 5 intentos y 5 minutos) y el bloqueo invalida el desafío.

Desactivar el 2FA (`POST /api/v1/twofactor/disable`, body `{ code, useRecoveryCode }`) exige el segundo factor vigente: un TOTP o un código de recuperación, que se canjea y deja de servir. Una sesión robada ya no basta para quitarlo. Los códigos fallidos cuentan con el mismo lockout de Identity, la acción tiene el rate limit `auth` y, al desactivar, se descarta la clave del autenticador. El código de recuperación permite que quien perdió el teléfono quite el 2FA y lo vuelva a activar. Sin 2FA activo la petición responde 400 y no modifica nada. No se pide la contraseña actual.

Pendiente antes de producción: el paso de contraseña usa `lockoutOnFailure: false` (sus intentos solo los limita el rate limit `auth`), los access tokens no se pueden revocar antes de expirar (no se valida el security stamp), los refresh tokens se guardan en texto plano sin detección de reuso y el login distingue cuentas sin confirmar o bloqueadas. También deben revisarse expiraciones, protección CSRF/cookie y el uso de tokens SignalR en query string.

La política actual de contraseñas exige mayúscula, minúscula, dígito y longitud 6, sin carácter no alfanumérico. Es una política que debe evaluarse antes de producción. Los guards del frontend solo mejoran UX; cada operación sensible se autoriza en backend por rol, permiso y capability aplicables. Se registran como deuda los controllers que dependen de roles aun cuando existe un modelo granular de permisos.

El alta de usuarios por API está acotada por caso de uso: `register/business` solo crea `TENANT_ADMIN`, `register/customer` solo `CUSTOMER` y `/api/v1/employees` limita el rol de aplicación a `TenantAdmin`, `Staff` o `Kitchen`. Se eliminó `POST /api/v1/auth/register`, que aceptaba cualquier `UserRole` (incluido `SuperAdmin`) de un `TENANT_ADMIN`. No existe camino de API para crear o promover `SUPER_ADMIN`; solo lo crea el seed de Development. `UserRole.SuperAdmin` vale `0`, el valor por defecto del enum y de `ApplicationUser.Role`, así que un rol omitido equivale al privilegio máximo: todo request que reciba `UserRole` debe rechazar `SuperAdmin` en su validador, y `RoleAssignmentGuardTests` lo verifica.

### Datos en el dispositivo

Vender guarda en el navegador (IndexedDB `tienditapp-offline`) el último catálogo del negocio y las ventas cobradas sin conexión hasta que el servidor las recibe, incluidos el nombre y teléfono del cliente si se capturaron; no guarda tokens. Cada venta pendiente queda ligada al negocio y al usuario que la cobró, y solo esa sesión la envía, así una venta no puede registrarse en otro negocio ni a nombre de otro cajero (el servidor además resuelve productos y sucursal con el filtro del tenant de la sesión). Cerrar sesión no borra las pendientes para no perder cobros: en un equipo compartido conviene enviarlas o descartarlas antes. La clave de idempotencia es un UUID aleatorio por venta y el índice único es por tenant.

### CORS y rate limiting

CORS usa la allowlist `Cors:AllowedOrigins` por entorno (vacía por defecto; `http://localhost:5173` en Development). Un origen fuera de la lista no recibe cabeceras CORS. La API agrega `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` y HSTS fuera de Development.

La política `auth` (10 solicitudes por minuto por IP fuera de Development) se aplica por acción solo a endpoints de credenciales y códigos: login, registro, confirmación y reenvío de correo y teléfono, olvido y restablecimiento de contraseña, cambio de contraseña y los códigos de 2FA al confirmar el alta, al iniciar sesión y al desactivarlo (`AuthRateLimitingTests`). Refresh, logout, perfil y cambio de negocio no tienen límite: corren en cada pantalla de cada dispositivo, a menudo detrás de una sola IP, y el cliente cerraba la sesión al recibir 429; ahora el cliente solo la cierra si el servidor rechaza el refresh (400, 401 o 403). Falta limitar también por cuenta (lockout de Identity) y la política general `api` sigue definida sin aplicarse; webhooks y hubs requieren su propia revisión.

### Archivos

Las subidas de órdenes detectan el tipo real por firma (JPEG, PNG, WebP o PDF) en lugar de confiar en el nombre o el `ContentType` del cliente. Las fotos y evidencias en imagen se re-codifican a WebP sin metadatos (se elimina la ubicación GPS) y la decodificación de cualquier imagen se limita a JPEG, PNG, WebP y GIF de un solo cuadro, con un tope de 50 megapíxeles contra bombas de descompresión. Una evidencia de pago solo puede referir un archivo subido para el mismo tenant. Las respuestas de `/uploads` llevan `Content-Security-Policy: sandbox` y los PDF se sirven como descarga (también desde Azure Blob). El borrado por URL recibe la carpeta `{área}/{tenantId}` derivada del tenant dueño y ambos providers rechazan rutas fuera de ella; las URLs de imagen solo las fija el endpoint de upload correspondiente.

Pendiente: `/uploads` sigue siendo estático y público, por lo que cualquiera con la URL descarga una evidencia de pago; los PDF no pasan por escaneo de malware. Las descargas deberían repetir autorización.

### SignalR, WhatsApp e integraciones

Los hubs requieren autenticación, validan al conectar membresía activa y permiso (órdenes: gestionar órdenes, tablero o ver reportes; chat: WhatsApp), agrupan por tenant y se cierran cuando expira el token. Webhooks de WhatsApp son anónimos por diseño y usan consultas sin filtros: la firma/origen, replay protection, asociación exacta a tenant e idempotencia son requisitos críticos. La firma `X-Hub-Signature-256` se verifica fail-closed, `PhoneNumberId` y `ExternalMessageId` son únicos y las entregas repetidas son idempotentes; lo pendiente (un `AppSecret` global y la propiedad del `PhoneNumberId`) está en la deuda técnica.

Los access tokens de integraciones deben cifrarse o almacenarse en un secret store, nunca exponerse en DTOs/logs. No registrar tokens, contraseñas, códigos 2FA, cuerpos con PII innecesaria ni información financiera sensible.

### Logs y auditoría

Serilog y `AuditLog` existen, pero los logs deben usar minimización y redacción. La auditoría de cambios sensibles debe incluir actor, tenant, acción, objetivo, resultado y correlación sin guardar secretos. SQL Server copia valores de las filas en sus mensajes de error (claves duplicadas, valores truncados). Por eso, fuera de Development, ni el middleware de excepciones, ni el webhook de WhatsApp, ni EF Core registran mensajes de errores de base de datos: quedan el tipo, el número de error SQL, el `traceId` y el stack trace. El middleware registra las demás excepciones con su mensaje; revisa que mensajes internos o de proveedores no filtren datos. El cuerpo de un SMS (que puede ser un OTP) solo se registra en Development cuando no hay proveedor configurado, y la configuración de envío de OTP solo se toma de un `SUPER_ADMIN` activo.

## Secretos

Configura connection strings, JWT, WhatsApp, SMS, email y Azure mediante variables de entorno, plataforma de secretos o user-secrets. El repositorio ya no contiene credenciales, pero cadenas de conexión de Azure SQL y el secreto JWT anterior siguen en el historial de git: rótalos y revisa el firewall de Azure SQL. Los valores de `docker-compose.yml` son ejemplos inseguros para producción.

Las dependencias deben revisarse de forma continua; el CI audita las dependencias npm de producción en cada PR. Al 2026-10-01 npm no reporta vulnerabilidades. `SQLitePCLRaw.lib.e_sqlite3` 2.1.10 (`GHSA-2m69-gcr7-jv3q`) no tiene versión corregida y solo entra por las pruebas que usan SQLite; no forma parte de la aplicación publicada. AutoMapper 13.0.1 conserva un advisory alto (`GHSA-rvv3-g6hj-g44x`) cuya corrección solo existe en versiones con licencia comercial; requiere una decisión. No uses actualizaciones automáticas de ruptura sin revisión.

## Reporte responsable

No abras un issue público con detalles explotables o datos reales. Contacta de forma privada al mantenedor del repositorio, incluye impacto, reproducción mínima, versión/commit afectado y propuesta de mitigación. Da tiempo razonable para corregir antes de divulgar. Si no existe un canal privado publicado, solicita uno al propietario sin revelar el detalle técnico.
