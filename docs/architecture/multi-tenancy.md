# Multi-tenancy

## Estado actual

Se usa una base SQL Server compartida. `ApplicationDbContext` recorre el modelo y agrega un filtro `TenantId == CurrentTenantId` a cada entidad que implementa `ITenantScoped`. `TenantResolutionMiddleware` toma `tenant_id` del JWT, valida acceso para rutas reconocidas y configura `ICurrentTenantService`.

- `TenantSaveChangesInterceptor` completa un `TenantId` vacío en entidades nuevas, rechaza un `TenantId` ajeno (salvo el de un tenant creado en la misma unidad de trabajo, como en el alta de negocio) y rechaza cambiar el `TenantId` de una entidad existente (`TenantMismatchException`, 403).
- Las exclusiones del middleware se comparan por prefijo y las rutas de "mi tenant" y marketing son tenant-scoped: un miembro desactivado ya no puede usarlas con un token vigente.
- `TenantOrdersController` rechaza un tenant de ruta distinto del tenant validado.
- Los hubs validan al conectar membresía activa y permiso (`OrderHub`: gestionar órdenes, tablero o ver reportes; `ChatHub`: WhatsApp) y se cierran cuando expira el token.
- El override `X-Tenant-Id` de `SUPER_ADMIN` queda registrado como evento de auditoría en el log (usuario, tenant, tenant del token, método y ruta).

Identity es global; `TenantUser` vincula usuario y tenant. Capabilities se leen de `TenantSettings`. SignalR agrupa conexiones por el claim de tenant. Muchas claves e índices incluyen `TenantId`.

## Reglas obligatorias

- Toda entidad propiedad de un tenant implementa `ITenantScoped`.
- `TenantId` es obligatorio y no cambia después de crear la entidad.
- Una nueva entidad tenant-scoped debe quedar bajo filtro global y tener pruebas de modelo/aislamiento.
- El frontend nunca es fuente confiable del tenant. El tenant efectivo viene de identidad autenticada y acceso validado.
- Está prohibido usar `IgnoreQueryFilters()` como solución rápida. Cada uso aceptado debe filtrar explícitamente por tenant/usuario, justificar el escenario pre-tenant o administrativo y probar aislamiento.
- Toda operación cross-tenant es administrativa, explícita, autorizada y auditada.
- Las pruebas cubren leer, modificar, eliminar y descargar desde un tenant distinto, además de IDs adivinados.
- Archivos, blobs, cachés, jobs, SignalR y notificaciones mantienen namespace/partición por tenant.
- `SUPER_ADMIN` no implica bypass silencioso: requiere selección explícita, autorización, auditoría y límites.

## Riesgos comprobados

1. `TenantSettings` y `WhatsAppConfig` tienen `TenantId` sin `ITenantScoped`; el filtro automático no se aplica y cada consulta debe filtrar por tenant de forma explícita (la auditoría del 2026-10-01 encontró y corrigió una consulta del reporte operativo que no lo hacía).
2. Hay múltiples `IgnoreQueryFilters()` en autenticación, empleados, tenants, webhooks e integraciones. El uso del dashboard, que exponía los productos más vendidos de todos los tenants, se corrige en una tarea separada.
3. El override `X-Tenant-Id` de `SUPER_ADMIN` ya se audita en el log, pero no exige justificación, no tiene límites y no se persiste en `AuditLog`.
4. El middleware decide si una ruta es tenant-scoped mediante una lista manual; una ruta nueva puede quedar fuera por omisión.
5. `OrderItem` y `OrderStatusHistory` no tienen `TenantId` y se exponen como `DbSet`; una consulta directa sin unir con `Order` no tiene filtro.
6. No existe una suite end-to-end de aislamiento para API, archivos, SignalR y webhooks; hay pruebas unitarias del middleware, los hubs y el interceptor.

Estos puntos siguen siendo deuda/riesgo.
