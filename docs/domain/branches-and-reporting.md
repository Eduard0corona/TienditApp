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

- El cobro de una venta entra a la caja del cajero que cobra; si no tiene, a la única caja abierta de la sucursal de la venta. Nunca a una caja de otra sucursal; con varias cajas abiertas en la sucursal y ninguna del cajero no se adivina, y sin caja que lo tome el cobro se rechaza (ver `OrderCashService.PickDrawer`). Por eso cada sucursal que cobra necesita al menos una caja abierta, y el menú en línea recibe pedidos solo mientras la principal tiene una (`OrderCashService.HasOpenDrawerAtBranchAsync`). Lo que cobra una plataforma de reparto en su app (pago «Plataforma», Fase 22F) no entra a ninguna caja; su pago guarda el almacén de la sucursal de la venta, como cualquier otro.
- El consumo de insumos de una venta sale del almacén de su sucursal (su ubicación principal o la primera activa; si no tiene, el almacén principal del negocio). Las devoluciones regresan los insumos a la ubicación de donde salieron.
- En Vender, la venta se registra en la sucursal de la caja abierta del cajero; sin caja, en la sucursal elegida en el dispositivo (se recuerda por negocio en el navegador). El corte del día y el historial de turnos filtran por sucursal.

## Migración `AddBranchesAndDailySalesSnapshots`

- Antes las ubicaciones de inventario hacían de sucursal. La migración crea una sucursal por cada ubicación **con el mismo Id y los mismos datos**, y asigna la ubicación a ella; así los pagos, devoluciones y cajas que apuntaban a una ubicación apuntan al mismo lugar.
- Principal por negocio: su ubicación principal activa, si no la primera activa, si no la primera. Los negocios sin ubicaciones reciben una "Sucursal principal".
- Cajas: la sucursal de su ubicación; si no tenían, la principal. Pedidos: la ubicación de su primer pago; si no, la principal.
- Después de migrar, las ubicaciones que solo eran almacenes aparecen como sucursales: desactívalas en Configuración › Sucursales y, si surten a todas, márcalas como almacén general en Inventario.
- El rollback se niega (`THROW 50003`) si hay sucursales que no venían de una ubicación (se perderían), salvo la "Sucursal principal" predeterminada sin editar, que no guarda nada más.

## Modelo de lectura: ventas diarias por sucursal

