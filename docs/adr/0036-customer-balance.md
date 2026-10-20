# ADR 0036: Saldo a favor del cliente

- Estado: Aceptada
- Fecha: 2026-10-20

## Contexto

Con las fases 26A a 26C ([ADR 0033](0033-customer-credit.md), [0034](0034-customer-write-offs-and-credit-term.md) y [0035](0035-customer-credit-term-and-reminders.md)), el negocio fía, condona, pone plazos y recuerda lo vencido. Todo eso va en una dirección: lo que el cliente le debe al negocio. Falta la otra, lo que el negocio le debe al cliente, que en un negocio de barrio pasa todos los días:

- **El anticipo.** «Te dejo 300 para el pastel del sábado.» Hoy no hay dónde anotarlo: o se cobra una venta que todavía no existe, o se queda en un papel.
- **El cambio que se queda.** La vecina abona 500 a una cuenta de 316 y dice «quédatelo para la próxima». Hoy el abono no puede pasar de lo que debe.
- **El reembolso que no sale de la caja.** Se le regresa lo que se cobró de más como saldo para su próxima compra, no en efectivo.

El roadmap juntaba el saldo a favor con los intereses del fiado. Los intereses quedan fuera:

- hay que pactarlos por escrito;
- causan IVA y se facturan;
- tienen límites legales;
- en un negocio de barrio casi no se cobran.

Si alguien los pide, serán una fase aparte.

Tomé las decisiones de la Fase 26D con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **El saldo es un libro inmutable por cuenta** (`CustomerBalanceEntry`). La cuenta es el teléfono, como el fiado (`MessagingPhone.ToKey`), con la ficha del cliente si la hay. Cada movimiento tiene un tipo y un monto con signo:
   - `Deposit` (+): lo que dejó a su favor, un anticipo o el resto de un abono;
   - `Refund` (+): un reembolso a su saldo;
   - `Applied` (−): lo que pagó con él;
   - `Returned` (−): lo que se le devolvió.

   El saldo es la suma de sus movimientos. Ninguno se edita ni se borra: un error se compensa con otro movimiento.
2. **El saldo nunca queda negativo ni se usa dos veces.**
   - Lo que toma del saldo lo lee y lo escribe dentro del candado del cliente (`ICustomerCreditLock`, `sp_getapplock`) y en una transacción (`ITransactionalRequest`).
   - Las pantallas mandan el saldo que mostraban (`ExpectedBalance`, obligatorio al dejar, al devolver y al pagar con el saldo o dejar el resto de un abono), y la cuenta lo que debía (`ExpectedOwed`). Si cambió, la petición se rechaza con el saldo nuevo («El saldo a favor cambió: ahora tiene…»). Así, un reintento o dos cajeros a la vez no dejan, usan ni devuelven dos veces.
   - La venta de Vender que paga con saldo usa además su llave de idempotencia: un reintento devuelve la venta que ya existe y no vuelve a tomar del saldo.
3. **Dejar a favor** (`AddCustomerDepositCommand`, con `ChargeOrders`, como cobrar).
   - Se elige el monto (hasta $1,000,000), cómo entra (efectivo, tarjeta, transferencia, billetera u otro), una referencia y una nota.
   - Entra a la caja abierta de quien cobra (o a la única abierta) como un movimiento propio, `CashTransactionType.CustomerDeposit` (8). No es una venta, ni un pago de un pedido, ni lleva CFDI.
   - Sin caja abierta no se puede: «Abre tu caja: lo que un cliente deja a su favor, o se le devuelve, pasa por una caja».
   - El abono a la cuenta (`PayCustomerAccountCommand`) acepta más de lo que se debe solo si se pide (`RestInFavor`), en un método en persona. Lo que sobra queda como un `Deposit` con la nota «Lo que sobró de un abono».
   - Queda en la actividad (`CustomerBalanceDeposited`).
4. **Pagar con el saldo** usa una forma de pago nueva, `OrderPaymentMethod.CustomerBalance` (7).
   - No entra a ninguna caja, como «Plataforma» o «En línea»: el dinero entró cuando lo dejó.
   - La venta se reconoce al aplicarlo: es un pago del pedido (`OrderPayment`), y su movimiento `Applied` queda ligado a ese pago (uno por pago, con índice único).
   - **En Vender**, a una venta con el teléfono del cliente, con conexión y cobrada por el negocio. Sin propina, sin comprobante y sin efectivo recibido. No en un pedido de plataforma ni en la cola sin conexión: el saldo solo lo sabe el servidor.
   - **En la cuenta**, con «Usar saldo a favor»: paga lo que debe desde el pedido más viejo, hasta lo que tiene o lo que debe, lo que sea menos.
   - No en los otros caminos: Pedidos (`AddOrderPaymentCommand`), los movimientos de caja a mano, las liquidaciones de repartidores. Se cobra desde la cuenta o en Vender.
5. **Reembolsar al saldo**: es una forma más de devolver en Reembolsar (con `ManageOrders` y `ProcessRefunds`, como hoy), solo si el pedido tiene teléfono.
   - No sale de ninguna caja. Agrega un `Refund` (+) a la cuenta del teléfono del pedido, ligado al reembolso (uno por reembolso).
   - Anular una venta pagada con saldo lo regresa al saldo, porque la anulación devuelve cada pago con su forma de pago.
