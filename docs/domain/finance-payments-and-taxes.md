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
  - `Inflows`/`Outflows` y el saldo esperado solo cuentan efectivo; tarjeta, transferencia y otros métodos se reportan aparte (`NonCashInflows`). El corte diario expone `NetCashOnlyFlow` con la misma regla (`CashSession.IsCashMovement`): efectivo cobrado y propinas en efectivo más los movimientos en efectivo (`CashManualIncomeTotal` − `CashManualExpenseTotal`); un reembolso con tarjeta o transferencia no sale de ningún cajón y no lo baja. `ManualIncomeTotal`/`ManualExpenseTotal` y `NetCashFlow` cuentan todos los métodos, así que ese reembolso sí baja `NetCashFlow`. Desde la Fase 22F, `NetCashFlow` deja fuera lo que cobraron las plataformas (`PlatformTotal`): ese dinero no lo recibió el negocio y su reembolso no crea movimiento de caja (ver [pedidos de plataformas](#pedidos-de-plataformas-de-reparto-fase-22f)).
  - Cada cajero tiene su propia caja abierta por sucursal (índice único filtrado por negocio y cajero, ver [caja por cajero](#caja-por-cajero-fase-2)) y `CashSession.Revision` es token de concurrencia: un pago concurrente con el cierre o dos cierres simultáneos fallan con 409 en lugar de sobrescribirse. El cierre exige un monto contado mayor o igual a cero.
- Al registrar un pago, `OrderCashService` lo agrega a la caja del cajero que cobra; si no tiene, a la única abierta de la sucursal de la venta (nunca a una de otra sucursal; las cajas de antes de las sucursales la toman si son la única abierta). Todo cobro que recibe el negocio, y todo reembolso de ese dinero, entra a una caja, sea cual sea el método: si hay varias abiertas y ninguna es suya, o no hay ninguna, se rechaza ("Abre tu caja para cobrar"), así ninguno queda fuera del corte de un turno. Guardar un pedido sin cobrar no necesita caja: su pago entra a la caja abierta cuando se cobra. La excepción es el pago «Plataforma» ([ADR 0013](../adr/0013-delivery-platform-orders.md)): lo cobró Rappi, Uber Eats o DiDi Food en su app, así que no entra a ninguna caja ni exige una abierta, y su reembolso tampoco.
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

- `POST /api/v1/orders` acepta `Payments` (hasta 10 por venta, por ejemplo efectivo más tarjeta). `OrderPaymentRegistrar` los registra en el mismo guardado que la venta, con las mismas reglas que `POST /api/v1/orders/{id}/payments`: importe positivo con dos decimales, sin sobrepago, evidencia del tenant y movimiento en una caja abierta. Si un pago es inválido o ninguna caja lo toma, la venta no se crea.
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
- Vender manda la propina y el efectivo recibido con los pagos de la venta (`POST /api/v1/orders`); Pedidos y Mesas, con cada pago (`tip` y `cashReceived` en `POST /api/v1/orders/{id}/payments`). Hasta el 2026-10-13 ese endpoint los ignoraba: los cobros de Pedidos y Mesas desde la Fase 4F se guardaron sin propina ni efectivo recibido, y su propina no entró a la caja. `OrderPaymentTipSqlServerTests` lo prueba por la API contra SQL Server.
- Un reembolso o una anulación devuelven lo pagado de la venta, no la propina; si hay que devolverla, se registra como retiro de caja.

## Caja por cajero (Fase 2)

- Cada cajero abre su caja en una sucursal (`BranchId`; desde la Fase 3 la sucursal es entidad propia, ver [sucursales](branches-and-reporting.md)) y queda como dueño (`OpenedByUserId`). Solo quien la abrió o un administrador la cierra o registra gastos y retiros en ella.
- Al cerrar se guardan lo esperado (fondo + efectivo neto), lo contado, la diferencia y las notas; las notas son obligatorias si hay faltante o sobrante. El conteo por denominación es opcional y debe cuadrar con el total (`CashCountJson`).
- `GET /api/v1/cash/sessions` lista los turnos por fecha, sucursal y estado, con filtro de solo diferencias. Caja muestra Mi caja, Turnos (el administrador puede cerrar cajas ajenas) y Corte del día.
- Desde la Fase 4 el detalle de un turno imprime su corte en formato de ticket de 72 mm (fondo, entradas y salidas de efectivo, esperado, contado, diferencia, conteo por denominación, movimientos y espacio para dos firmas), y Corte del día imprime el resumen del día. Vender abre la caja del cajero en un diálogo, sin salir de la venta.
- Desde la Fase 5 el corte también sale por WhatsApp como texto (enlace `wa.me`, la persona elige el chat): el del turno desde su detalle en Turnos y desde Mi caja al cerrarla (con imprimir), y el del día desde Corte del día. Lleva las mismas cifras que el impreso, sin la lista de movimientos.
- Todo cobro que recibe el negocio, y todo reembolso de ese dinero, exige una caja abierta que lo tome, sea cual sea el método (antes era la política opcional `TenantSettings.RequireOpenCashSession`, solo para efectivo; su columna se borró con `DropRequireOpenCashSession`). Una venta sin conexión que llega sin caja abierta queda en el dispositivo como "No aceptada" y se reintenta al abrirla. El pago «Plataforma» de un pedido que cobró Rappi, Uber Eats o DiDi Food no entra a ninguna caja ni exige una abierta, y su reembolso tampoco (Fase 22F, [ADR 0013](../adr/0013-delivery-platform-orders.md)).
- `GET /api/v1/cash/current-session?branchId=` devuelve la caja que tomaría los cobros del usuario en esa sucursal, con la misma regla que el cobro (`OrderCashService.PickDrawer`), o nada si el cobro se rechazaría. Vender la consulta con la sucursal del dispositivo: avisa que hay que abrir la caja y, sin caja, Cobrar abre primero el diálogo para abrirla y luego el cobro.
- Las cajas anteriores a la Fase 2 se completaron en la migración: sucursal predeterminada del negocio, cajero por el correo con que se abrió y, en las cerradas, esperado y diferencia calculados de sus movimientos.

## Reembolsos y anulaciones (Fase 2)

- Un reembolso (`OrderRefund`) es un registro aparte e inmutable: importe con dos decimales, forma de pago, motivo, fecha del servidor, quién lo hizo y, opcionalmente, las unidades devueltas por línea (`OrderRefundLine`). Pagos y líneas no se editan; la orden acumula `RefundedAmount` y `OrderItem.ReturnedQuantity`, y expone `NetPaidAmount = PaidAmount − RefundedAmount`.
- No se puede reembolsar más de lo cobrado y no devuelto, ni devolver más unidades de las vendidas y no devueltas. Una orden cancelada no admite reembolsos.
- El efectivo devuelto sale de la caja del cajero (`CashTransactionType.Refund`); un reembolso con otra forma de pago queda registrado como salida no efectiva, y uno «Plataforma» no toca ninguna caja (Fase 22F). Si se pide, los insumos de las unidades devueltas regresan al inventario con la referencia `ORDER:{orden}:RETURN:{reembolso}`, una sola vez.
- **Anular venta** devuelve lo que queda de cada forma de pago con esa misma forma, regresa las unidades y cancela la orden si su estado lo permite; una venta entregada queda entregada y reembolsada por completo.
- Solo dueños, admins y personal con `ProcessRefunds` reembolsan o anulan (`OrderRefundProcessor`). Cada reembolso emite `OrderRefundedEvent`, que se audita por el outbox.
- Inicio descuenta los reembolsos del rango de los ingresos y las unidades devueltas de los más vendidos; el corte de caja los muestra como salidas, y los «Plataforma» aparte (`PlatformRefundsTotal`).

## Pedidos de plataformas de reparto (Fase 22F)

Un pedido de Rappi, Uber Eats o DiDi Food (canales 3 a 5) lo registra el negocio en Vender con el folio de la app ([ADR 0013](../adr/0013-delivery-platform-orders.md); sus reglas están en [reglas de negocio](business-rules.md#pedidos-de-plataformas-de-reparto-fase-22f)). Quién cobró se decide en cada pedido: la plataforma, en su app (por omisión), o el negocio, cuando el repartidor le paga al recoger.

- **Pago «Plataforma» (`OrderPaymentMethod.Platform`, 5):**
  - **Quién lo crea:** solo el servidor, al registrar un pedido que cobró la plataforma (`Order.RegisterPlatformOrder`). Es por el total, con la misma hora que el detalle del pedido, el folio como referencia y propina 0.
  - **Caja y permisos:** no entra a ninguna caja (`OrderPaymentMethodRules.EntersADrawer`), no exige una abierta y no pide `ChargeOrders`.
  - **Defensas:** nadie más lo registra.
    - La venta lo rechaza con «El pago «Plataforma» se registra solo, al indicar que cobró la plataforma.».
    - Un cobro lo rechaza con «El pago «Plataforma» solo lo registra el pedido al crearse.» (el validador, `OrderPaymentRegistrar` y `Order.RegisterPayment`).
    - Un movimiento de caja manual y `CashSession` lo rechazan con «Lo que cobra una plataforma no entra a ninguna caja.».

    Un pedido que cobró la plataforma no admite otros pagos.
  - **Devoluciones:** lo que cobró la plataforma se devuelve como «Plataforma», sin caja (anular un pedido de Rappi no la necesita), y solo eso se devuelve así. `OrderRefundProcessor` no busca caja para ese método.
- **Cuando cobra el negocio:** se cobra como siempre, al registrarlo o después desde Pedidos, y el cobro entra a una caja. No lleva propina, porque la de la app es del repartidor, y la plataforma no retiene nada.
- **Precios (Fase 22F-2, [ADR 0014](../adr/0014-delivery-platform-prices.md)):** cada línea cobra lo que cuesta el producto en la app de esa plataforma, más lo que suma ahí cada opción, y el del menú donde el negocio no puso uno (Catálogo › Precios en apps). El servidor resuelve el precio (`Product.PriceIn` y `ProductOption.PriceDeltaIn`) y lo copia a la línea, así que el IVA, el total, la comisión, las retenciones y el neto esperado salen del total que cobró la app. Cambiar un precio después no reescribe ningún pedido.
- **Términos por plataforma** (`DeliveryPlatformSetting`, uno por negocio y plataforma, Configuración › Negocio › Plataformas de reparto). Son porcentajes con hasta dos decimales:

  | Término | Rango del dominio | Rango de la API | Por omisión |
  |---|---|---|---|
  | Comisión | 0 a 60 | igual | sin valor: se exige al guardar |
  | Base de la comisión | total con IVA o subtotal sin IVA | igual | total con IVA |
  | IVA de la comisión | 0 a 100 | 16, 8 o 0 (`VatRateRules`) | 16 |
  | ISR retenido | 0 a 20 | igual | 2.5 |
  | IVA retenido (porcentaje del IVA del pedido) | 0 a 100 | igual | 50 |

  Cada pedido copia los términos, su revisión y los importes calculados en su `PlatformOrderDetail`, que no cambia. Cambiar una tasa nunca reescribe un pedido, igual que `Order.PricesIncludeTax`.
- **Fórmulas (`PlatformOrderCalculator`).** Todo es `decimal`, y cada componente se redondea una vez a centavos con `MidpointRounding.ToEven`, como el IVA. En las fórmulas, R es ese redondeo:

  ```
  base           = total con IVA o subtotal, según los términos
  comisión       = R(base × comisión / 100)
  IVA comisión   = R(comisión × IVA de la comisión / 100)
  si cobró la plataforma:
    ISR retenido = R(subtotal × ISR / 100)
    IVA retenido = R(Σ(base gravada × tasa / 100) × IVA retenido / 100)   // grupos no exentos con tasa > 0, sin redondear antes
    neto esperado = total − comisión − IVA comisión − ISR retenido − IVA retenido
  si cobró el negocio:
    retenciones = 0
    neto esperado = −(comisión + IVA comisión)   // lo que la plataforma descontará de un depósito
  ```

- **Ejemplos fijados por `PlatformOrderCalculatorTests`:**
  - Un pedido de $1,000 con IVA incluido (subtotal 862.07, IVA 137.93), comisión de 30 % sobre el total, ISR de 2.5 % e IVA retenido de 50 %, cobrado por la plataforma: comisión 300.00, IVA de la comisión 48.00, ISR 21.55 e IVA retenido 68.97. **Neto esperado: 561.48** con las tasas de 2026. Con ISR de 1 % (la tasa anterior a 2026) da **574.41**, el neto del ejemplo publicado de DiDi.
  - El mismo pedido cobrado por el negocio: retenciones en 0 y neto esperado **−348.00**.
  - Con IVA sumado y la comisión de 25 % sobre el subtotal (total 290, subtotal 250, IVA 40): comisión 62.50, IVA de la comisión 10.00, ISR 6.25, IVA retenido 20.00 y neto esperado 191.25.
  - Con tasas mezcladas ($500 al 16 % incluido y $200 al 0 %): neto esperado 406.14. La parte al 0 % no causa IVA retenido.
  - Una comisión de 10 % sobre 1.25 da 0.125 y queda en 0.12 (con `AwayFromZero` quedaría en 0.13).
- **Por qué el IVA retenido sale de la base por la tasa:** con el IVA ya redondeado del pedido, el de $1,000 daría 137.93 × 50 % = 68.965, que queda en 68.96. La base por la tasa da 862.07 × 0.16 × 0.5 = 68.9656, que queda en 68.97, como DiDi.
- **Qué no entra:** la propina, el envío y las promociones de la plataforma. Un precio de la app que no se actualizó en Precios en apps tampoco: el pedido sale con el que está guardado.
- **Retenciones y costo:** por lo general las retenciones son pagos a cuenta de los impuestos del negocio, no un costo (para quien optó por el pago definitivo, son su impuesto). El costo del canal es la comisión y su IVA, que el contador puede acreditar.
- **Corte del día (`GET /api/v1/cash/daily-cut`):**
  - **Cifras propias:** `PlatformTotal` y `PlatformPaymentsCount` son los pagos «Plataforma» del día.
  - **Devoluciones:** `PlatformRefundsTotal` son los reembolsos «Plataforma» del día, con los mismos filtros que los pagos: sucursal por la del pedido, usuario por quien reembolsó y ubicación por la del pago «Plataforma» del pedido.
  - **«Otros»:** `OtherTotal` es ahora todo método sin cubeta propia, así que efectivo, tarjeta, transferencia, billetera, otros y plataforma suman siempre lo cobrado, para cada método del enum.
  - **Filas:** la fila de cada cajero (`ByUser`) no incluye lo que cobró la plataforma; las de ubicación y sucursal sí.
  - **Flujos:** `NetCashFlow = SalesPaymentsTotal − PlatformTotal + TipsTotal + ManualIncomeTotal − ManualExpenseTotal`, y `NetCashOnlyFlow` no cambia.
  - **En pantalla:** Corte del día muestra «Plataformas» con «Lo cobró la plataforma: no entra a la caja» y lo devuelto. El impreso y el texto de WhatsApp suman las filas «Cobrado por plataformas» y «Devuelto por plataformas». No dicen «por liquidar»: lo que debe cada plataforma está en Reportes › Liquidaciones (Fase 22G). Los turnos y Mi caja no cambian.
- **Inicio:** «Cobrado» (`TotalRevenue`) conserva su significado e incluye lo que cobraron las plataformas. `PlatformCollected` es lo que cobraron (pagos «Plataforma» menos reembolsos «Plataforma» del rango, con los mismos tipos de pedido legibles), y la pantalla lo dice debajo: «Incluye $350.00 que cobraron las plataformas; no entra a la caja.». «Por cobrar» no cambia: lo que cobró la plataforma no tiene saldo, y lo que el negocio todavía no cobra sí está pendiente.
- **CFDI (vista previa):**
  - **Forma de pago:** un pedido pagado por completo con «Plataforma» se declara PUE con la forma 31, Intermediario pagos (`SatCatalog.PaymentFormIntermediary`).
  - **Avisos:** que normalmente va en la factura global del periodo; si cobró la plataforma, además que lleva la forma 31 y que la plataforma retiene ISR e IVA. Los dos piden confirmar con el contador. Con un receptor, avisa que la plataforma no comparte los datos de sus clientes y que solo se factura a nombre de alguien que dio los suyos.
  - **Fuera de alcance:** el CFDI de retenciones lo emite la plataforma.
- **Liquidación de los depósitos (Fase 22G, [ADR 0015](../adr/0015-delivery-platform-settlements.md)):**
  - **Lo que espera cada pedido:** `PlatformSettlementCalculator.ExpectedNet` = R(`ExpectedSettlement` × (`PaidAmount` − `RefundedAmount`) / `OrderTotal`), redondeado una vez a centavos con `MidpointRounding.ToEven`. Es la parte que Ventas por canal le atribuye al pedido: con todo devuelto da 0, y un pedido que cobró el negocio da su comisión e IVA en negativo.
  - **Ejemplos fijados por `PlatformSettlementTests` y `PlatformSettlementFlowTests`:** el pedido de $1,000 cobrado por la plataforma espera 561.48. Con $500 devueltos espera 280.74. Cobrado por el negocio espera −348.00, y nada mientras el negocio no lo cobra.
  - **Lo que explica el resto:** el 35 % de una promoción que cubre Rappi, los faltantes, el saldo negativo de DiDi por el efectivo que cobra el negocio, la publicidad o las cancelaciones tardías se registran como ajustes con signo; lo que no se identifica, como «Otro» con su nota.
  - **Cuadrar:** debió depositar = pedidos + ajustes, y la liquidación solo se cierra con la diferencia en $0.00. Al cerrar, el servidor vuelve a calcular cada pedido; si una devolución cambió alguno, guarda las cifras nuevas y pide revisarlas.
  - **Fuera de la caja:** un depósito no es un movimiento de caja ni de banco. Inicio, el corte y Ventas por canal siguen en base caja: lo que cobró la plataforma cuenta al registrar el pedido. «Por cobrar» de cada plataforma (Reportes › Liquidaciones) suma el neto esperado de sus pedidos pendientes, cada uno redondeado como en la liquidación, así que puede diferir por centavos de Ventas por canal, que redondea por fila.
- **Pendiente:** importar el estado de cuenta de cada plataforma (hoy se captura a mano) y el CFDI de retenciones.

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