- `DailySalesSnapshot` guarda, por negocio, día local, sucursal y tipo de pedido: pedidos recibidos, pedidos con pago, pagos por método, devoluciones y la zona horaria con que se cortó el día. Desde la Fase 22F, lo que cobraron las plataformas de reparto (pagos «Plataforma») va en `PlatformAmount` y no en `OtherAmount`, así que lo cobrado es siempre la suma de efectivo, tarjeta, transferencia, billetera, otros y plataforma (I6). La columna tiene 0 por omisión, y los días guardados antes quedan en 0, que es lo que tuvieron: el método no existía.
- Si un swap de despliegue cruza la medianoche y la instancia anterior a la Fase 22F guarda el día con pagos «Plataforma», ese día queda con ellos en «Otros». La consulta 7 del `verify.sql` de `AddDeliveryPlatformOrders` lo detecta y `database/production/data-repair/20261012_PlatformAmountSnapshots` lo repara: borra esos días para que el worker los vuelva a calcular ([migraciones](../development/database-migrations.md)).
- Un día se considera cerrado 15 minutos después de la medianoche local. Desde ese momento sus cifras no cambian: pagos y devoluciones llevan hora del servidor y no se editan, y los pedidos cuentan el día en que se crearon y no cambian de sucursal. Por eso un día cerrado se calcula una vez.
- `SalesSnapshotWorkerService` (con `BackgroundJobs:Enabled`) guarda cada 30 minutos los días cerrados que falten, del más reciente hacia atrás, hasta 31 por negocio y pasada, con un horizonte de 400 días (`BackgroundJobs:SalesSnapshots`). Es idempotente: recalcula el día completo y reemplaza lo guardado; si otra instancia lo guardó primero, el índice único lo rechaza y la siguiente pasada lo encuentra. Los días sin ventas se guardan como una fila en ceros.
- Si el negocio cambia de zona horaria, los días guardados con otra zona se ignoran y se vuelven a calcular.
- `GET /api/v1/reports/sales-by-branch?from&to&branchId` (`ViewReports`) lee los días guardados y calcula al momento hoy y los días que falten. Periodo máximo de 366 días; por omisión el mes en curso. Respeta los módulos del negocio (ventas retail y servicios). En Reportes se muestra como "Ventas por sucursal". Desde la Fase 22F, `SalesFiguresDto.PlatformAmount` dice lo que cobraron las plataformas, y la tabla muestra la columna «Plataformas» solo si el periodo tiene ese dinero.
- Reglas de fechas compartidas (Fase 22F): las ventas por sucursal, las ventas por canal y los pedidos de una plataforma resuelven el periodo igual (`DailySalesReadModel.ResolveRange`: el mes en curso hasta hoy sin fechas, el mes que empieza en "desde" si solo viene esa, y nunca más de 366 días) y lo validan igual (`ReportRangeRules`: «La fecha inicial debe ser anterior a la final.» y «El periodo puede abarcar como máximo 366 días.»).
- "Tickets cobrados" suma los pedidos con pago de cada día: un pedido pagado en dos días distintos cuenta en ambos.
- Desde la Fase 14 Inicio (`GET /api/v1/dashboard/metrics`) lee del mismo modelo sus ingresos (cobrado menos reembolsado): los días cerrados guardados y, para hoy y los que falten, una suma en la base, sin leer venta por venta (`DailySalesReadModel.ReadNetSalesAsync`). Los conteos por estado, los clientes únicos, el saldo por cobrar y los pedidos pagados salen de agregados en SQL; un pedido pagado en dos días del periodo cuenta una vez, porque el ticket promedio es por pedido. El periodo son días completos del negocio (cada extremo vale por el día en que cae) y abarca hasta 366 días.

## Ventas por canal (Fase 22F)

`GET /api/v1/reports/sales-by-channel?from&to&branchId` (`ViewReports`, `GetSalesByChannelQuery`) reparte las ventas por el origen del pedido (`Order.Channel`), con lo que cobró cada plataforma de reparto, su comisión, el IVA de la comisión, sus retenciones y lo que debería depositar ([ADR 0013](../adr/0013-delivery-platform-orders.md)). En Reportes se muestra como "Ventas por canal", debajo de las ventas por sucursal.

- **Base caja, la de Inicio y las ventas por sucursal:**
  - lo cobrado y lo que cobró la plataforma cuentan en la fecha de cada pago (`PaidAt`);
  - lo devuelto, en la de cada reembolso (`RefundedAt`).

  Usa los mismos filtros que las ventas por sucursal: días locales, tipos de pedido que el negocio puede leer según sus módulos y la sucursal del pedido (`Order.BranchId`). El periodo se resuelve y se valida como en las ventas por sucursal.
- **En vivo:** el resumen diario no tiene una dimensión de canal. La consulta agrega en SQL, con `GROUP BY` del canal y sumas condicionales, sin leer pago por pago. Usa los índices de pagos y reembolsos por negocio y fecha.
- **Las cifras de plataforma siguen al dinero:**
  - **Cómo se calculan:** cada pago de un pedido con detalle suma su parte de lo que copió el pedido (importe × pago / total del pedido), y cada reembolso resta la suya. Cada suma se hace con toda la precisión en la base y se redondea una vez por fila con `ToEven`. El neto esperado se deriva de los componentes ya redondeados (I7), y la fila Total suma las filas.
  - **Cobró la plataforma:** el pago es por el total y lleva la misma hora que el registro, así que la parte es 1 y la fila coincide al centavo con la suma de lo que copiaron los pedidos.
  - **Cobra el negocio:** la comisión aparece cuando el repartidor paga, en proporción a lo cobrado. Un pedido cancelado sin pago no genera comisión.
  - **Anulación otro día:** compensa ese día y deja como estaba el día de la venta; un periodo que cubre los dos queda en cero. Un reembolso parcial compensa en proporción, como estimación.
  - **En SQL Server:** cada parte se calcula con escala 6 (`decimal(38,6)`) antes de sumarse. Con pagos completos el resultado es exacto; con pagos parciales, en un punto medio raro, puede diferir un centavo del cálculo con toda la precisión.
