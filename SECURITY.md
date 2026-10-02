# Seguridad

## Activos y fronteras de confianza

Activos sensibles: credenciales y hashes de Identity, JWT y refresh tokens, códigos 2FA, secretos de WhatsApp/SMS/email/Azure, PII de clientes, archivos y evidencias de pago, importes, existencias, auditoría y datos de cada tenant. Las fronteras principales son navegador/API, JWT/API, SignalR/API, webhooks/API, API/SQL Server, API/almacenamiento y API/proveedores externos.

## Riesgos y controles

### Multi-tenant

El tenant efectivo debe derivarse de identidad autenticada y acceso validado, nunca de un valor de frontend. Toda entidad tenant-owned debe implementar `ITenantScoped`; nuevas entidades deben quedar cubiertas por filtros globales. Operaciones cross-tenant solo pueden ser administrativas, explícitas, autorizadas y auditadas.

Riesgos actuales comprobados:

- `TenantSettings` y `WhatsAppConfig` contienen `TenantId` pero no implementan `ITenantScoped`; por ello el filtro global automático no las cubre.
- Existen usos de `IgnoreQueryFilters()` en autenticación, administración, webhooks y servicios. Cada uso debe revisarse por alcance y autorización; está prohibido añadirlo como atajo.
- `TenantResolutionMiddleware` permite a `SUPER_ADMIN` sustituir el tenant mediante `X-Tenant-Id` y omite la comprobación de membresía/capability. Debe endurecerse y auditarse antes de considerarlo un control cross-tenant suficiente.
- Las pruebas existentes no constituyen una suite completa que demuestre imposibilidad de consultar, modificar, eliminar o descargar datos de otro tenant.

Los grupos SignalR y rutas/nombres de archivos deben incluir aislamiento verificable por tenant. Un rol global, incluido `SUPER_ADMIN`, no concede por sí solo permiso para ignorar ese aislamiento.

### Autenticación y autorización

El backend usa ASP.NET Identity, JWT HMAC, refresh tokens persistidos con rotación/revocación y 2FA. El access token vive en memoria del cliente; el refresh token se entrega como cookie `HttpOnly` y no se retorna al cliente después del login normal ni del login con 2FA. Antes de producción deben revisarse expiraciones, revocación, protección CSRF/cookie, claves, algoritmos y el uso de tokens SignalR en query string.

El login con 2FA tiene dos pasos y no usa cookies de Identity. `POST /api/v1/auth/login` valida la contraseña y, si el usuario tiene 2FA, solo devuelve `twoFactorChallengeToken`: un JWT de 5 minutos firmado con una clave derivada (HKDF) del secreto JWT y con audiencia propia, por lo que no se acepta como access token. Su huella (`jti` + security stamp) se guarda en `AspNetUserTokens`; así es de un solo uso, un login posterior lo reemplaza y deja de valer si cambia el security stamp. `POST /api/v1/twofactor/login` exige ese desafío más un TOTP o un código de recuperación de un solo uso; cada código fallido cuenta con el lockout de Identity (por defecto 5 intentos y 5 minutos) y el bloqueo invalida el desafío. Deuda: el paso de contraseña usa `lockoutOnFailure: false`, así que sus intentos solo los limita el rate limit `auth`.

La política actual de contraseñas exige mayúscula, minúscula, dígito y longitud 6, sin carácter no alfanumérico. Es una política que debe evaluarse antes de producción. Los guards del frontend solo mejoran UX; cada operación sensible se autoriza en backend por rol, permiso y capability aplicables. Se registran como deuda los controllers que dependen de roles aun cuando existe un modelo granular de permisos.

