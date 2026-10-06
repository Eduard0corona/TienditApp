# Reglas de Application

- Organiza casos de uso por contexto como Command o Query con MediatR; no accedas desde WebApi directamente a persistencia.
- Depende solo de Domain y abstracciones necesarias; interfaces de persistencia/integraciones viven en `Common/Interfaces` o el contexto adecuado.
- FluentValidation cubre formato/entrada; autorización, tenant, permission y capability se evalúan en backend por caso de uso.
- Nunca aceptes `TenantId` de frontend como autoridad. Usa `ICurrentTenantService` o un flujo administrativo explícito y validado.
- No añadas `IgnoreQueryFilters()` sin escenario pre-tenant/cross-tenant documentado, filtro explícito, autorización y pruebas.
- Usa DTOs, `AsNoTracking` para lecturas y paginación antes de materializar (un listado sin páginas se acota con un tope por negocio en Domain, como `Category.MaxCategoriesPerTenant`); propaga `CancellationToken`.
- Coordina cambios de orden/inventario/caja y side effects mediante puertos. No llames providers concretos.
- Agrega tests de handler/validator, incluidos errores, aislamiento, idempotencia y concurrencia relevantes.
