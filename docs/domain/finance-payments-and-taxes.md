# Finanzas, pagos e impuestos

## Estado actual

- Importes, costos, impuestos y existencias usan `decimal`; no se encontraron `float`/`double` para dinero en Domain/Application.
- `Order` calcula `SubTotal`, `Tax`, `Total`, `PaidAmount` y `Balance` con el desglose de IVA por tasa descrito abajo. Admite pagos parciales y rechaza sobrepago.
- `OrderPayment` conserva método, fecha, ubicación opcional, evidencia URL, referencia y notas. La fecha del pago la pone el servidor (`PaidAt` ya no se acepta del cliente) y el importe admite máximo 2 decimales.
- Una orden cancelada no acepta pagos y una orden con dinero sin reembolsar no puede cancelarse (ver [reembolsos](#reembolsos-y-anulaciones-fase-2) y ADR 0007); `OrderDto.CanCancel` refleja ambas reglas con el neto pagado.
- La evidencia de un pago debe ser un archivo subido por el endpoint de evidencias para el mismo tenant (`OrderUploadRules`); las imágenes se re-codifican y los PDF se sirven como descarga. Desde la Fase 8 es privada: el pago guarda `/files/{key}` y solo `GET /api/v1/files/{key}` la entrega, a miembros del negocio con `ManageOrders` o `ChargeOrders`.
- `CashSession` conserva fondo, apertura/cierre, entradas, salidas y saldo esperado. `CashTransaction` puede referir orden, método y ubicación.
  - El signo del movimiento lo define su tipo (gasto, retiro y reembolso restan; depósito y venta suman); el importe capturado siempre es positivo con 2 decimales y la fecha es la del servidor.
  - Los movimientos manuales solo pueden ser depósito, gasto, retiro o reembolso; la orden referida debe existir.
  - `Inflows`/`Outflows` y el saldo esperado solo cuentan efectivo; tarjeta, transferencia y otros métodos se reportan aparte (`NonCashInflows`). El corte diario expone `NetCashOnlyFlow` con la misma regla (`CashSession.IsCashMovement`): efectivo cobrado y propinas en efectivo más los movimientos en efectivo (`CashManualIncomeTotal` − `CashManualExpenseTotal`); un reembolso con tarjeta o transferencia no sale de ningún cajón y no lo baja. `ManualIncomeTotal`/`ManualExpenseTotal` y `NetCashFlow` cuentan todos los métodos, así que ese reembolso sí baja `NetCashFlow`.
  - Cada cajero tiene su propia caja abierta por sucursal (índice único filtrado por negocio y cajero, ver [caja por cajero](#caja-por-cajero-fase-2)) y `CashSession.Revision` es token de concurrencia: un pago concurrente con el cierre o dos cierres simultáneos fallan con 409 en lugar de sobrescribirse. El cierre exige un monto contado mayor o igual a cero.
- Al registrar un pago, `OrderCashService` lo agrega a la caja del cajero que cobra; si no tiene, a la única abierta de la sucursal de la venta (nunca a una de otra sucursal; las cajas de antes de las sucursales la toman si son la única abierta). Si hay varias abiertas y ninguna es suya, o no hay ninguna, el pago sigue registrado sin movimiento de caja, salvo que el negocio exija caja abierta para cobrar en efectivo.
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
- No hay timbrado (PAC), descuentos por línea ni notas de crédito fiscales. Las claves SAT por línea y la vista previa del CFDI están en [cantidades decimales y CFDI](#cantidades-decimales-y-datos-para-cfdi-fase-3).

## Cantidades decimales y datos para CFDI (Fase 3)

- Kilo, gramo, libra, onza, litro, mililitro, metro y hora se venden con hasta 3 decimales (`UnitTypeRules`); pieza, paquete, caja, par, porción, rebanada, ración, rollo, sesión y servicio solo en enteros. El dominio rechaza 1.5 piezas y la API rechaza más de 3 decimales (`PrecisionScale(18, 3)`). Un servicio rastreable crea una pieza por unidad, así que pide cantidades enteras aunque se venda por hora.
- `OrderItem.Quantity`, `ReturnedQuantity` y `OrderRefundLine.Quantity` son `decimal(18,3)`. El importe de la línea se redondea una vez a centavos con redondeo bancario (`TotalPrice = round(cantidad × precio, 2, ToEven)`) y Vender lo estima igual en centavos enteros (`lineAmountCents`). Los reembolsos devuelven fracciones (0.5 kg de 1.25 kg).
- Vender pregunta "¿Cuánto?" al tocar un producto por kilo, litro, metro u hora, con atajos (¼, ½, 1, 2 kg; 100 a 1000 g; ½ a 2 h); los botones + y − suman 0.1 kg/L/m, 50 g/ml o ½ hora. El ticket imprime "1.255 kg" en lugar de "1 ×".
- Cada producto puede tener clave de producto o servicio SAT (`SatProductCode`, 8 dígitos), clave de unidad (`SatUnitCode`) y objeto de impuesto (`TaxObject`: 02 sí objeto, 01 no objeto; un producto 01 debe ser exento o 0 %). Sin clave usa la genérica 01010101 y la unidad de su tipo (H87 pieza, KGM kilo, LTR litro, E48 servicio...).
- Cada línea copia al venderse la unidad y las claves del producto (`OrderLineFiscalData`); cambiar el producto después no altera ventas anteriores. La migración completó las líneas existentes con la unidad actual de su producto.
- `CfdiAmountCalculator` reparte una orden en conceptos CFDI 4.0: por tasa, las bases suman la base de la orden y los IVA de los conceptos suman el IVA de la orden. Cada IVA queda dentro del rango que acepta el SAT para su base (base ± medio centavo por la tasa, truncado y redondeado hacia arriba); el centavo que deja el redondeo por grupo pasa al concepto que más se acerca. Exentos se declaran con TipoFactor Exento, 0 % con Tasa 0.000000 y los conceptos 01 sin traslado. Una prueba con 500 órdenes aleatorias comprueba que siempre cuadra.
- `GET /api/v1/orders/{id}/cfdi-draft` (con `ManageOrders`) devuelve la vista previa: emisor (RFC, razón social, régimen y código postal de "Mi negocio"), receptor (público en general por defecto, o el RFC, nombre, código postal, régimen y uso que se den), forma y método de pago (PUE con la forma del mayor pago si está pagada; PPD y 99 si tiene saldo), conceptos, traslados y totales. Separa lo que impide timbrar (datos del emisor faltantes, diferencia de redondeo) de los avisos (claves genéricas, reembolsos que requieren nota de crédito, factura global). Una orden cancelada no se factura.
- Pendiente: timbrado con un PAC y certificados CSD, factura global del periodo, complementos de pago y notas de crédito (CFDI de egreso).

## Cobro en la venta y cierre automático

- `POST /api/v1/orders` acepta `Payments` (hasta 10 por venta, por ejemplo efectivo más tarjeta). `OrderPaymentRegistrar` los registra en el mismo guardado que la venta, con las mismas reglas que `POST /api/v1/orders/{id}/payments`: importe positivo con dos decimales, sin sobrepago, evidencia del tenant y movimiento en la caja abierta si existe. Si un pago es inválido, la venta no se crea.
- Desde la Fase 4F cada pago guarda el efectivo recibido y la propina (ver [Propina y efectivo recibido](#propina-y-efectivo-recibido-fase-4f)).
- Con `AutoCloseCounterSales`, una venta de mostrador (`CreatedByBusiness`) sin saldo se cierra sola mediante `Order.CloseIfPaidCounterSale` y la máquina de estados:
  - una venta de tienda o de comida sin pantalla de cocina pasa a `Delivered`;
  - un pedido de comida de un negocio con Cocina pasa a `Delivered` cuando la cocina lo marca `Ready` (o cuando se paga después de estar listo);
  - un servicio pasa a `Completed` si no tiene piezas rastreadas aceptadas o en proceso.
- El cierre se evalúa al crear la venta, al registrar un pago y cuando la orden llega a `Ready`. Queda en el historial con el usuario y no envía WhatsApp al cliente (`OrderStatusChangedEvent.NotifyCustomer = false`).
- Inicio reporta ingresos por pagos (base de efectivo): `TotalRevenue` suma los pagos con `PaidAt` en el rango sin importar el estado de la orden, `AverageOrderValue` divide entre las órdenes que recibieron esos pagos y `OutstandingBalance`/`UnpaidOrders` muestran el saldo de las órdenes no canceladas del rango. Desde la Fase 4, Más vendidos sigue la misma base: cuenta las líneas de las órdenes no canceladas que recibieron un pago en el rango (una venta cobrada que sigue en cocina cuenta; una entrega a crédito todavía no).
- La moneda es el peso mexicano: `TenantSettings.Currency` y `Product.PriceCurrency` valen `MXN` por defecto y la migración `UseMexicanPesosByDefault` cambió a `MXN` el `USD` que los negocios recibieron del valor predeterminado anterior (ninguna pantalla permitía elegir otra moneda). Los importes no cambian.

## Propina y efectivo recibido (Fase 4F)

- Cada pago (`OrderPayment`) guarda su propina (`Tip`, cero por defecto) y, en efectivo, lo que entregó el cliente (`CashReceived`); el cambio se calcula (`Change = CashReceived − Amount − Tip`). Así el ticket reimpreso desde Pedidos muestra la propina, el efectivo recibido y el cambio, no solo el ticket recién cobrado.
- La propina no es parte de la venta: no lleva IVA, no entra en `Total`, `PaidAmount` ni `Balance`, y no cuenta en los ingresos ni en los reportes de ventas. La orden expone `TipTotal`.
- Reglas: la propina no puede ser negativa ni mayor que el total de la cuenta (la suma de las propinas de la orden); puede ser mayor que el último pago de un cobro mixto (los últimos $5 en efectivo con $20 de propina). `CashReceived` solo se acepta en efectivo y debe cubrir el pago más la propina. Los validadores responden 400 y el dominio repite las reglas.
- En la caja, la propina entra como su propio movimiento (`CashTransactionType.Tip`, ingreso) en la misma caja y con la forma de pago del pago: en efectivo aumenta el esperado; con tarjeta queda como entrada no efectiva. No es un movimiento manual.
- El corte del día muestra `TipsTotal` y `CashTipsTotal` aparte de lo cobrado: `NetCashOnlyFlow` incluye las propinas en efectivo (están en los cajones) y `NetCashFlow` todas.
- Vender, Pedidos y Mesas ofrecen Sin propina, 10 %, 15 % u Otra (sobre lo que se cobra), y la propina va con el último pago del cobro. El botón muestra lo que paga el cliente con la propina incluida.
- Un reembolso o una anulación devuelven lo pagado de la venta, no la propina; si hay que devolverla, se registra como retiro de caja.

## Caja por cajero (Fase 2)

- Cada cajero abre su caja en una sucursal (`BranchId`; desde la Fase 3 la sucursal es entidad propia, ver [sucursales](branches-and-reporting.md)) y queda como dueño (`OpenedByUserId`). Solo quien la abrió o un administrador la cierra o registra gastos y retiros en ella.
- Al cerrar se guardan lo esperado (fondo + efectivo neto), lo contado, la diferencia y las notas; las notas son obligatorias si hay faltante o sobrante. El conteo por denominación es opcional y debe cuadrar con el total (`CashCountJson`).
- `GET /api/v1/cash/sessions` lista los turnos por fecha, sucursal y estado, con filtro de solo diferencias. Caja muestra Mi caja, Turnos (el administrador puede cerrar cajas ajenas) y Corte del día.
- Desde la Fase 4 el detalle de un turno imprime su corte en formato de ticket de 72 mm (fondo, entradas y salidas de efectivo, esperado, contado, diferencia, conteo por denominación, movimientos y espacio para dos firmas), y Corte del día imprime el resumen del día. Vender abre la caja del cajero en un diálogo, sin salir de la venta.
- Desde la Fase 5 el corte también sale por WhatsApp como texto (enlace `wa.me`, la persona elige el chat): el del turno desde su detalle en Turnos y desde Mi caja al cerrarla (con imprimir), y el del día desde Corte del día. Lleva las mismas cifras que el impreso, sin la lista de movimientos.
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
- Evidencias de pago se tratan como archivo sensible tenant-scoped: almacenamiento privado y descarga autorizada (Fase 8).
- Pago, orden, caja y ubicación deben compartir tenant y correlación verificable.

## Arquitectura objetivo: Tax Calculation Engine México

`OrderTaxCalculator` cubre ya precios con impuesto incluido o separado, varias tasas, exentos, redondeo uniforme y desglose persistido por línea, y `CfdiAmountCalculator` lo reparte en conceptos CFDI con claves SAT y objeto de impuesto por línea. Falta un componente versionado que reciba contexto de entidad legal, fecha y líneas, y cubra además:

- reglas por artículo/servicio y entidad legal;
- orden explícito de descuentos;
- otros impuestos (IEPS, retenciones);
- timbrado, factura global, complementos de pago y notas de crédito.

La adopción requiere especificación fiscal, pruebas con casos límite, migración compatible y revisión contable/legal.
