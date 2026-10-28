# ADR 0048: Las notas de crédito (CFDI de egreso) de la factura de un pedido

- Estado: Aceptada
- Fecha: 2026-10-24

## Contexto

Desde la Fase 28A (ADR 0045) el negocio timbra la factura de un pedido, desde la 28B (ADR 0046) la global del periodo y desde la 28C (ADR 0047) la factura en parcialidades con los complementos de sus cobros. Lo que se devolvía o se perdonaba después quedaba fuera: una venta con reembolsos (Fase 2) o con una condonación (Fase 26B) no se podía facturar, y una ya facturada seguía amparando ante el SAT un total que el cliente ya no pagó. El SAT resuelve ese caso con el **CFDI de egreso** (tipo E), la nota de crédito relacionada con la factura.

Lo que piden las fuentes primarias (Anexo 20 rev. 3, Guía de llenado del CFDI, catálogos del SAT y matriz de errores de julio de 2026):

- **El comprobante:** tipo E, método de pago PUE, la forma con la que se efectúa el egreso (15 «Condonación» cuando no hay dinero de por medio; nunca 99), moneda MXN y Exportación 01.
- **La relación:** CfdiRelacionados con TipoRelacion 01 «Nota de crédito de los documentos relacionados» y el UUID de la factura.
- **El receptor:** el de la factura, con uso G02 «Devoluciones, descuentos o bonificaciones» si su régimen lo admite.
- **Los conceptos:** 84111506 con ClaveUnidad ACT, y los traslados de IVA de lo que se acredita, dentro de los límites de redondeo de su base.
- **La cancelación:** motivo 02 (errores sin relación) o 03 (la operación no se llevó a cabo). El SAT no cancela una factura mientras tenga documentos relacionados vigentes.

Tomé las decisiones de la Fase 28D con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Qué lleva nota de crédito:** cada reembolso (`OrderRefund`) y cada condonación que sigue en pie (`OrderWriteOff` sin deshacer) del pedido de una factura timbrada lleva **una** nota de crédito, también los que se hicieron antes de timbrarla.
   - Se levanta el bloqueo de la 28A: una venta con reembolsos o con condonación ya se factura (por la venta completa, PUE o PPD) y cada ajuste lleva su nota. La vista previa lo avisa.
   - Sigue bloqueado el reembolso en línea en curso.
   - Un pedido cancelado no lleva notas: la venta no se llevó a cabo y su factura se cancela con el motivo 03. Sus reembolsos (los de «Anular venta») lo dicen en lugar de emitirse.
   - La global sigue sin ventas con ajustes: la vista previa dice que se facturen en Pedidos si el cliente da sus datos fiscales, o que se consulte al contador (el egreso relacionado con una global queda fuera; llegó en la Fase 28I, [ADR 0054](0054-global-invoice-credit-notes.md)).
2. **El agregado `CreditNote`** (`ITenantScoped`, CFDI tipo E) hereda de `Cfdi` (ADR 0047) el estado, el documento, el timbre, los archivos, el rechazo y la cancelación.
   - Guarda la factura, el pedido, el reembolso o la condonación (exactamente uno), la forma, el subtotal, el IVA, el total, la parcialidad más alta de los complementos al pedirla (`AfterInstallment`; la que se emite otra vez conserva la de la nota anterior del ajuste en esa factura, para que los complementos que la descontaron la sigan descontando) y quién la pidió (sin persona, la emitió TienditApp).
   - Hay una sola nota viva (timbrando, timbrada o cancelándose) por ajuste, garantizado por dos índices únicos filtrados. Son únicos la serie y el folio por negocio, la llave y el UUID por negocio.
   - El dominio rechaza:
     - una factura que no está timbrada;
     - un ajuste de otro negocio u otro pedido, o una condonación deshecha;
     - un reembolso sin forma del SAT («Otro», o devuelto como saldo a favor del cliente);
     - un total en cero, con fracciones de centavo o mayor que la factura;
     - un subtotal y un IVA que no suman el total.
3. **Serie propia:** `InvoicingProfile.CreditNoteSerie` («NC» por omisión) se cambia en Configuración › Facturación junto a las de las facturas y los complementos. El folio sale del contador de esa serie: si coincide con otra, comparten el contador y un folio nunca se repite.
4. **El documento** (`CfdiDocument.ForCreditNote`):
   - usa el emisor actual de Mi negocio, que tiene que tener el RFC con el que se timbró la factura, y el lugar de expedición actual;
   - el receptor es el de la factura, con uso G02 (o S01 si su régimen no admite G02);
   - método PUE; forma: la del SAT del reembolso (la misma tabla que los complementos) o 15 para una condonación;
   - CfdiRelacionados con TipoRelacion 01 y el UUID de la factura;
   - un concepto 84111506, ACT, «Devolución» o «Condonación» por cada tasa de IVA de la factura, más uno sin objeto de impuesto (01) para lo que la factura tiene sin traslados.

   El JSON de SW Sapien lleva `CfdiRelacionados` antes del emisor, como arreglo de `{TipoRelacion, CfdiRelacionado: [{UUID}]}`, el mismo mapeo que el XML.