6. **Devolver el saldo** (`ReturnCustomerBalanceCommand`) pide `ChargeOrders` y `ProcessRefunds`: es dinero que sale, como un reembolso.
   - Se devuelve hasta lo que tiene, en un método en persona.
   - Sale de la caja de quien devuelve como `CashTransactionType.CustomerDepositReturn` (9), con signo negativo.
   - Queda en la actividad (`CustomerBalanceReturned`).
7. **El corte del día**:
   - Lo pagado con saldo es parte de «Cobrado» y de cada sucursal, pero no de la fila de ningún cajero ni del flujo neto, porque ya entró con el anticipo. Va en su línea, «Pagado con saldo a favor».
   - Lo reembolsado al saldo va en «Reembolsado a saldo a favor».
   - Lo que dejaron y lo devuelto son dinero de las cajas, no ventas. Van en sus líneas («Dejaron a favor» y «Saldo a favor devuelto»), fuera de los movimientos a mano. Sí entran al flujo neto, y al de efectivo solo su parte en efectivo.
   - Se mantienen las cuentas del corte: `NetCashFlow` es la suma de lo que entró y salió de cada caja (fuera o dentro de ella), y `NetCashOnlyFlow` la del efectivo.
8. **CFDI**: lo pagado con saldo usa la forma de pago 30 (aplicación de anticipos), y el borrador de la factura avisa (`CustomerBalanceWarning`). El CFDI del anticipo y su egreso los hace el contador: TienditApp no los genera.
9. **Los reportes diarios**: lo pagado con saldo cuenta en «Otros» de la foto diaria. Sus columnas son fijas y no hay una propia: agregarla pediría migrar las fotos guardadas.
10. **Se ve:**
    - **Estado de cuenta:** «A favor $X», los últimos 10 movimientos con su nota, y «Dejar a favor», «Usar saldo a favor» (si debe y tiene) y «Devolver saldo» (para quien también reembolsa, con el monto escrito).
    - **Abonar:** «Dejar el cambio a su favor», con lo que queda a su favor en lugar del cambio.
    - **Ficha del cliente:** «A favor $X». La cuenta se abre aunque no deba ni tenga fiado, para quien cobra.
    - **Vender:** «Saldo a favor» como forma de pago, con lo que le queda y el aviso «{nombre} tiene $X a favor».
    - **Por cobrar:** «A favor $X» en la fila de quien también debe, y el total a favor de los clientes del negocio.
    - **Reembolsar:** «Saldo a favor», si el pedido tiene teléfono.
    - **Corte del día:** sus líneas, impreso y por WhatsApp.
    - **Ticket y estado de cuenta por WhatsApp:** «Saldo a favor» y «Tienes $X a tu favor».
11. **Fuera de esta fase:**
    - intereses del fiado;
    - saldo a favor sin teléfono;
    - usarlo desde Pedidos (se cobra desde la cuenta);
    - anticipos ligados a un pedido o a una cita;
    - el CFDI del anticipo;
    - que el cliente vea su saldo en su cuenta de comprador.
12. **La migración** `20261020145628_AddCustomerBalance` agrega la tabla `CustomerBalanceEntries`:
    - `Phone` es `varchar(20)`, `Amount` es `decimal(18,2)`, y `Method`, `CashSessionId`, `OrderId`, `OrderPaymentId` y `OrderRefundId` aceptan nulos;
    - `CK_CustomerBalanceEntries_Amount` exige el signo de cada tipo;
    - `CK_CustomerBalanceEntries_Ties` exige a qué va ligado: lo que se deja o se devuelve, a su método y su caja; un pago, a su pedido y su pago; un reembolso, a su pedido y su reembolso;
    - el índice `(TenantId, Phone, OccurredAt)` incluye `Amount`, para el saldo y los últimos movimientos;
    - índices únicos filtrados en `OrderPaymentId` y `OrderRefundId`.

## Consecuencias

- **Lo que logra:**
  - El negocio anota anticipos y el cambio que el cliente le deja, en el mismo lugar que el fiado, y los cobra después sin papel.
  - La caja cuadra: lo que dejaron entra el día que lo dejaron, y la venta pagada con saldo no lo cuenta dos veces.
  - Cada peso del saldo tiene su historia: quién lo dejó, cómo, cuándo y en qué se usó o se devolvió.
- **Lo que limita:**
  - El saldo es de un teléfono. Un cliente con dos teléfonos tiene dos saldos.
  - En los reportes por día, lo pagado con saldo se ve como «Otros».
  - Lo que se deja a favor no lleva CFDI de anticipo desde TienditApp; si el cliente lo pide, lo hace el contador.
  - En Vender sin conexión no se puede pagar con saldo.
- **Compatibilidad durante el despliegue:**
  - La versión anterior (26C) funciona con la base migrada: no conoce la tabla, y no hay pagos ni movimientos de caja con los valores nuevos hasta que la 26D los crea.
  - Mientras conviven las dos versiones (el despliegue o un swap), la 26C no sabe mostrar la forma de pago 7 ni los movimientos de caja 8 y 9 que haya creado la 26D.
- **Reversibilidad:**
  - `rollback.sql` borra la tabla solo si no hay movimientos de saldo, pagos ni reembolsos con la forma de pago 7, ni movimientos de caja de tipo 8 o 9 o con la forma de pago 7.
  - En cuanto alguno existe, se detiene con el error 50029 sin cambiar nada, y solo queda corregir hacia adelante.
