# ADR 0047: La factura en parcialidades (PPD) y los complementos de pago

- Estado: Aceptada
- Fecha: 2026-10-24

## Contexto

Desde la Fase 28A (ADR 0045) el negocio timbra la factura de un pedido **pagado por completo** (PUE). Una venta fiada (Fase 26A) o entregada antes de cobrarse se quedaba sin factura hasta liquidarse, aunque el cliente la pidiera el día de la compra. El SAT resuelve ese caso con la factura en parcialidades o diferido (método PPD, forma 99) y un **CFDI de recepción de pagos** (tipo P, complemento Pagos 2.0) por cada cobro que se reciba después.

Lo que piden las fuentes primarias (Anexo 20 rev. 3, Guía de llenado del CFDI y del complemento para recepción de pagos 2.0, matriz de errores de julio de 2026, RMF 2026):

- **La factura PPD** lleva forma de pago 99 «Por definir» (CFDI40105); una PUE nunca la lleva.
- **El CFDI tipo P:** SubTotal y Total 0, Moneda XXX, sin FormaPago ni MetodoPago, Exportación 01. El receptor es el de la factura, con uso CP01. Un solo concepto 84111506, cantidad 1, ACT, «Pago», valor e importe 0, ObjetoImp 01.
- **Pagos 2.0:**
  - Totales: MontoTotalPagos y, por tasa, TotalTrasladosBaseIVA16/8/0 con su impuesto, y BaseIVAExento.
  - Pago: FechaPago, FormaDePagoP (nunca 99), MonedaP MXN, TipoCambioP 1 y Monto.
  - DoctoRelacionado:
    - el UUID, serie y folio de la factura, MonedaDR MXN y EquivalenciaDR 1;
    - NumParcialidad de 1 a 999;
    - ImpSaldoAnt > 0, ImpPagado > 0 e ImpSaldoInsoluto = ImpSaldoAnt − ImpPagado ≥ 0;
    - ObjetoImpDR, y con 02 sus ImpuestosDR.
  - TrasladoDR con BaseDR > 0 y un ImporteDR dentro de los límites de redondeo de su base. ImpuestosP es la suma por impuesto, factor y tasa.
- **El IVA de cada pago:** el SAT deja el reparto al emisor. La práctica es prorratear por la proporción del pago sobre el total de la factura, tasa por tasa, y que el último pago absorba el centavo que deja el redondeo.
- **Plazo:** a más tardar el día 5 natural del mes siguiente al del pago (RMF 2.7.1.32). Los pagos recibidos antes de la factura (un enganche) también llevan complemento, con su fecha real.
- **Cancelación:** el complemento se cancela con motivo 02 o 03 y puede pedir la aceptación del receptor. El SAT no cancela una factura PPD mientras tenga complementos vigentes.

Tomé las decisiones de la Fase 28C con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **La factura en parcialidades** de un pedido se permite cuando no está pagado por completo (o no tiene cobros), el cliente ya la debe (`Order.IsOwedByCustomer`: se vendió fiada, o se entregó o completó) y ya no puede cambiar su total.
   - Siguen bloqueadas: la cancelada, la de total 0, la que tiene condonación o reembolsos (28D) y la que tiene un reembolso en línea en curso. Una venta que todavía no se lleva el cliente se cobra antes.
   - Una venta fiada que sigue pendiente o es una cuenta abierta todavía toma partidas, envío o rondas: se factura cuando se acepta o se cierra («La venta todavía puede cambiar…»), porque la factura ya no cambiaría con ella.
   - La vista previa ya armaba la factura PPD: forma 99, método PPD. Avisa que TienditApp emite el complemento de cada cobro, también de los anteriores a la factura, y que los cobros «Otro» o con saldo a favor no tienen forma del SAT para su complemento.
   - `CfdiInvoice.Begin` recibe el método: PUE exige una forma real y PPD exige 99. `CK_OrderInvoices_PaymentMethod` lo repite en la base. La factura global sigue siendo PUE.
