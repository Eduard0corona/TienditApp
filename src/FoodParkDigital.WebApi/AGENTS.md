# Reglas de WebApi

- Este proyecto es composition root. Controllers reciben contratos, envían Commands/Queries y traducen HTTP; no implementan reglas ni acceden a DbContext.
- Mantén rutas bajo `/api/v1`, DTOs públicos, `CreatedAtAction` cuando aplica y `ProblemDetails` consistente.
- Propaga `CancellationToken` desde request a MediatR/providers.
- Declara y prueba autenticación, tenant, permiso, capability y acceso al recurso. No confíes solo en roles ni en headers.
- Los hubs validan membresía/tenant/permiso y solo publican a grupos tenant-scoped.
- Webhooks anónimos requieren firma, replay protection, idempotencia y resolución segura del tenant.
- CORS debe ser allowlist por entorno antes de producción; no amplíes la política permisiva actual.
- Uploads validan tamaño, contenido real, extensión, nombre, autorización y tenant; errores/logs no filtran información sensible.
- Producción no aplica migraciones automáticamente al arrancar.
