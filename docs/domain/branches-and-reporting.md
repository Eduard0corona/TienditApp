# Sucursales y modelo de lectura de ventas (Fase 3)

## Sucursal como entidad propia

- `Branch` es el lugar donde el negocio vende. Es tenant-owned (`ITenantScoped`), con clave única por negocio (`Code`, en mayúsculas), dirección, encargado y teléfono.
- Cada negocio tiene exactamente una sucursal principal (índice único filtrado `IX_Branches_TenantId_Default`). La principal no se puede desactivar; una sucursal inactiva no puede ser principal. Cambiar la principal se hace en dos guardados dentro de una transacción, para no chocar con el índice único.
- No se puede desactivar una sucursal con cajas abiertas. Un negocio tiene máximo 50 sucursales.
- Las altas de negocio (`RegisterBusinessCommand`, `CreateTenantCommand`) y los datos semilla crean la "Sucursal principal" (`PRINCIPAL`) y le asignan el almacén principal.
- `GET /api/v1/branches` lo lee cualquier miembro activo del negocio (`RequiresTenantMember`), sin depender del módulo de inventario; crear, editar, activar y marcar principal es de dueños y administradores (`RequiresTenantManager`). La pantalla está en Configuración › Sucursales.

## Qué pertenece a una sucursal

| Dato | Campo | Cómo se decide |
|---|---|---|
| Venta | `Order.BranchId` (fija al crear) | La sucursal pedida (debe estar activa y ser del negocio); si no, la de la caja abierta del cajero; si no, la principal. Los pedidos en línea y los de clientes van a la principal. |
| Caja | `CashSession.BranchId` | La sucursal elegida al abrir (o la principal). `InventoryLocationId` guarda el almacén de esa sucursal para los cortes por ubicación. |
| Ubicación de inventario | `InventoryLocation.BranchId` | Opcional: sin sucursal es un almacén general que surte a todas. Al crear una sucursal en un negocio con inventario se crea su almacén. |

- El efectivo de una venta entra a la caja del cajero que cobra; si no tiene, a la única caja abierta de la sucursal de la venta. Nunca a una caja de otra sucursal; con varias cajas abiertas en la sucursal y ninguna del cajero no se adivina (ver `OrderCashService.PickDrawer`).
- El consumo de insumos de una venta sale del almacén de su sucursal (su ubicación principal o la primera activa; si no tiene, el almacén principal del negocio). Las devoluciones regresan los insumos a la ubicación de donde salieron.
- En Vender, la venta se registra en la sucursal de la caja abierta del cajero; sin caja, en la sucursal elegida en el dispositivo (se recuerda por negocio en el navegador). El corte del día y el historial de turnos filtran por sucursal.

## Migración `AddBranchesAndDailySalesSnapshots`

- Antes las ubicaciones de inventario hacían de sucursal. La migración crea una sucursal por cada ubicación **con el mismo Id y los mismos datos**, y asigna la ubicación a ella; así los pagos, devoluciones y cajas que apuntaban a una ubicación apuntan al mismo lugar.
- Principal por negocio: su ubicación principal activa, si no la primera activa, si no la primera. Los negocios sin ubicaciones reciben una "Sucursal principal".
- Cajas: la sucursal de su ubicación; si no tenían, la principal. Pedidos: la ubicación de su primer pago; si no, la principal.
- Después de migrar, las ubicaciones que solo eran almacenes aparecen como sucursales: desactívalas en Configuración › Sucursales y, si surten a todas, márcalas como almacén general en Inventario.
- El rollback se niega (`THROW 50003`) si hay sucursales que no venían de una ubicación (se perderían), salvo la "Sucursal principal" predeterminada sin editar, que no guarda nada más.

## Modelo de lectura: ventas diarias por sucursal

- `DailySalesSnapshot` guarda, por negocio, día local, sucursal y tipo de pedido: pedidos recibidos, pedidos con pago, pagos por método, devoluciones y la zona horaria con que se cortó el día.
- Un día se considera cerrado 15 minutos después de la medianoche local. Desde ese momento sus cifras no cambian: pagos y devoluciones llevan hora del servidor y no se editan, y los pedidos cuentan el día en que se crearon y no cambian de sucursal. Por eso un día cerrado se calcula una vez.
- `SalesSnapshotWorkerService` (con `BackgroundJobs:Enabled`) guarda cada 30 minutos los días cerrados que falten, del más reciente hacia atrás, hasta 31 por negocio y pasada, con un horizonte de 400 días (`BackgroundJobs:SalesSnapshots`). Es idempotente: recalcula el día completo y reemplaza lo guardado; si otra instancia lo guardó primero, el índice único lo rechaza y la siguiente pasada lo encuentra. Los días sin ventas se guardan como una fila en ceros.
- Si el negocio cambia de zona horaria, los días guardados con otra zona se ignoran y se vuelven a calcular.
- `GET /api/v1/reports/sales-by-branch?from&to&branchId` (`ViewReports`) lee los días guardados y calcula al momento hoy y los días que falten. Periodo máximo de 366 días; por omisión el mes en curso. Respeta los módulos del negocio (ventas retail y servicios). En Reportes se muestra como "Ventas por sucursal".
- "Tickets cobrados" suma los pedidos con pago de cada día: un pedido pagado en dos días distintos cuenta en ambos.

## Pendiente

- Mesas por sucursal (hoy las mesas son del negocio).
- Inicio sigue calculando sus indicadores en vivo; puede leer del modelo de lectura cuando el volumen lo pida.