2. **La base `Cfdi`** se extrae de `CfdiInvoice`.
   - `Cfdi` lleva lo que comparte todo CFDI timbrado y sus transiciones: serie, folio, fecha, llave de idempotencia, estado, documento, UUID, archivos, rechazo, cancelación y versión.
   - `CfdiInvoice` conserva la forma, el método, los importes y quién la pidió.
   - El timbrado (`OrderInvoiceStamper`), el reintento y la revisión de cancelaciones del worker se vuelven genéricos sobre `Cfdi`. Las tablas existentes no cambian más que la restricción del método.
3. **El agregado `PaymentComplement`** (`ITenantScoped`, CFDI tipo P) cubre un cobro (`OrderPayment`) del pedido de una factura PPD timbrada.
   - Guarda la factura, el pedido, el cobro, la fecha y forma del cobro, el monto, la parcialidad, el saldo anterior y el insoluto, y quién lo pidió (sin persona, lo emitió TienditApp).
   - Hay un solo complemento vivo (timbrando, timbrado o cancelándose) por cobro, garantizado por un índice único filtrado `(TenantId, OrderPaymentId)`. Otro índice impide dos vivos con la misma parcialidad de una factura.
   - Son únicos la serie y el folio por negocio, la llave y el UUID por negocio.
   - El dominio rechaza:
     - una factura que no es PPD o no está timbrada;
     - un cobro de otro negocio u otro pedido;
     - una forma sin equivalente del SAT;
     - una parcialidad fuera de 1 a 999;
     - un saldo anterior en cero, con fracciones de centavo o arriba del total;
     - un pago mayor al saldo.
4. **Serie propia:** `InvoicingProfile.PaymentSerie` («P» por omisión) se cambia en Configuración › Facturación junto a la de las facturas. El folio sale del contador de esa serie (`CFDI-{serie}`). Si el negocio elige la misma serie que sus facturas, comparten el contador y un folio nunca se repite.
5. **Parcialidad y saldos, en orden de emisión,** dentro del candado de facturación del negocio. Cada cobro que ya tuvo un complemento de la factura conserva la parcialidad de su último complemento, y su monto, aunque ese complemento esté rechazado o cancelado: el cobro sí se recibió, y su complemento se emite otra vez.
   - Un cobro nuevo toma la parcialidad más alta de los cobros que ya la tienen, más uno.
   - El que se emite otra vez recupera su número (si ningún vivo lo tomó), así que la cadena no tiene huecos: cancelar la parcialidad 1 de 2 y emitirla otra vez da otra 1, no una 3 desde $0.
   - El saldo anterior es el total de la factura menos los cobros de las parcialidades anteriores. Un cobro que llega mientras otro espera volver a emitirse sigue la cadena: con $300, la 1 de $100 cancelada y la 2 de $50, la 3 de $60 va de $150 a $90, no de $250 a $190.
   - Lo pagado es el monto del cobro sin propina. Un cobro mayor que lo que la factura todavía debe después de los demás cobros se rechaza.
   - Un cobro nunca tiene dos complementos vivos, ni de facturas distintas: si su complemento de una factura anterior sigue vigente, se cancela primero.
6. **El IVA de cada complemento** (`PaymentComplementTaxes.Prorate`) sigue la práctica de las fuentes:
   - por cada tasa de la factura (16 %, 8 %, 0 % y exento), BaseDR = redondeo(base × pago ÷ total), sin pasar de lo que queda de esa base, e ImporteDR = redondeo(BaseDR × tasa);
   - el pago que liquida lo que la factura todavía debe, cuando los complementos de todos los demás cobros están vigentes, toma lo que queda de cada base y de su IVA si cae dentro de los límites del SAT; si no, toma la tasa sobre esa base (mientras otro espera volver a emitirse, toma su parte como cualquiera, y el centavo lo absorbe ese otro al final);
   - una tasa cuya parte redondea a 0 no lleva traslado (el SAT no acepta una base de 0);
   - sin traslados, ObjetoImpDR es 01;
   - una tasa fuera de las que tienen campo en los Totales se rechaza (es inalcanzable: el PAC no la habría timbrado).
