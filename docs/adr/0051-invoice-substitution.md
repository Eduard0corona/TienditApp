# ADR 0051: Sustituir una factura con errores (motivo 01)

- Estado: Aceptada
- Fecha: 2026-10-26

## Contexto

Desde la Fase 28A (ADR 0045) el negocio timbra la factura de un pedido, y desde la 28F (ADR 0050) el cliente la pide en línea con su ticket. La corrección más común es una factura con un dato mal: el RFC, el nombre, el código postal, el régimen o el uso del CFDI. Hasta ahora el negocio la cancelaba con el motivo 02 («Comprobante emitido con errores sin relación») y facturaba otra vez. Cuando hay un CFDI que la sustituye, el SAT pide otra cosa:

1. Primero se timbra el CFDI correcto con `CfdiRelacionados` `TipoRelacion` 04 («Sustitución de los CFDI previos») y el UUID del que tiene errores.
2. Después se cancela el que tiene errores con el motivo 01 («Comprobante emitido con errores con relación») y el UUID del nuevo como folio de sustitución.
3. Si el SAT no acepta el motivo 01, se cancela con el 02.

SW Sapien recibe el folio de sustitución en la ruta (`/cfdi33/cancel/{rfc}/{uuid}/01/{folioSustitucion}`). El SAT puede tardar en tener el CFDI recién timbrado (hasta 72 horas): mientras tanto responde que no lo relaciona.

`CfdiCancellationMotive.ErrorsWithReplacement` existía, pero ninguna factura lo aceptaba. Las ADR 0045, 0046, 0047 y 0048 dejaron fuera la sustitución.

Tomé las decisiones de la Fase 28G con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Solo la factura de un pedido** (`OrderInvoice`). La global, los complementos de pago y las notas de crédito se siguen cancelando con 02 o 03.
2. **Quién:** el dueño o un administrador (`RequiresTenantManager`), los mismos que cancelan. Guardar los datos fiscales en el cliente pide además `ManageCustomers`, como en Facturar.
3. **Cuándo se puede sustituir** (`OrderInvoiceSubstitutions.ProblemAsync`; el detalle de facturación del pedido dice por qué no, en `substitutionProblem`):
   - es la factura vigente del pedido, timbrada, que ninguna otra sustituye y sin cancelación pendiente;
   - no sustituye a otra que siga vigente: primero se cancela esa;
   - sin complementos de pago ni notas de crédito vigentes (el SAT tampoco la cancela con ellos);
   - con el mismo RFC del emisor con el que timbra hoy el negocio (el SAT relaciona CFDI de un mismo emisor);
   - el negocio puede timbrar (sus datos y su CSD vigente), el pedido se facturaría hoy en Pedidos (no cancelado, pagado o en parcialidades, sin reembolso en curso) y no está en una factura global vigente;
   - el CFDI que Pedidos timbraría ahora está listo.
4. **La que sustituye** es el CFDI que Pedidos timbraría ahora, con los datos fiscales y el correo que se escriben (se proponen los de la factura con errores), con `CfdiRelacionados` 04 y el UUID de la original. Lleva folio nuevo y sigue el mismo camino: «Timbrando», el worker si el PAC no contesta, el correo de la 28E al timbrarse y, si se pide, los datos guardados en el cliente. `OrderInvoiceRequester` es el mismo de Pedidos y de la autofactura, con `Replaces`.
5. **El modelo:** `OrderInvoices.ReplacedByInvoiceId` en la original.
   - Índice único filtrado `IX_OrderInvoices_ReplacedByInvoiceId`: una factura sustituye a una sola.
   - El índice de «una factura vigente por pedido» deja fuera a las sustituidas: `[Status] IN (0, 1, 3) AND [ReplacedByInvoiceId] IS NULL`. Así la original y la que la sustituye conviven mientras el SAT cancela la original.
   - `CK_OrderInvoices_CancellationMotive`: el motivo 01 solo en una sustituida. `CK_OrderInvoices_Replacement`: una factura no se sustituye a sí misma, y solo una timbrada, cancelándose o cancelada está sustituida.
   - **Sin llave foránea.** SQL Server revisa el índice único sentencia por sentencia: la original tiene que dejar de contar como vigente antes de insertar la que la sustituye. Con una llave foránea de la original a la nueva, la nueva tendría que existir antes. Por eso la solicitud guarda dos veces dentro de su transacción: primero marca la original y después inserta la nueva. La relación la cuida el dominio (`ReplaceWith`: mismo negocio y pedido, la nueva «Timbrando» y la original timbrada).
   - La que sustituye se reconoce porque la original apunta a ella. El DTO dice `replacesInvoiceId`/`replacesNumber` y `replacedByInvoiceId`/`replacedByNumber`.
