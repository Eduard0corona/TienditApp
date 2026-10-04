# Finanzas, pagos e impuestos

## Estado actual

- Importes, costos, impuestos y existencias usan `decimal`; no se encontraron `float`/`double` para dinero en Domain/Application.
- `Order` calcula `SubTotal`, `Tax`, `Total`, `PaidAmount` y `Balance` con el desglose de IVA por tasa descrito abajo. Admite pagos parciales y rechaza sobrepago.
- `OrderPayment` conserva método, fecha, ubicación opcional, evidencia URL, referencia y notas. La fecha del pago la pone el servidor (`PaidAt` ya no se acepta del cliente) y el importe admite máximo 2 decimales.
- Una orden cancelada no acepta pagos y una orden con dinero sin reembolsar no puede cancelarse (ver [reembolsos](#reembolsos-y-anulaciones-fase-2) y ADR 0007); `OrderDto.CanCancel` refleja ambas reglas con el neto pagado.
- La evidencia de un pago debe ser un archivo subido por el endpoint de evidencias para el mismo tenant (`OrderUploadRules`); las imágenes se re-codifican y los PDF se sirven como descarga.
- `CashSession` conserva fondo, apertura/cierre, entradas, salidas y saldo esperado. `CashTransaction` puede referir orden, método y ubicación.
  - El signo del movimiento lo define su tipo (gasto, retiro y reembolso restan; depósito y venta suman); el importe capturado siempre es positivo con 2 decimales y la fecha es la del servidor.
  - Los movimientos manuales solo pueden ser depósito, gasto, retiro o reembolso; la orden referida debe existir.
  - `Inflows`/`Outflows` y el saldo esperado solo cuentan efectivo; tarjeta, transferencia y otros métodos se reportan aparte (`NonCashInflows`). El corte diario expone `NetCashOnlyFlow`.
  - Cada cajero tiene su propia caja abierta por sucursal (índice único filtrado por negocio y cajero, ver [caja por cajero](#caja-por-cajero-fase-2)) y `CashSession.Revision` es token de concurrencia: un pago concurrente con el cierre o dos cierres simultáneos fallan con 409 en lugar de sobrescribirse. El cierre exige un monto contado mayor o igual a cero.
- Al registrar un pago, `OrderCashService` lo agrega a la caja del cajero que cobra; si no tiene, a la única abierta de la sucursal del pago o del negocio. Si hay varias abiertas y ninguna es suya, o no hay ninguna, el pago sigue registrado sin movimiento de caja, salvo que el negocio exija caja abierta para cobrar en efectivo.
- Compras aceptan `TaxPercent` por línea y calculan subtotales/impuesto.
- `DigitalWallet` existe como método de pago y subtotal del corte, no como cuenta Wallet.

## Impuestos (Fase 1)

- Cada negocio define en `TenantSettings` si sus precios ya incluyen IVA (`PricesIncludeTax`), su tasa general (`DefaultTaxRate`, 16 % por defecto) y si las ventas de mostrador pagadas se cierran solas (`AutoCloseCounterSales`). Los negocios nuevos nacen con precios con IVA incluido; los que ya existían conservan el IVA sumado al 16 % hasta que su administrador lo cambie en el arranque guiado o en "Mi negocio" (`PUT /api/v1/auth/my-tenant/sales-policy`, auditado), para que ningún total cambie sin aviso.
- Un producto puede tener tasa propia (16, 8 o 0 %) o ser exento (`Product.IsTaxExempt`); sin tasa propia usa la del negocio (`Product.GetVatRate`). La API acepta solo 16, 8 y 0 (`VatRateRules`); el dominio admite de 0 a 100 con dos decimales (`VatRate`).
- Cada línea guarda la tasa con la que se vendió (`OrderItem.TaxRate`, `OrderItem.IsTaxExempt`) y la orden guarda la política vigente al crearse (`Order.PricesIncludeTax`). Cambiar la política o la tasa de un producto no altera ventas anteriores.
- `OrderTaxCalculator` agrupa las líneas por tasa y redondea el IVA de cada grupo a dos decimales con redondeo bancario (`MidpointRounding.ToEven`):
  - con precios con IVA: impuesto = importe × r / (100 + r) y base = importe − impuesto;
  - con IVA sumado: impuesto = importe × r / 100 y base = importe;
  - exentos y tasa 0 no causan impuesto.
- `SubTotal` es la suma de las bases, `Tax` la suma de los impuestos y `Total = SubTotal + Tax`. Con todas las líneas al 16 % sumado el resultado es idéntico al cálculo anterior (`round(SubTotal × 0.16)`), así que las órdenes existentes no cambian.
- `OrderDto.TaxBreakdown` expone el desglose por tasa y el ticket lo imprime. El SPA lo estima con el mismo algoritmo en centavos enteros (`estimateOrderTotals`); la venta envía `ExpectedTotal` y la API la rechaza sin guardarla si un precio cambió mientras se capturaba.
- No existen CFDI, claves SAT, descuentos por línea ni notas de crédito fiscales. El desglose persistido por línea es la base de ese trabajo.

## Cobro en la venta y cierre automático

- `POST /api/v1/orders` acepta `Payments` (hasta 10 por venta, por ejemplo efectivo más tarjeta). `OrderPaymentRegistrar` los registra en el mismo guardado que la venta, con las mismas reglas que `POST /api/v1/orders/{id}/payments`: importe positivo con dos decimales, sin sobrepago, evidencia del tenant y movimiento en la caja abierta si existe. Si un pago es inválido, la venta no se crea.
- El efectivo recibido y el cambio se calculan en el cliente y salen en el ticket; la API recibe solo el importe aplicado y no guarda el efectivo entregado.
- Con `AutoCloseCounterSales`, una venta de mostrador (`CreatedByBusiness`) sin saldo se cierra sola mediante `Order.CloseIfPaidCounterSale` y la máquina de estados:
  - una venta de tienda o de comida sin pantalla de cocina pasa a `Delivered`;
  - un pedido de comida de un negocio con Cocina pasa a `Delivered` cuando la cocina lo marca `Ready` (o cuando se paga después de estar listo);
  - un servicio pasa a `Completed` si no tiene piezas rastreadas aceptadas o en proceso.
- El cierre se evalúa al crear la venta, al registrar un pago y cuando la orden llega a `Ready`. Queda en el historial con el usuario y no envía WhatsApp al cliente (`OrderStatusChangedEvent.NotifyCustomer = false`).
- Inicio reporta ingresos por pagos (base de efectivo): `TotalRevenue` suma los pagos con `PaidAt` en el rango sin importar el estado de la orden, `AverageOrderValue` divide entre las órdenes que recibieron esos pagos y `OutstandingBalance`/`UnpaidOrders` muestran el saldo de las órdenes no canceladas del rango.

## Caja por cajero (Fase 2)

- Cada cajero abre su caja en una sucursal (`InventoryLocationId`) y queda como dueño (`OpenedByUserId`). Solo quien la abrió o un administrador la cierra o registra gastos y retiros en ella.
- Al cerrar se guardan lo esperado (fondo + efectivo neto), lo contado, la diferencia y las notas; las notas son obligatorias si hay faltante o sobrante. El conteo por denominación es opcional y debe cuadrar con el total (`CashCountJson`).
- `GET /api/v1/cash/sessions` lista los turnos por fecha, sucursal y estado, con filtro de solo diferencias. Caja muestra Mi caja, Turnos (el administrador puede cerrar cajas ajenas) y Corte del día.
- `TenantSettings.RequireOpenCashSession` (apagado por defecto) rechaza cobros y reembolsos en efectivo si el cajero no tiene caja abierta; tarjeta y transferencia no la necesitan.
- Las cajas anteriores a la Fase 2 se completaron en la migración: sucursal predeterminada del negocio, cajero por el correo con que se abrió y, en las cerradas, esperado y diferencia calculados de sus movimientos.

## Reembolsos y anulaciones (Fase 2)

- Un reembolso (`OrderRefund`) es un registro aparte e inmutable: importe con dos decimales, forma de pago, motivo, fecha del servidor, quién lo hizo y, opcionalmente, las unidades devueltas por línea (`OrderRefundLine`). Pagos y líneas no se editan; la orden acumula `RefundedAmount` y `OrderItem.ReturnedQuantity`, y expone `NetPaidAmount = PaidAmount − RefundedAmount`.
- No se puede reembolsar más de lo cobrado y no devuelto, ni devolver más unidades de las vendidas y no devueltas. Una orden cancelada no admite reembolsos.
- El efectivo devuelto sale de la caja del cajero (`CashTransactionType.Refund`); un reembolso con otra forma de pago queda registrado como salida no efectiva. Si se pide, los insumos de las unidades devueltas regresan al inventario con la referencia `ORDER:{orden}:RETURN:{reembolso}`, una sola vez.
- **Anular venta** devuelve lo que queda de cada forma de pago con esa misma forma, regresa las unidades y cancela la orden si su estado lo permite; una venta entregada queda entregada y reembolsada por completo.
- Solo dueños, admins y personal con `ProcessRefunds` reembolsan o anulan (`OrderRefundProcessor`). Cada reembolso emite `OrderRefundedEvent`, que se audita por el outbox.
- Inicio descuenta los reembolsos del rango de los ingresos y las unidades devueltas de los más vendidos; el corte de caja los muestra como salidas.

## Otros cálculos

El reporte operativo calcula la utilidad bruta sin IVA (`NetRevenue` − costo de ventas), toma como costo de ventas los consumos de las mismas órdenes reconocidas como ingreso y, sin rango explícito, usa el mes en la zona horaria del tenant.

Compras redondean línea a 4 decimales y órdenes a 2, sin política transversal documentada. La diferencia debe resolverse antes de ampliar contabilidad.

## Reglas aprobadas

- Nunca usar `float`/`double` para dinero.
- Backend realiza cálculos autoritativos.
- Moneda, precisión, midpoint rounding, nivel de redondeo y orden de operaciones son explícitos.
- No modificar/eliminar transacciones históricas para corregir saldo.
- Correcciones futuras usan ajustes crédito/débito trazables, relacionados con orden, pago o movimiento original.
- Los ajustes contemplan cuentas por cobrar, saldo a favor, Wallet y conciliación sin afirmar que esos módulos existan hoy.
- Evidencias de pago se tratan como archivo sensible tenant-scoped.
- Pago, orden, caja y ubicación deben compartir tenant y correlación verificable.

## Arquitectura objetivo: Tax Calculation Engine México

`OrderTaxCalculator` cubre ya precios con impuesto incluido o separado, varias tasas, exentos, redondeo uniforme y desglose persistido por línea. Falta un componente versionado que reciba contexto de entidad legal, fecha y líneas, y cubra además:

- reglas por artículo/servicio y entidad legal;
- orden explícito de descuentos;
- otros impuestos (IEPS, retenciones);
- datos para CFDI (claves SAT, objeto de impuesto, régimen fiscal del receptor).

La adopción requiere especificación fiscal, pruebas con casos límite, migración compatible y revisión contable/legal.