7. **El documento** (`CfdiDocument.ForPayment`) usa el emisor actual de Mi negocio, que tiene que tener el RFC de la factura, y el lugar de expedición actual. El receptor es el de la factura con CP01, y FechaPago es la hora local del cobro en la zona del negocio. El JSON de SW Sapien lleva `Complemento.Any` con `Pago20:Pagos` y los importes del comprobante en "0".
8. **Emisión automática:** el worker de facturación busca, en cada pasada, los cobros de facturas PPD timbradas sin ningún complemento para esa factura, solo en los negocios cuyo CSD sella en ese momento. Los pide y los timbra, del cobro más viejo al más nuevo: hasta 20 por negocio en cada pasada, de los 100 más viejos que revisa.
   - Un negocio sin CSD vigente no ocupa los lugares de los demás mientras tramita el nuevo, y un cobro que no se puede emitir todavía (el RFC de Mi negocio cambió, por ejemplo) no detiene a los más nuevos.
   - Si falta algo (los datos de Mi negocio, el PAC), espera y lo avisa una vez por pasada en el log.
   - Un complemento rechazado o cancelado solo se emite otra vez a mano: «Emitir otra vez» en Pedidos. «Emitir ahora» no espera al worker. Los dos usan el mismo comando y las mismas reglas.
9. **Formas sin complemento:**
   - un cobro con saldo a favor (30, la aplicación de un anticipo) y uno con «Otro» (99, que el SAT prohíbe en FormaDePagoP) no se emiten;
   - el cobro dice por qué, y la vista previa de la factura lo avisa.
10. **Timbrar y reintentar** son los de la 28A: se guarda el CFDI con su llave antes de llamar al PAC, el PAC se llama después del commit, y sin respuesta queda «Timbrando». «Volver a pedir el timbre» o el worker mandan el mismo CFDI.
    - Dos personas que emiten el complemento del mismo cobro a la vez: la segunda espera el candado y encuentra el primero.
    - Si aun así choca con el índice, la API responde «Otra persona está emitiendo el complemento de este cobro» y el folio regresa al contador.
11. **Cancelar** un complemento: dueño o administrador, motivo 02 (errores) o 03 (el cobro no se llevó a cabo). Se pide primero al PAC y queda «Cancelándose» si el SAT espera al receptor; el worker lo resuelve. Nunca se borra.
    - Una factura PPD con complementos vigentes no se cancela: la API dice cuáles hay que cancelar primero.
12. **Acceso:** ver, emitir, reintentar y descargar el XML y el PDF de un complemento lo hace quien gestiona los pedidos (`ManageOrders`) y ve el pedido, como sus facturas. Cancelar lo hacen dueños y administradores (`RequiresTenantManager`). El complemento de otro negocio responde 404 (suite entre negocios).
13. **Se ve en Pedidos › Factura:** debajo de las facturas, «Complementos de pago» lista cada cobro con su fecha, forma y monto. Con él va:
    - su complemento: folio, estado, parcialidad, lo pagado, el saldo anterior y el que queda, folio fiscal y si lo emitió TienditApp;
    - mientras no tenga uno vivo, «El SAT lo pide a más tardar el …» (o «El plazo del SAT … venció el …»), con «TienditApp lo emite en unos minutos» si lo emite por su cuenta, y junto con el motivo si no se emite;
    - las acciones: «Emitir ahora» o «Emitir otra vez», «Volver a pedir el timbre», «Cancelar complemento» y las descargas.

    La consulta se vuelve a leer cada 30 segundos mientras algo espera al PAC, al SAT o al worker.
