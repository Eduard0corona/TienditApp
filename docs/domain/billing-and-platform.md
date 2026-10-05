# Planes, facturación y consola de plataforma (Fase 3)

## Modelo

- `Plan` es de la plataforma (no de un negocio): clave única, nombre, precio mensual (`decimal`, MXN), módulos que un negocio en él puede encender (`TenantCapability`, al menos un tipo de venta), límites de sucursales activas y usuarios activos, días de prueba gratis y si es el plan para negocios nuevos (uno como máximo, índice único filtrado). Precios y planes los define el administrador de la plataforma; no se siembra ninguno.
- `TenantSubscription` (tenant-owned, una por negocio) guarda el plan, el fin de la prueba (`TrialEndsAt`), el fin del periodo pagado (`PaidThrough`) y el estado. `SubscriptionPayment` registra cada pago tal como se capturó (monto, meses, método, referencia, periodo que cubre y quién lo registró); los pagos no se editan.
- **Un negocio sin plan (todos los creados antes de esta fase) no tiene límites.**

## Estados

| Estado | Cuándo | Acceso |
|---|---|---|
| En prueba | Antes de `TrialEndsAt` y sin periodo pagado vigente. | Sí |
| Al corriente | Antes de `PaidThrough`. | Sí |
| Pago vencido | Terminó la prueba o el periodo pagado; dura 7 días de gracia. | Sí, con aviso a dueños y administradores |
| Suspendido | Sin pago después de los días de gracia, o suspendido por la plataforma (motivo obligatorio). | No |
| Cancelado | Cancelado por la plataforma (motivo obligatorio). | No |

- Un pago extiende el periodo desde donde termina la cobertura (un pago durante la prueba empieza al terminar la prueba) o desde el día del pago si ya terminó; levanta la suspensión por falta de pago, no la suspensión manual.
- Reactivar quita la suspensión manual o la cancelación y vuelve a decidir el estado por fechas: sin pago ni prueba vigente sigue suspendido hasta registrar un pago o extender la prueba.
- `SubscriptionStatusWorkerService` (cada hora, `BackgroundJobs:Subscriptions:IntervalMinutes`) aplica las fechas a cada negocio en su propio scope, de forma idempotente, y audita cada cambio (`SubscriptionStatusChanged`).
- El estado de la suscripción se refleja en `Tenant.Status`: suspendido → `Suspended`, cancelado → `Inactive`, el resto → `Active`.

## Qué limita el plan

- **Módulos:** el dueño solo puede encender los módulos del plan (`UpdateMyTenantCommand` responde qué módulos no incluye). Al asignar o cambiar el plan, o al quitarle módulos a un plan, cada negocio conserva solo los incluidos (siempre conserva un tipo de venta) y se audita (`PlanModulesTrimmed`). La configuración guardada es la fuente de verdad: las lecturas de módulos no cambian.
- **Sucursales y usuarios activos:** crear o activar una sucursal o un usuario más allá del límite se rechaza con el mensaje del plan.
- **Acceso:** el middleware responde 403 (`type: tenant-suspended`) a todo lo de un negocio suspendido o cancelado excepto `GET /api/v1/auth/my-tenant` y `GET /api/v1/auth/my-tenant/subscription`, para que el cliente muestre el motivo. SignalR tampoco une a esos negocios y no se envían sus campañas ni recordatorios. El administrador de la plataforma no se ve afectado.

## Negocios nuevos

- Si hay un plan para negocios nuevos (activo), el registro (`RegisterBusinessCommand`) y el alta por la plataforma (`CreateTenantCommand`) crean la suscripción en prueba con los días del plan y recortan los módulos iniciales al plan. Sin ese plan, el negocio nace sin límites.

## Consola de plataforma

- `api/v1/platform/*` (`RequiresPlatformAdmin` en cada caso de uso): resumen (negocios, en prueba, al corriente, pago vencido, sin acceso, ingreso mensual recurrente y cobrado en 30 días), listado paginado de negocios con plan, estado, vencimiento, sucursales, usuarios y última venta, y la cobranza de un negocio: asignar o cambiar plan, registrar pago, extender prueba, suspender, quitar suspensión y cancelar.
- Las acciones sobre un negocio ponen ese negocio en el scope (`ICurrentTenantService`) antes de leer y escribir, así los filtros y `TenantSaveChangesInterceptor` aplican como en una sesión, y se auditan en el negocio (`PlanAssigned`, `PlanChanged`, `SubscriptionPaymentRegistered`, `TrialExtended`, `TenantSuspended`, `TenantReactivated`, `SubscriptionCancelled`). Las lecturas de toda la plataforma ignoran el filtro de tenant a propósito y no escriben.
- Cliente: menú "Plataforma" (Negocios y Planes) solo para el administrador de la plataforma; "Mi negocio" muestra el plan a dueños y administradores, deshabilita los módulos fuera del plan y avisa del pago vencido; un negocio suspendido ve una pantalla con el motivo.

## Proveedor de pagos

- Hoy los pagos los registra la plataforma (transferencia, tarjeta en terminal, efectivo). Un proveedor de pagos se integra después llamando a `RegisterSubscriptionPaymentCommand` desde su webhook (idempotente por referencia del proveedor) y, si hace falta, enlaces de pago; ese adaptador y su verificación de firma quedan pendientes.

## Migración `AddPlansAndSubscriptions`

- Solo agrega tablas (`Plans`, `TenantSubscriptions`, `SubscriptionPayments`); no cambia datos existentes. El rollback se niega (`THROW 50004`) si ya hay pagos registrados.
