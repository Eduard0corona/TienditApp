# ADR 0054: Las notas de crédito de las ventas de una factura global

- Estado: Aceptada
- Fecha: 2026-10-28

## Contexto

Desde la Fase 28B (ADR 0046) el negocio timbra la factura global a público en general de un periodo, y desde la 28D (ADR 0048) cada reembolso y cada condonación de un pedido con factura propia lleva su nota de crédito (CFDI de egreso). Entre las dos quedaba un hueco:

- una venta al público con un reembolso **antes** de su global no entraba en ella (`RefundedReason`): sin datos fiscales del cliente se quedaba sin ningún CFDI;
- un reembolso **después** de la global, o el de una venta anulada que la global ya cubría, no tenía nota: la global seguía amparando ante el SAT lo que el negocio ya devolvió.

Lo que piden las fuentes primarias del SAT:

- **Guía de llenado del CFDI global 4.0** (rev. 1, 2023-03-08), apéndice 2, caso I: la devolución de una venta de una global se ampara con un CFDI de egreso relacionado con ella, a público en general.
- **Matriz de errores del Anexo 20:**
  - CFDI40130: el nodo `InformacionGlobal` solo va en un comprobante de tipo I;
  - CFDI40161: el uso del CFDI tiene que admitir el régimen del receptor, y el régimen 616 solo admite S01. El ejemplo de la guía dice G02, que el catálogo `c_UsoCFDI` no admite con 616.

Tomé las decisiones de la Fase 28I con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Qué lleva nota de crédito:** cada reembolso con forma del SAT de una venta que cubre una factura global timbrada lleva **una** nota de crédito relacionada con esa global. Cubre:
   - los reembolsos hechos después de timbrarla;
   - los de una venta anulada que la global cubre: la global no se cancela por una de sus ventas, así que la nota la deshace (no aplica el motivo 03 de la 28D, que es de la factura propia);
   - los hechos antes de que la venta entrara en la global (punto 2).

   Sin condonaciones: la global solo toma ventas pagadas por completo y sin condonar, y una venta pagada ya no se condona.
2. **La global toma las ventas devueltas en parte:** entran por su total, como la primera vez que la 28B emite otra vez una global cancelada, y cada reembolso lleva su nota.
   - Una venta devuelta completa sigue fuera: «Se devolvió completa: no hay nada que facturar».
   - Una venta con un reembolso sin forma del SAT («Otro» o con saldo a favor) queda fuera: ese reembolso nunca llevaría nota y la global declararía lo que se devolvió. El motivo pide consultar al contador.
   - El reembolso en línea en curso sigue fuera (su monto todavía puede cambiar).
   - La vista previa avisa cuántas ventas con devoluciones incluye.
   - Las ventas devueltas en parte que una global ya timbrada dejó fuera antes de esta fase entran en la siguiente, como ventas de periodos anteriores, con su aviso.
3. **El agregado `CreditNote`** (ADR 0048) acredita la factura de un pedido o una factura global, nunca las dos ni ninguna:
   - `OrderInvoiceId` pasa a ser opcional y se agrega `GlobalInvoiceId`;
   - `CreditNote.ForRefundInGlobalInvoice` exige una global timbrada con su UUID, la venta de esa global (no liberada), un reembolso de esa venta y del mismo negocio, con forma del SAT, y no más que lo que la venta cobró en la global;
   - la base lo garantiza con `CK_CreditNotes_Invoice`: la nota de una global es solo de un reembolso y sin parcialidad antes (`AfterInstallment` = 0).
4. **El documento** (`CfdiDocument.ForCreditNote`):
   - tipo E, método PUE, la forma del reembolso, serie y folio de las notas;
   - emisor: el actual de Mi negocio, que tiene que tener el RFC con el que se timbró la global; lugar de expedición: el código postal actual;
   - receptor: XAXX010101000 «PUBLICO EN GENERAL», con el lugar de expedición como domicilio fiscal, régimen 616 y uso S01;
   - sin `InformacionGlobal`;
   - `CfdiRelacionados` con TipoRelacion 01 y el UUID de la global;
   - un concepto 84111506, ACT, «Devolución» por cada tasa de IVA que la venta tenía en la global, más uno sin objeto para lo que no tenía traslados. Cada concepto lleva en `NoIdentificacion` el folio de la venta, como su concepto en la global.
5. **Los importes:** el total de la nota es exactamente lo reembolsado.
   - Se reparte con `CreditNoteTaxes.Split` (ADR 0048), con las tasas de los conceptos de la venta en la global (los que llevan su folio) y el total de la venta en ella.
   - Si la global no tiene conceptos de la venta, no se emite y lo dice.
6. **Cuánto puede acreditar:** lo que la venta cobró en la global menos las otras notas de sus reembolsos en esa global. Cuenta la última nota de cada reembolso aunque esté rechazada o cancelada, como en la 28D.
7. **Emisión automática:** el worker de facturación también busca los reembolsos de ventas de una global timbrada sin ninguna nota de esa global, con las mismas reglas, límites y turnos de la 28D. «Emitir ahora» y «Emitir otra vez» usan el mismo comando.
   - Una global que se timbra, o que espera su cancelación, todavía no lleva notas, y lo dice.
   - Si Mi negocio cambió de RFC, la nota no se emite desde TienditApp, y lo dice.
