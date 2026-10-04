# Inventario y compras

## Estado actual

`SupplyItem` representa insumos; `InventoryLocation` es una ubicación de inventario que pertenece a una sucursal (`BranchId`, opcional para un almacén general; ver [sucursales](branches-and-reporting.md)) y las ventas consumen del almacén de su sucursal; `InventoryStock` almacena on-hand/reservado por insumo+ubicación. `InventoryMovement` y líneas forman el kardex con cantidades antes/después, costos, lote, caducidad, referencias y `ExternalReference`.

Las entradas/salidas manuales pasan por `CreateInventoryMovementCommand`. Recibir una `PurchaseOrder` actualiza líneas, stock, costo del insumo y crea movimiento de compra. Órdenes con receta crean salida por venta/consumo; cancelación crea entrada compensatoria. Un índice único `(TenantId, ExternalReference)` protege los movimientos automáticos que sí usan clave estable.

Reglas vigentes:

- Tipos y motivos se guardan y viajan como enteros (la API no usa `JsonStringEnumConverter`). `InventoryMovementType` va de 1 (`Entry`) a 8 (`ReturnToSupplier`) e `InventoryMovementReason` de 1 (`Purchase`) a 10 (`ManualAdjustment`), más 99 (`Other`). Suman existencias `Entry`, `AdjustmentIncrease`, `TransferIn` y `ReturnFromProduction`; restan `Exit`, `AdjustmentDecrease`, `TransferOut` y `ReturnToSupplier`. Renumerarlos reinterpretaría el historial: `InventoryEnumValuesTests` (Domain.Tests) fija los valores y `ClientEnumContractTests` (WebApi.Tests) compara la copia de `client/src/types/index.ts`.
- Solo las entradas (`Entry`) modifican el costo promedio; transferencias, ajustes y devoluciones se valoran al promedio vigente.
- Los movimientos manuales no pueden usar los prefijos reservados `ORDER:` y `PO:` ni repetir una referencia externa existente, ni fecharse en el futuro.
- Una recepción no puede repetir líneas ni fecharse en el futuro; el stock sube con las cantidades que la compra aceptó.
- Ciclo de una compra: se crea como borrador o enviada; un borrador se envía (`POST /purchaseorders/{id}/send`); llega por partes (`Recepción parcial`) hasta quedar `Recibida`. Se cancela (`POST /purchaseorders/{id}/cancel`) solo mientras no ha llegado nada: lo recibido ya es existencia y gasto. Cerrar una compra parcial sin esperar el resto todavía no existe.
- Cada línea recibida puede traer el costo real de la factura (`unitCost`): el movimiento de entrada, el costo promedio y el total de la línea usan ese costo; sin él se usa el pactado.
- Política de existencias por negocio (`TenantSettings.InsufficientStockPolicy`, migración `AddAppointmentResourcesAndStockPolicy`): si las recetas de una venta piden más insumo del que hay, `Block` (por defecto, el comportamiento anterior) rechaza la venta con un mensaje que nombra cada insumo, lo que hay y lo que se necesita; `Warn` registra la venta, deja el insumo en negativo y la respuesta trae `stockWarnings` ("Tortilla quedó en -3") que Vender muestra al cajero. La política aplica a lo que el equipo vende o confirma en ese momento (venta de mostrador, pedido capturado desde el chat, confirmar un pedido, completar una pieza). Nunca se rechazan: las ventas capturadas sin conexión, los pedidos que hace el cliente (menú en línea, bot de WhatsApp) y el cierre de una cuenta de mesa, porque la mercancía ya salió o el negocio revisa el pedido.
- Cambiar el insumo de una línea de receta reemplaza el requisito (antes se ignoraba y se seguía consumiendo el insumo anterior).
- `InventoryStock.OnHandQuantity`, `SupplyItem.AverageCost` y `PurchaseOrderLine.ReceivedQuantity` son tokens de concurrencia: dos operaciones simultáneas sobre el mismo stock, costo o línea ya no se sobrescriben; la segunda falla con 409 (el comando de piezas de servicio reintenta).

## Guardrails

- Controllers y React no modifican existencias directamente.
- Todo cambio produce un movimiento trazable con tenant, ubicación, causa y origen.
- Reintentos usan clave idempotente estable y constraint único.
- Salidas de órdenes referencian la orden/pieza origen.
- Cancelaciones/devoluciones compensan; nunca borran movimiento histórico.
- Transferencias identifican origen y destino y se ejecutan como unidad coordinada.
- No se permite negativo salvo regla explícita y auditada.
- Protege stock, folios y recepciones frente a concurrencia.
- Valida que insumo, ubicación, proveedor, compra y orden pertenezcan al mismo tenant.
- Diferencias entre stock calculado por kardex y almacenado se detectan/reconcilian mediante ajuste trazable.

## Deuda actual

- Hasta la corrección del 2026-10-04, la pantalla de inventario numeraba tipos y motivos desde 0. Cada movimiento manual anterior se guardó con el tipo y el motivo siguientes al elegido, así que movió las existencias en sentido contrario y a veces alteró el costo promedio; "Entrada" y "Compra" se rechazaban. Los movimientos del sistema no se afectaron. La revisión y compensación por tenant sigue [este procedimiento](../../database/production/data-repair/20261004_InventoryMovementEnumOffset/README.md) y está pendiente; no existe una operación para revaluar el costo promedio.
- Fuera del comando de piezas de servicio, un conflicto de concurrencia de stock responde 409 y el cliente debe reintentar; no hay reintento automático.
- `Product.StockQuantity` es informativo: las ventas no lo descuentan ni lo validan. Falta decidir si el stock de producto se controla con recetas (insumo = producto) o con ese campo.
- Folios `INV-yyyyMMdd-nnnn` se generan contando registros y pueden colisionar; el índice único detecta, pero no evita el conflicto.
- El movimiento manual permite tipos TransferIn/TransferOut, pero no se observó un caso de uso atómico que exija origen y destino.
- La recepción de compra genera `ExternalReference` con timestamp, por lo que un retry no deduplica de forma determinista; además el avance de línea puede impedir/alterar el segundo intento, pero no es una garantía idempotente formal.
- `allowNegative: true` aparece al revertir consumo (sumar una devolución no produce negativo) y en el consumo de órdenes, después de que la política de existencias aceptó el faltante; fuera de esos casos una salida no deja negativo.
- Las rondas de una cuenta de mesa no se revisan contra existencias: la cuenta consume al cerrar y ahí ya no se rechaza.
- No existe prueba de integración SQL Server para locks, constraints e idempotencia concurrente.
