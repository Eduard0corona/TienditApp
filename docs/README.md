# Documentación de TienditApp

Esta documentación describe `master`, la única rama permanente desde el 4 de octubre de 2026 (antes convivía con `develop`).

## Convenciones de estado

- **Estado actual:** comprobado en código o configuración.
- **Decisión arquitectónica:** regla adoptada formalmente para nuevos cambios.
- **Arquitectura objetivo:** evolución aprobada aún no implementada.
- **Deuda técnica:** desviación o riesgo existente.
- **Propuesta:** recomendación pendiente de decisión humana.

## Índice

- Arquitectura: [visión general](architecture/system-overview.md), [capas](architecture/layers-and-dependencies.md), [contextos](architecture/bounded-contexts.md), [multi-tenancy](architecture/multi-tenancy.md), [identidad y acceso](architecture/identity-permissions-capabilities.md), [consistencia y eventos](architecture/data-consistency-and-domain-events.md), [frontend](architecture/frontend-architecture.md), [observabilidad](architecture/observability.md).
- Dominio: [capabilities de negocio](domain/business-capabilities.md), [reglas](domain/business-rules.md), [ciclos de órdenes](domain/order-lifecycles.md), [inventario y compras](domain/inventory-and-purchasing.md), [finanzas e impuestos](domain/finance-payments-and-taxes.md), [mensajería](domain/messaging.md), [sucursales y reportes](domain/branches-and-reporting.md), [planes y facturación](domain/billing-and-platform.md).
- Desarrollo: [entorno local](development/local-development.md), [estándares](development/coding-standards.md), [pruebas](development/testing-strategy.md), [migraciones](development/database-migrations.md), [despliegue](development/deploy-azure.md), [Git](development/git-workflow.md), [Definition of Done](development/definition-of-done.md).
- IA: [flujo](ai/task-workflow.md), [guardrails](ai/guardrails.md), [checklist](ai/review-checklist.md).
- Evolución: [actual vs objetivo](roadmap/current-vs-target.md), [deuda técnica](roadmap/technical-debt.md).
- Decisiones: [ADRs](adr/0001-modular-monolith.md), entre ellos [horario del negocio](adr/0011-opening-hours.md), [días especiales](adr/0012-special-days.md), [pedidos de Rappi, Uber Eats y DiDi Food](adr/0013-delivery-platform-orders.md), [precios en las apps de las plataformas](adr/0014-delivery-platform-prices.md), [liquidación de sus depósitos](adr/0015-delivery-platform-settlements.md), [sucursal solo a domicilio](adr/0016-delivery-only-branches.md), [zonas de entrega](adr/0017-delivery-zones.md), [pedido a domicilio](adr/0018-home-delivery-orders.md), [cobro contra entrega del repartidor](adr/0019-courier-cash-on-delivery.md), [servicios a domicilio](adr/0020-home-visits.md), [Explorar, el directorio de negocios](adr/0021-business-directory.md), [comprar con tu cuenta](adr/0022-shopping-with-an-account.md), [seguimiento del pedido](adr/0023-order-tracking.md), [fotos del servicio](adr/0024-service-photos.md), [citas en línea](adr/0025-online-booking.md), [el menú con la forma de las apps de entrega](adr/0026-store-like-delivery-apps.md) y [lo que leen los buscadores y las vistas previas](adr/0027-search-engines-and-link-previews.md).
- Seguridad: [SECURITY.md](../SECURITY.md).