8. **Cancelar:**
   - la nota de una global solo se cancela con 02, como toda nota de reembolso;
   - una global con notas vivas no se cancela: la API dice cuáles van primero y Reportes › Factura global lo dice en lugar del botón (las timbradas se cancelan en su pedido; las que se timbran o se cancelan, que terminen);
   - en Pedidos, el aviso de una venta de la global que el cliente quiere facturar a su nombre nombra esas notas antes de pedir cancelar la global con 04.
9. **Nunca va por correo:** es para el público en general. Al timbrarla no se encola un correo, y «Enviar por correo» la rechaza.
10. **Dónde se ve:**
    - **Pedidos › Factura de la venta:** sus reembolsos con sus notas, «al público en general, de la factura global F-x», sin «Enviar por correo». El texto de arriba dice qué global ampara la venta.
    - **Reportes › Factura global › una:** «Notas de crédito», con el folio, la venta (enlace a Pedidos), el estado y el total. Se emiten, se cancelan y se descargan en el pedido.
    - **Reportes › CFDI emitidos** (ADR 0052): la nota de una global se relaciona con ella y su receptor es el público en general. Se encuentra por el folio de la venta.
11. **Acceso:** el de la 28D. Ver, emitir y descargar lo hace quien gestiona los pedidos y ve el pedido; cancelar, dueños y administradores. El detalle de la global es de dueños y administradores. Todo lee con el filtro del negocio; el worker lee sin él, cada conjunto unido por su negocio.
12. **La API se mantiene compatible:**
    - `CreditNoteDto.orderInvoiceId` puede ser nulo;
    - campos nuevos: `CreditNoteDto.globalInvoiceId` y `globalInvoiceNumber`, `GlobalInvoiceOrderDto.creditNotes` y `OrderInvoicingDto.globalInvoiceNumber`;
    - sin rutas nuevas, y nada se quita ni cambia de nombre;
    - cambia de comportamiento: la global toma ventas devueltas en parte, y una global con notas vivas no se cancela.
13. **La migración** `20261028013618_AddGlobalInvoiceCreditNotes`: `CreditNotes.OrderInvoiceId` acepta nulos y se agregan `GlobalInvoiceId` (con su llave foránea y un índice filtrado) y `CK_CreditNotes_Invoice`. No cambia datos. Su `Down` se detiene con el error 50044 en cuanto existe una nota de una global.
14. **Fuera de esta fase:**
    - una nota por descuento o bonificación sin reembolso (también la de una condonación de una venta de la global);
    - una nota que acredite varias ventas de la global a la vez;
    - sustituir la global o sus notas (motivo 01);
    - descargar o cancelar la nota desde el detalle de la global.

## Consecuencias

- **Lo que logra:**
  - Una venta al público devuelta en parte ya tiene su CFDI (la global) y su egreso, sin datos fiscales del cliente.
  - Lo que se devuelve después de la global queda declarado sin que el negocio arme el egreso a mano.
  - Una global nunca se cancela dejando notas relacionadas vigentes.
- **Lo que limita:**
  - La nota se emite con el código postal de hoy: si el negocio se mudó después de la global, el receptor y el lugar de expedición son los nuevos, como pide la guía.
  - Al desplegar, el worker emite con la fecha de ese día las notas de los reembolsos de ventas que ya cubre una global timbrada (la consulta 4 del `verify.sql` los cuenta). Si alguno ya tiene su egreso hecho a mano, su nota se cancela con 02.
  - La siguiente global de cada negocio puede traer ventas devueltas en parte de periodos anteriores (punto 2). La vista previa las cuenta antes de timbrar.
  - Cancelar la global revisa sus notas vivas antes de pedirlo al PAC, sin el candado de facturación (tomarlo obligaría a mantener la transacción abierta durante la llamada al PAC). Si el worker emite una nota justo entre esa revisión y la respuesta del SAT, pasa una de dos cosas: el SAT rechaza la cancelación, o la cancela y la nota queda relacionada con una global ya cancelada. En el segundo caso Pedidos la muestra vigente, con «Cancelar nota de crédito», y la nota del mismo reembolso en la siguiente global espera hasta que se cancele.
  - El formato de `CfdiRelacionados` en el JSON de SW Sapien sigue sin probarse en su ambiente de pruebas (ADR 0048).
- **Compatibilidad durante el despliegue:**
  - La versión anterior (28H) funciona con la base migrada: sigue emitiendo las notas de sus facturas con `OrderInvoiceId` y no conoce la columna nueva.
  - Volver a ella funciona mientras no exista ninguna nota de una global. Con una, esa versión no lee las notas de ese pedido (su `OrderInvoiceId` es nulo) y su detalle de facturación falla: conviene corregir hacia adelante. La consulta 3 del `verify.sql` las cuenta.
- **Reversibilidad:** `rollback.sql` deja la tabla como estaba solo si no hay ninguna nota de una global. En cuanto existe una (aunque esté rechazada), se detiene con el error 50044 sin cambiar nada.