5. **Los importes** (`CreditNoteTaxes.Split`): el total de la nota es **exactamente** lo reembolsado o condonado.
   - Se reparte entre las tasas de la factura según lo que cada una pesa en su total; la última toma el resto. Si el redondeo dio de más y a la última no le alcanza (por ejemplo, redondeó a 0), las anteriores devuelven los centavos, de la última a la primera, sin quedar abajo de 0.
   - En cada tasa, la base es la parte sin IVA, redondeada, y el IVA es lo que queda de la parte, dentro de los límites del SAT.
   - Una tasa cuya parte redondea a 0 no lleva traslado (el SAT no acepta una base de 0).
6. **Cuánto puede acreditar una nota:**
   - **De un reembolso:** el total de la factura menos las otras notas que cuentan, de los dos tipos. La factura nunca vale menos de cero.
   - **De una condonación:** lo mismo, y además no más de lo que la factura todavía debe: su total menos los cobros con complemento y menos las otras notas de condonación que cuentan.
   - Cuenta la última nota de cada ajuste aunque esté rechazada o cancelada: el ajuste sí ocurrió y su nota se emite otra vez. No cuentan las de una condonación deshecha.
   - Un ajuste mayor que lo que queda no se emite, y dice por qué.
7. **En la factura PPD** solo cuentan las notas de condonación (no hay dinero: bajan lo que se debe). Un reembolso devuelve dinero que ya se recibió y que su complemento ya declaró; en TienditApp tampoco baja el saldo del pedido.
   - El saldo anterior de la parcialidad n es el total menos los cobros de las parcialidades anteriores y menos las notas de condonación con `AfterInstallment` < n.
   - Lo que se debe, para el tope de un cobro, resta todas las notas de condonación que cuentan.
   - El cobro que liquida (y toma el resto de cada base) exige además que esas notas estén vigentes.
   - El IVA ya cubierto incluye el de las notas de condonación vigentes.
8. **Emisión automática:** el worker de facturación busca, en cada pasada, los reembolsos (con forma del SAT) y las condonaciones en pie de pedidos con factura timbrada sin ninguna nota para esa factura, solo en los negocios cuyo CSD sella. Los pide y los timbra, del más viejo al más nuevo: hasta 20 por negocio en cada pasada, de los 100 más viejos que revisa.
   - Un negocio cuya última pasada no timbró ninguna nota va después de los demás, y esos negocios se turnan, como con los complementos.
   - Una nota rechazada o cancelada solo se emite otra vez a mano («Emitir otra vez»); «Emitir ahora» no espera al worker. Los dos usan el mismo comando y las mismas reglas.
   - Dentro del candado, el worker vuelve a revisar que el ajuste nunca haya tenido nota de esa factura: si una persona la emitió mientras la pasada avanzaba y el PAC la rechazó, o se canceló, el worker no emite otra.
9. **Timbrar y reintentar** son los de la 28A, dentro del candado de facturación del negocio: se guarda el CFDI con su llave antes de llamar al PAC, el PAC se llama después del commit, y sin respuesta queda «Timbrando».
   - Dos personas (o una persona y el worker) que emiten la nota del mismo ajuste a la vez: la segunda espera el candado y encuentra la primera.
   - Si aun así choca con el índice, la API responde «Otra persona está emitiendo la nota de crédito de este ajuste» y el folio regresa al contador.
10. **Cancelar** una nota: dueño o administrador. Se pide primero al PAC y queda «Cancelándose» si el SAT espera al receptor; el worker la resuelve. Nunca se borra.
    - La de un reembolso solo se cancela con 02 (errores): un reembolso registrado en TienditApp no se deshace, así que «no se llevó a cabo» (03) no le corresponde. La de una condonación, con 02 o con 03 antes de deshacerla.
    - Una factura con notas vivas no se cancela: la API dice cuáles cancelar primero, y Pedidos lo dice en lugar del botón.
    - Una condonación con nota viva no se deshace: primero se cancela la nota. Deshacerla toma el candado de facturación después del del fiado.
    - Tampoco se deshace mientras un complemento posterior a su nota (de una parcialidad mayor que la que tenía al pedirla) siga vigente: su saldo anterior ya la descuenta. Esos complementos se cancelan primero y se emiten otra vez sin ella.
11. **Acceso:** ver, emitir, reintentar y descargar el XML y el PDF de una nota lo hace quien gestiona los pedidos (`ManageOrders`) y ve el pedido. Cancelar lo hacen dueños y administradores (`RequiresTenantManager`). La nota de otro negocio responde 404 (suite entre negocios).
12. **Se ve en Pedidos › Factura:** debajo de los complementos, «Notas de crédito» lista cada reembolso («Reembolso del …», con su forma) y cada condonación («Condonación del …», con su motivo, «Deshecha» si se deshizo), con su monto y su nota.
    - La nota muestra: folio, estado, «Por $X (subtotal $Y e IVA $Z)», forma, folio fiscal y si la emitió TienditApp.
    - Sin nota viva: «Sin nota de crédito todavía: TienditApp la emite en unos minutos», o el motivo por el que no se emite. El motivo se calcula con las mismas reglas con que se rechazaría emitirla: forma, factura, Mi negocio y su CSD, RFC de la factura, condonación deshecha, lo que queda.
    - Las acciones: «Emitir ahora» o «Emitir otra vez», «Volver a pedir el timbre», «Cancelar nota de crédito» y las descargas.
    - Una nota de una factura que ya no es la timbrada dice de cuál fue.
    - Una factura con complementos o notas vigentes dice «Para cancelarla, primero cancela …» en lugar de ofrecer cancelarla.
    - La consulta se vuelve a leer cada 30 segundos mientras algo espera al PAC, al SAT o al worker.
