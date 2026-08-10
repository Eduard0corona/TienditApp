# Documentación de TienditApp

Esta documentación describe `develop` en el commit `b26a3c6` inspeccionado el 10 de agosto de 2026. La divergencia confirmada fue `origin/master...origin/develop = 5/27` (commits exclusivos de cada lado). La rama `master` local estaba desactualizada y no se usó como referencia.

## Convenciones de estado

- **Estado actual:** comprobado en código o configuración.
- **Decisión arquitectónica:** regla adoptada formalmente para nuevos cambios.
- **Arquitectura objetivo:** evolución aprobada aún no implementada.
- **Deuda técnica:** desviación o riesgo existente.
- **Propuesta:** recomendación pendiente de decisión humana.

## Índice

- Arquitectura: [visión general](architecture/system-overview.md), [capas](architecture/layers-and-dependencies.md), [contextos](architecture/bounded-contexts.md), [multi-tenancy](architecture/multi-tenancy.md), [identidad y acceso](architecture/identity-permissions-capabilities.md), [consistencia y eventos](architecture/data-consistency-and-domain-events.md), [frontend](architecture/frontend-architecture.md).
- Dominio: [capabilities de negocio](domain/business-capabilities.md), [reglas](domain/business-rules.md), [ciclos de órdenes](domain/order-lifecycles.md), [inventario y compras](domain/inventory-and-purchasing.md), [finanzas e impuestos](domain/finance-payments-and-taxes.md).
- Desarrollo: [entorno local](development/local-development.md), [estándares](development/coding-standards.md), [pruebas](development/testing-strategy.md), [migraciones](development/database-migrations.md), [Git](development/git-workflow.md), [Definition of Done](development/definition-of-done.md).
- IA: [flujo](ai/task-workflow.md), [guardrails](ai/guardrails.md), [checklist](ai/review-checklist.md).
- Evolución: [actual vs objetivo](roadmap/current-vs-target.md), [deuda técnica](roadmap/technical-debt.md).
- Decisiones: [ADRs](adr/0001-modular-monolith.md).
- Seguridad: [SECURITY.md](../SECURITY.md).