6. **El orden:**
   1. En una transacción, bajo el candado de facturación del negocio (`IInvoicingLock`): el folio, la original sustituida, la nueva «Timbrando» y la auditoría `OrderInvoiceRequested` con `Replaces`. Si alguien cambió la original mientras tanto (su PDF, la revisión de su cancelación), no se guarda nada, ni el folio. Cualquier otro error al guardar también deshace todo y se lanza, para que la estrategia de ejecución reintente lo pasajero: con el candado nadie más factura el pedido, así que nunca es «otra persona está facturando».
   2. Después del commit se timbra la nueva, en la misma solicitud.
   3. Timbrada, se pide al SAT cancelar la original con 01 y el UUID de la nueva (`ReplacedInvoiceCanceller`), también en la misma solicitud. La auditoría registra `OrderInvoiceCancellationRequested` con el motivo 01 y la que la sustituye.
   4. Si la nueva se queda «Timbrando», se cancela la original en cuanto quede timbrada: en la misma solicitud de «Volver a pedir el timbre», o en la siguiente pasada de cancelaciones del worker si la timbra él.
   5. Si el PAC no contesta o el SAT todavía no relaciona las dos, la original sigue timbrada y guarda el motivo. El worker lo pide otra vez en su pasada de cancelaciones (cada 30 minutos) mientras la nueva tenga menos de 72 horas timbrada y la original no haya pedido su cancelación desde que se timbró la nueva (una que el receptor rechazó antes de sustituirla no cuenta). Después lo pide una persona: «Cancelar con relación» (`POST …/invoices/{id}/cancel-replaced`). Un error con una factura no detiene la pasada de las demás.
   - SW Sapien: 205 (el SAT todavía no tiene el CFDI) es «no disponible»; 207, 208, 212 y 312 con el motivo 01 son «no relacionada» y dicen que el SAT puede tardar hasta dos días en recibir la nueva.
7. **Una sustituta que el PAC rechaza** no existió: con el rechazo, la original deja de estar sustituida y vuelve a ser la factura del pedido; se corrige y se sustituye otra vez. La respuesta de «Sustituir» lo dice como el detalle del pedido: la rechazada no sustituye a ninguna. Se guarda en una transacción y en dos pasos, como la solicitud: primero la nueva queda rechazada y después regresa la original (en un solo guardado, SQL Server aplicaría las dos actualizaciones en el orden de sus llaves y la mitad de las veces chocaría con el índice de una factura vigente por pedido). Si alguien canceló la original sin relación (02) o porque la venta no se hizo (03) mientras la nueva esperaba al PAC, la original sigue cancelada y tampoco queda sustituida. La cancelación con 01 solo se pide con la nueva timbrada, así que nunca llega un rechazo después.
8. **Mientras la original está sustituida:**
   - Ya no es la factura vigente del pedido (`IsCurrent`): los complementos, las notas de crédito y la autofactura leen la nueva. Un reembolso o una condonación esperan a que la nueva quede timbrada y su nota se relaciona con ella. La venta sigue fuera de la factura global mientras cualquiera de las dos esté vigente.
   - La nueva no se cancela mientras la original siga vigente (la cancelación de la original la nombra), ni se sustituye: primero se cancela la original, con relación (01) o sin ella (02).
   - La original se puede cancelar con 02 o 03 en cualquier momento, como dice el SAT cuando el 01 no pasa. El diálogo de cancelar dice que la nueva la sustituye y llama al 02 «sin relacionarla con la F-n».