13. **La API se mantiene compatible:**
    - Rutas nuevas:
      - `POST /api/v1/invoicing/refunds/{refundId}/credit-notes` y `/write-offs/{writeOffId}/credit-notes`;
      - `POST /api/v1/invoicing/credit-notes/{id}/retry` y `/cancel`;
      - `GET /api/v1/invoicing/credit-notes/{id}/xml|pdf`.
    - Campos nuevos: `OrderInvoicingDto.adjustments`, `InvoicingSettingsDto.creditNoteSerie`, `creditNoteSerie` opcional al cambiar la serie, y el enum `CreditNoteAdjustment`.
    - Nada se quita ni cambia de nombre. Lo que cambia de comportamiento: una venta con reembolsos o condonación ya se factura; cancelar una factura con notas vivas o deshacer una condonación con nota viva se rechaza.
14. **Fuera de esta fase:**
    - el egreso relacionado con una global;
    - una nota por descuento o bonificación sin reembolso;
    - una nota que acredite varias facturas o varios ajustes a la vez;
    - otras monedas, sustitución con motivo 01;
    - mandar la nota por correo o WhatsApp, y un reporte de notas por periodo.
15. **La migración** `20261024084435_AddCreditNotes`:
    - `CreditNotes` con sus restricciones (estado, folio, exactamente un ajuste, importes que cuadran, parcialidad, timbre, rechazo, cancelación y motivo 02/03), sus índices y sus llaves a negocio, factura, pedido, reembolso y condonación;
    - `InvoicingProfiles.CreditNoteSerie` (`'NC'` por omisión).

## Consecuencias

- **Lo que logra:**
  - Lo que se devuelve o se perdona después de facturar queda declarado sin que el negocio arme el egreso a mano, y una venta con ajustes ya se factura.
  - Las notas de una factura nunca suman más que su total, y las de condonación nunca más que lo que todavía se debe.
  - La cadena de saldos de los complementos de una PPD sigue cuadrando después de una condonación.
- **Lo que limita:**
  - El formato de `CfdiRelacionados` en el JSON de SW Sapien sigue el mapeo del XML y no se ha probado en su ambiente de pruebas: si el PAC lo rechazara, las notas quedarían «Rechazadas» con su motivo (sin efecto en las facturas) hasta corregir el mapeo. Se prueba con el primer negocio en pruebas.
  - La nota sigue a la factura timbrada: un ajuste registrado mientras la factura espera al PAC se emite en cuanto quede timbrada. Si la factura se cancela y se factura otra vez, el ajuste lleva otra nota relacionada con la nueva.
  - Cancelar la factura revisa sus notas vivas antes de pedirlo al PAC, sin el candado: si el worker timbra una nota justo en medio, el SAT rechaza la cancelación o la nota queda de una factura cancelada, y se cancela a mano.
  - Al desplegar, la primera pasada del worker emite, con la fecha de ese día, las notas de los reembolsos y condonaciones que ya tenían factura timbrada (la consulta 4 del `verify.sql` los cuenta). Si alguno ya tiene su CFDI de egreso hecho a mano, su nota se cancela con 02.
  - El receptor es el de la factura: si el cliente cambió su código postal o su régimen en el SAT, el PAC rechaza la nota.
  - Los reembolsos «Otro» o como saldo a favor no llevan nota desde aquí; el contador decide.
  - Una condonación hecha cuando la factura ya no debe nada (los cobros con complemento la cubrieron) no lleva nota: la pantalla lo dice.
  - El candado espera hasta 10 segundos; si otra factura del negocio tarda más, la persona lo intenta otra vez.
- **Compatibilidad durante el despliegue:**
  - La versión anterior (28C) funciona con la base migrada: no conoce la tabla nueva ni la columna `CreditNoteSerie` y no las escribe.
  - Si se vuelve a ella, las notas se quedan en la base y en el PAC. La 28C vuelve a bloquear la factura de ventas con ajustes, deja cancelar una factura con notas vigentes (el SAT la rechaza) y deja deshacer una condonación con nota vigente (se cancela a mano).
  - Además, la 28C no conoce las notas de condonación: el saldo anterior de los complementos que emita después de una queda alto y la factura no se liquida. Con notas de condonación timbradas, es mejor corregir hacia adelante.
- **Reversibilidad:**
  - `rollback.sql` quita la tabla y la columna solo si no hay ninguna nota de crédito.
  - En cuanto existe una (aunque esté rechazada), se detiene con el error 50040 sin cambiar nada. Solo queda corregir hacia adelante.