- **Filas:** una por canal con pagos o reembolsos en el periodo, más las plataformas activas aunque estén en cero, en el orden del enum. Las cifras de plataforma salen en 0 en Mostrador, WhatsApp y En línea. `BusinessCollected` (venta neta menos lo que cobró la plataforma) es lo que el negocio cobró de los pedidos de una plataforma, y es 0 en los demás canales.
- **Pedidos con pago:** `PaidOrders` cuenta cada pedido una vez por periodo, como Inicio. Las ventas por sucursal lo cuentan por día ("Tickets cobrados"). Ningún importe depende de esa diferencia.
- **Invariantes de cuadre**, cada una con su prueba (`GetSalesByChannelQueryTests`, `CashShiftTests`, `DailySalesReadModelTests`, `GetDashboardMetricsQueryTests`, `FractionalSaleAndCfdiDraftTests` y, contra SQL Server, `SalesByChannelSqlServerTests`):

  | # | Igualdad |
  |---|---|
  | I1 | Lo cobrado del total por canal es lo cobrado del total por sucursal. |
  | I2 | Lo devuelto del total por canal es lo devuelto del total por sucursal. |
  | I3 | La venta neta del total por canal es la del total por sucursal y es «Cobrado» de Inicio. |
  | I4 | La suma de los pagos «Plataforma» es `PlatformAmount` del total por sucursal y la suma de `PlatformTotal` de los cortes de esos días. |
  | I5 | En el corte, lo cobrado es efectivo + tarjeta + transferencia + billetera + otros + plataforma, para cada método del enum. |
  | I6 | En cada día guardado, lo cobrado es la suma de sus cubetas por método, para cada método del enum. |
  | I7 | En cada fila de plataforma, el neto esperado es lo que cobró la plataforma menos la comisión, su IVA, el ISR y el IVA retenidos, al centavo. |
  | I8 | `PlatformCollected` de Inicio es la suma de lo que cobró la plataforma en las filas por canal. |
  | I9 | La vista previa del CFDI de un pedido que cobró la plataforma y está pagado declara PUE con la forma de pago 31. |

- **Lista por pedido:** `GET /api/v1/reports/delivery-platform-orders?channel&from&to&branchId&pageNumber&pageSize` (`ViewReports`) sirve para comparar folio por folio con el estado de cuenta semanal de una plataforma. Es de solo lectura.
  - **Filtro y orden:** los pedidos se filtran por la hora de su registro (`PlatformOrderDetail.RegisteredAt`) en días locales, del más reciente al más viejo y luego por id.
  - **Columnas:** el folio de la app, el pedido, la fecha, el estado, quién cobró, el total, la comisión con su IVA, las retenciones, el neto esperado que copió y lo devuelto desde entonces.
  - **Validación:** exige Rappi, Uber Eats o DiDi Food («Elige Rappi, Uber Eats o DiDi Food.»), páginas de 1 a 100 (20 por omisión) y las mismas reglas de fechas. Aplica los mismos filtros de módulos y de sucursal.
- **El canal no cambia** (`Order.Channel` se fija al crear el pedido), así que un día cerrado tampoco cambia de canal.

## Pendiente

- Mesas por sucursal (hoy las mesas son del negocio).
