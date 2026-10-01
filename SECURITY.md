# Seguridad

## Activos y fronteras de confianza

Activos sensibles: credenciales y hashes de Identity, JWT y refresh tokens, códigos 2FA, secretos de WhatsApp/SMS/email/Azure, PII de clientes, archivos y evidencias de pago, importes, existencias, auditoría y datos de cada tenant. Las fronteras principales son navegador/API, JWT/API, SignalR/API, webhooks/API, API/SQL Server, API/almacenamiento y API/proveedores externos.

## Riesgos y controles

### Multi-tenant

El tenant efectivo debe derivarse de identidad autenticada y acceso validado, nunca de un valor de frontend. Toda entidad tenant-owned debe implementar `ITenantScoped`; nuevas entidades deben quedar cubiertas por filtros globales. Operaciones cross-tenant solo pueden ser administrativas, explícitas, autorizadas y auditadas.

Riesgos actuales comprobados:

- `TenantSettings` y `WhatsAppConfig` contienen `TenantId` pero no implementan `ITenantScoped`; por ello el filtro global automático no las cubre y cada consulta debe filtrar por tenant de forma explícita. `OrderItem` y `OrderStatusHistory` no tienen `TenantId`.
- Existen usos de `IgnoreQueryFilters()` en autenticación, administración, webhooks y servicios. Cada uso debe revisarse por alcance y autorización; está prohibido añadirlo como atajo. El del dashboard expone datos de todos los tenants y se corrige en una tarea separada.
- `TenantResolutionMiddleware` permite a `SUPER_ADMIN` sustituir el tenant mediante `X-Tenant-Id` sin comprobar membresía/capability. El uso queda registrado en el log como evento de auditoría, pero falta justificación, límites y persistencia antes de considerarlo un control cross-tenant suficiente.
- El interceptor de guardado rechaza entidades nuevas con un `TenantId` ajeno y cambios de `TenantId`; el middleware revalida la membresía en "mi tenant", marketing y órdenes por tenant, y los hubs la validan al conectar.
- Las pruebas existentes no constituyen una suite completa que demuestre imposibilidad de consultar, modificar, eliminar o descargar datos de otro tenant.

Los grupos SignalR y rutas/nombres de archivos deben incluir aislamiento verificable por tenant. Un rol global, incluido `SUPER_ADMIN`, no concede por sí solo permiso para ignorar ese aislamiento.

### Autenticación y autorización

El backend usa ASP.NET Identity, JWT HMAC, refresh tokens persistidos con rotación/revocación y 2FA. El access token vive en memoria del cliente; el refresh token se entrega como cookie `HttpOnly` (`Secure` fuera de Development) y no se retorna al cliente después del login normal; cambiar de tenant también lo rota. La llave JWT es obligatoria fuera de Development, debe tener al menos 32 bytes y la API rechaza la que estuvo publicada en el repositorio.

Pendiente antes de producción: los access tokens no se pueden revocar antes de expirar (no se valida el security stamp), los refresh tokens se guardan en texto plano sin detección de reuso, no hay bloqueo de cuenta por intentos fallidos y el login distingue cuentas sin confirmar o bloqueadas. El 2FA, que hoy bloquea la cuenta, y el endpoint `POST /auth/register`, que permite asignar `SUPER_ADMIN`, se corrigen en tareas separadas. También deben revisarse expiraciones, protección CSRF/cookie y el uso de tokens SignalR en query string.

La política actual de contraseñas exige mayúscula, minúscula, dígito y longitud 6, sin carácter no alfanumérico. Es una política que debe evaluarse antes de producción. Los guards del frontend solo mejoran UX; cada operación sensible se autoriza en backend por rol, permiso y capability aplicables. Se registran como deuda los controllers que dependen de roles aun cuando existe un modelo granular de permisos.

### CORS y rate limiting

CORS usa la allowlist `Cors:AllowedOrigins` por entorno (vacía por defecto; `http://localhost:5173` en Development). Un origen fuera de la lista no recibe cabeceras CORS. La API agrega `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` y HSTS fuera de Development.

Existen políticas de rate limiting para autenticación y API, pero la política general `api` no se observó aplicada globalmente ni en controllers; se requiere verificación y cobertura de endpoints sensibles, webhooks y hubs.

### Archivos

Las subidas de órdenes detectan el tipo real por firma (JPEG, PNG, WebP o PDF) en lugar de confiar en el nombre o el `ContentType` del cliente. Las fotos y evidencias en imagen se re-codifican a WebP sin metadatos (se elimina la ubicación GPS) y la decodificación de cualquier imagen se limita a JPEG, PNG, WebP y GIF de un solo cuadro, con un tope de 50 megapíxeles contra bombas de descompresión. Una evidencia de pago solo puede referir un archivo subido para el mismo tenant. Las respuestas de `/uploads` llevan `Content-Security-Policy: sandbox` y los PDF se sirven como descarga (también desde Azure Blob).

Pendiente: `/uploads` sigue siendo estático y público, por lo que cualquiera con la URL descarga una evidencia de pago; los PDF no pasan por escaneo de malware. Las descargas deberían repetir autorización y los nombres de almacenamiento mantenerse namespaced por tenant.

### SignalR, WhatsApp e integraciones

Los hubs requieren autenticación, validan al conectar membresía activa y permiso (órdenes: gestionar órdenes, tablero o ver reportes; chat: WhatsApp), agrupan por tenant y se cierran cuando expira el token. Webhooks de WhatsApp son anónimos por diseño y usan consultas sin filtros: la firma/origen, replay protection, asociación exacta a tenant e idempotencia son requisitos críticos; la verificación de firma obligatoria se corrige en una tarea separada.

Los access tokens de integraciones deben cifrarse o almacenarse en un secret store, nunca exponerse en DTOs/logs. No registrar tokens, contraseñas, códigos 2FA, cuerpos con PII innecesaria ni información financiera sensible.

### Logs y auditoría

Serilog y `AuditLog` existen, pero los logs deben usar minimización y redacción. La auditoría de cambios sensibles debe incluir actor, tenant, acción, objetivo, resultado y correlación sin guardar secretos. El middleware actual registra excepciones con su mensaje; revisa que mensajes internos o de proveedores no filtren datos. El cuerpo de un SMS (que puede ser un OTP) solo se registra en Development cuando no hay proveedor configurado, y la configuración de envío de OTP solo se toma de un `SUPER_ADMIN` activo.

## Secretos

Configura connection strings, JWT, WhatsApp, SMS, email y Azure mediante variables de entorno, plataforma de secretos o user-secrets. El repositorio ya no contiene credenciales, pero cadenas de conexión de Azure SQL y el secreto JWT anterior siguen en el historial de git: rótalos y revisa el firewall de Azure SQL. Los valores de `docker-compose.yml` son ejemplos inseguros para producción.

Las dependencias deben revisarse de forma continua; el CI audita las dependencias npm de producción en cada PR. Al 2026-10-01 npm no reporta vulnerabilidades y se eliminó el paquete de pruebas que traía SQLitePCLRaw. AutoMapper 13.0.1 conserva un advisory alto (`GHSA-rvv3-g6hj-g44x`) cuya corrección solo existe en versiones con licencia comercial; requiere una decisión. No uses actualizaciones automáticas de ruptura sin revisión.

## Reporte responsable

No abras un issue público con detalles explotables o datos reales. Contacta de forma privada al mantenedor del repositorio, incluye impacto, reproducción mínima, versión/commit afectado y propuesta de mitigación. Da tiempo razonable para corregir antes de divulgar. Si no existe un canal privado publicado, solicita uno al propietario sin revelar el detalle técnico.