El alta de usuarios por API está acotada por caso de uso: `register/business` solo crea `TENANT_ADMIN`, `register/customer` solo `CUSTOMER` y `/api/v1/employees` limita el rol de aplicación a `TenantAdmin`, `Staff` o `Kitchen`. Se eliminó `POST /api/v1/auth/register`, que aceptaba cualquier `UserRole` (incluido `SuperAdmin`) de un `TENANT_ADMIN`. No existe camino de API para crear o promover `SUPER_ADMIN`; solo lo crea el seed de Development. `UserRole.SuperAdmin` vale `0`, el valor por defecto del enum y de `ApplicationUser.Role`, así que un rol omitido equivale al privilegio máximo: todo request que reciba `UserRole` debe rechazar `SuperAdmin` en su validador, y `RoleAssignmentGuardTests` lo verifica.

### CORS y rate limiting

La política `AllowAll` acepta cualquier origen mediante `SetIsOriginAllowed(_ => true)` y además habilita credenciales. Es un riesgo crítico y bloqueante antes de producción. Debe sustituirse por una allowlist de orígenes por entorno.

Existen políticas de rate limiting para autenticación y API, pero la política general `api` no se observó aplicada globalmente ni en controllers; se requiere verificación y cobertura de endpoints sensibles, webhooks y hubs.

### Archivos

Los uploads actuales validan parcialmente tamaño, extensión y `ContentType`, y las imágenes se decodifican con ImageSharp. Toda carga debe validar tamaño, tipo real/magic bytes, extensión, nombre normalizado, autorización, tenant, destino y malware cuando aplique. Los nombres de almacenamiento deben estar namespaced por tenant y las descargas deben repetir autorización; no confíes en el nombre o MIME enviado por el cliente. El borrado por URL recibe la carpeta `{área}/{tenantId}` derivada del tenant dueño y ambos providers rechazan rutas fuera de ella; las URLs de imagen solo las fija el endpoint de upload correspondiente.

### SignalR, WhatsApp e integraciones

Los hubs requieren autenticación y agrupan por claim `tenant_id`; deben validar pertenencia efectiva y permiso al conectar/unirse. Hay una inconsistencia comprobada entre `SUPERADMIN` en `OrderHub` y el rol `SUPER_ADMIN` usado en el resto del sistema. Webhooks de WhatsApp son anónimos por diseño y usan consultas sin filtros: la firma/origen, replay protection, asociación exacta a tenant e idempotencia son requisitos críticos.

Los access tokens de integraciones deben cifrarse o almacenarse en un secret store, nunca exponerse en DTOs/logs. No registrar tokens, contraseñas, códigos 2FA, cuerpos con PII innecesaria ni información financiera sensible.

### Logs y auditoría

Serilog y `AuditLog` existen, pero los logs deben usar minimización y redacción. La auditoría de cambios sensibles debe incluir actor, tenant, acción, objetivo, resultado y correlación sin guardar secretos. SQL Server copia valores de las filas en sus mensajes de error (claves duplicadas, valores truncados). Por eso, fuera de Development, ni el middleware de excepciones, ni el webhook de WhatsApp, ni EF Core registran mensajes de errores de base de datos: quedan el tipo, el número de error SQL, el `traceId` y el stack trace. El middleware registra las demás excepciones con su mensaje; revisa que mensajes internos o de proveedores no filtren datos.

## Secretos

Configura connection strings, JWT, WhatsApp, SMS, email y Azure mediante variables de entorno, plataforma de secretos o user-secrets. Los valores de desarrollo y `docker-compose.yml` son ejemplos inseguros para producción. Rota cualquier secreto que haya sido expuesto.

Las dependencias deben revisarse de forma continua. La validación documental del 2026-08-10 encontró advisories altos en AutoMapper/SQLitePCLRaw y 20 vulnerabilidades npm; prioriza análisis de explotabilidad, actualización compatible y regresión en una tarea dedicada. No uses actualizaciones automáticas de ruptura sin revisión.

## Reporte responsable

No abras un issue público con detalles explotables o datos reales. Contacta de forma privada al mantenedor del repositorio, incluye impacto, reproducción mínima, versión/commit afectado y propuesta de mitigación. Da tiempo razonable para corregir antes de divulgar. Si no existe un canal privado publicado, solicita uno al propietario sin revelar el detalle técnico.
