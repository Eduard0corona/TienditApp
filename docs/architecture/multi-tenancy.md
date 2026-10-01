# Multi-tenancy

## Estado actual

Se usa una base SQL Server compartida. `ApplicationDbContext` recorre el modelo y agrega un filtro `TenantId == CurrentTenantId` a cada entidad que implementa `ITenantScoped`. `TenantSaveChangesInterceptor` intenta completar un `TenantId` vacío en entidades nuevas. `TenantResolutionMiddleware` toma `tenant_id` del JWT, valida acceso para rutas reconocidas y configura `ICurrentTenantService`.

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

1. `TenantSettings` y `WhatsAppConfig` tienen `TenantId` sin `ITenantScoped`; el filtro automático no se aplica.
2. Hay múltiples `IgnoreQueryFilters()` en autenticación, empleados, tenants, webhooks e integraciones. Algunos escenarios ocurren antes de resolver tenant, pero la cobertura completa no está probada.
3. `SUPER_ADMIN` puede cambiar tenant mediante `X-Tenant-Id` sin validar membresía ni capability y sin auditoría visible en el middleware.
4. El middleware decide si una ruta es tenant-scoped mediante una lista manual; una ruta nueva puede quedar fuera por omisión.
5. Los hubs aceptan `tenant_id` del token y el middleware omite `/hubs/`; no se observa revalidación de membresía activa al conectar.
6. No existe una suite end-to-end de aislamiento para API, archivos, SignalR y webhooks.
7. `TenantSaveChangesInterceptor` no rechaza de forma explícita una entidad nueva con un `TenantId` ajeno ya informado; el filtro de lectura no sustituye validación de escritura.

Estos puntos son deuda/riesgo. Esta documentación no los corrige.