9. **El receptor no acepta la cancelación con 01:** la original vuelve a timbrada, con el motivo, y sigue sustituida. Quedan dos facturas vigentes hasta que el negocio decida: pedir otra vez «Cancelar con relación» o cancelar la original con 02. El worker no lo intenta otra vez.
10. **En Pedidos:**
    - «Sustituir factura» en la factura vigente (dueños y administradores) abre el formulario de Facturar con los datos y el correo de esa factura. Arriba dice qué va a pasar; «No sustituir» regresa al formulario de siempre. El botón principal es «Timbrar y sustituir la F-n».
    - Si el PAC la rechaza, el formulario sigue abierto: la original sigue vigente.
    - Las tarjetas dicen «Sustituye a la factura F-n» y «Sustituida por la factura F-n», que la original se cancela con relación cuando la nueva quede timbrada (o en la siguiente revisión, si ya lo está), y el motivo si el SAT no la tomó. Mientras la original espera su cancelación, el detalle se vuelve a leer cada 30 segundos.
    - «Cancelar con relación» aparece con la nueva timbrada. «Cancelar factura» (02 o 03) en la original aparece cuando el 01 no pasó o el receptor lo rechazó, y no se ofrece mandarla por correo: la que le sirve al cliente es la nueva.
11. **La API se mantiene compatible:** rutas nuevas `POST /api/v1/invoicing/invoices/{invoiceId}/substitute` (el mismo cuerpo que facturar: receptor, correo y guardar en el cliente; responde la nueva) y `POST /api/v1/invoicing/invoices/{invoiceId}/cancel-replaced` (responde la original), y campos nuevos en `OrderInvoiceDto` (`replacedByInvoiceId`, `replacedByNumber`, `replacesInvoiceId`, `replacesNumber`, `canSubstitute`, `substitutionProblem`). `IInvoicingGateway.CancelAsync` recibe el folio de sustitución; con el motivo 01 es obligatorio y con otro no se acepta.
12. **Fuera de esta fase:**
    - sustituir la global, un complemento de pago o una nota de crédito;
    - sustituir una factura con complementos o notas vigentes (habría que emitirlos otra vez relacionados con la nueva);
    - cambiar el emisor o sustituir varias facturas con una;
    - cancelar dentro del candado de facturación: la cancelación llama al PAC, y tener el candado durante esa llamada detendría al negocio. Queda como deuda, igual que la cancelación de hoy.
13. **La migración** `20261026145713_AddInvoiceSubstitution`: la columna, los dos índices y las dos restricciones de la decisión 5. Su `Down` se detiene con el error 50043 si alguna factura está sustituida.

## Consecuencias

- **Lo que logra:**
  - El negocio corrige una factura como lo pide el SAT, sin perder la relación entre la que tenía errores y la buena.
  - Si el SAT tarda en recibir la nueva, TienditApp lo intenta solo durante tres días y dice por qué no pasó.
  - Lo que sigue (complementos, notas, autofactura) va a la factura buena.
- **Lo que limita:**
  - Mientras el SAT no cancela la original, el pedido tiene dos facturas vigentes ante el SAT. El receptor puede negarse a la cancelación; entonces lo resuelve el negocio.
  - Una factura con complementos o notas vigentes no se sustituye: primero se cancelan.
  - La cancelación se pide fuera del candado de facturación (decisión 12).
- **Compatibilidad durante el despliegue:**
  - La versión anterior (28F) funciona con la base migrada mientras nadie sustituya: no conoce la columna, que queda en nulo.
  - Si se vuelve a ella después de sustituir, verá dos facturas vigentes en el pedido, su worker no pedirá el motivo 01, y podría relacionar una nota o un complemento con la sustituida o dejar cancelar la nueva antes. Antes de volver, se cancelan con 02 las sustituidas que sigan vigentes; conviene no sustituir mientras se prueba el despliegue.
- **Reversibilidad:**
  - `rollback.sql` quita la columna, los índices y las restricciones, y regresa el índice de una factura vigente por pedido, solo si ninguna factura está sustituida.
  - En cuanto una lo está, se detiene con el error 50043 sin cambiar nada: quitarla perdería la relación y dejaría pedidos con dos facturas vigentes que el índice anterior rechaza. Solo queda corregir hacia adelante.