14. **La API se mantiene compatible:**
    - Rutas nuevas:
      - `POST /api/v1/invoicing/payments/{paymentId}/complements`;
      - `POST /api/v1/invoicing/payment-complements/{id}/retry` y `/cancel`;
      - `GET /api/v1/invoicing/payment-complements/{id}/xml|pdf`.
    - Campos nuevos: `OrderInvoicingDto.payments`, `InvoicingSettingsDto.paymentSerie` y `paymentSerie` opcional al cambiar la serie.
    - Nada se quita ni cambia de nombre. Lo único que cambia de comportamiento: una venta fiada o entregada que se debe ya no responde «no está pagada»; se factura en parcialidades.
15. **Fuera de esta fase:**
    - sustitución con motivo 01;
    - un complemento que cubra varias facturas (un abono que liquida varias ventas lleva un complemento por venta);
    - otras monedas y la global de ventas a crédito;
    - notas de crédito: lo condonado o reembolsado después de una PPD queda para la 28D;
    - un reporte de complementos por periodo, y mandarlos por correo o WhatsApp.
16. **La migración** `20261024014559_AddPaymentComplements`:
    - `PaymentComplements` con sus restricciones (estado, folio, importes y saldos que cuadran, parcialidad, forma distinta de 99 y 30, timbre, rechazo, cancelación y motivo 02/03), sus índices y sus llaves a negocio, factura, pedido y cobro;
    - `InvoicingProfiles.PaymentSerie` (`'P'` por omisión);
    - `CK_OrderInvoices_PaymentMethod` pasa de «solo PUE» a «PUE con forma real, o PPD con 99»; las facturas existentes (todas PUE con forma real) ya lo cumplen.

## Consecuencias

- **Lo que logra:**
  - El cliente que se lleva fiado tiene su factura el día de la compra, y cada abono tiene su complemento sin que el negocio lo arme a mano.
  - Los complementos de una factura suman su total, en bases y en IVA.
  - Un cobro nunca tiene dos complementos vivos, ni una factura dos con la misma parcialidad.
- **Lo que limita:**
  - Lo emitido automáticamente sigue a la factura timbrada: un cobro registrado mientras la factura «Timbrando» espera al PAC se emite en cuanto quede timbrada.
  - Cancelar la factura revisa sus complementos vigentes antes de pedirlo al PAC, sin el candado: si el worker timbra un complemento justo en medio, el SAT rechaza la cancelación (la factura tiene un documento relacionado vigente) o el complemento queda de una factura cancelada; en ese caso el cobro no recibe otro mientras ese siga vigente, y se cancela a mano.
  - El receptor del complemento es el de la factura: si el cliente cambió su código postal o su régimen en el SAT, el PAC rechaza el complemento y el camino es cancelar los complementos y la factura y facturar otra vez.
  - Condonar o reembolsar después de timbrar la factura PPD se permite, como después de una PUE: la factura queda con un saldo que pide la nota de crédito de la 28D.
  - Los cobros «Otro» y con saldo a favor no tienen complemento; el contador decide.
  - Una venta con condonación o reembolso no se factura en parcialidades hasta que existan las notas de crédito.
  - El candado espera hasta 10 segundos; si otra factura del negocio tarda más, la persona lo intenta otra vez.
- **Compatibilidad durante el despliegue:**
  - La versión anterior (28B) funciona con la base migrada: no conoce la tabla nueva ni la columna `PaymentSerie` y no las escribe. Todas sus facturas son PUE con forma real, así que cumplen la restricción.
  - Si se vuelve a ella, las facturas PPD y sus complementos se quedan en la base y en el PAC. La 28B no emite complementos nuevos y deja cancelar una PPD con complementos vigentes, que el SAT rechaza.
- **Reversibilidad:**
  - `rollback.sql` quita la tabla y la columna, y regresa la restricción a «solo PUE», solo si no hay ningún complemento ni ninguna factura PPD.
  - En cuanto existe uno, se detiene con el error 50039 sin cambiar nada. Solo queda corregir hacia adelante.
