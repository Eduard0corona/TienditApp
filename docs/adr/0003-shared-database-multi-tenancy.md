# ADR 0003: Multi-tenancy con base compartida

- Estado: Aceptada
- Fecha: 2026-08-10

## Contexto

Los tenants comparten `ApplicationDbContext` y esquema. Una filtración entre tenants es un incidente crítico.

## Decisión

Toda entidad tenant-owned implementa `ITenantScoped`, contiene `TenantId` obligatorio e inmutable y recibe filtro global. El tenant efectivo deriva de identidad/acceso validado. `IgnoreQueryFilters()` no es un bypass permitido; operaciones cross-tenant son explícitas, autorizadas y auditadas.

Archivos, SignalR, notificaciones, jobs y cachés siguen la misma partición. Las pruebas deben demostrar aislamiento de lectura y escritura.

## Consecuencias

La base compartida simplifica operación, pero convierte filtros, autorización y pruebas en controles de máxima prioridad. `TenantSettings`, `WhatsAppConfig`, overrides administrativos y usos actuales de `IgnoreQueryFilters()` requieren revisión.
