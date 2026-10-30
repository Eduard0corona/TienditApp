# ADR 0049: La factura le llega al cliente por correo

- Estado: Aceptada
- Fecha: 2026-10-26

## Contexto

Desde la Fase 28A (ADR 0045) el negocio timbra la factura de un pedido, desde la 28C (ADR 0047) los complementos de sus cobros y desde la 28D (ADR 0048) las notas de crédito de sus reembolsos y condonaciones. Pero nada se los entregaba al cliente: el negocio descargaba el XML y el PDF en Pedidos y los mandaba por su cuenta. El Código Fiscal (art. 29, fr. V) obliga a entregar o poner a disposición del cliente el XML y su representación impresa, y la deuda técnica lo tenía anotado («mandar la factura por correo o WhatsApp»).

TienditApp ya manda correos de las cuentas por SMTP desde el outbox (Fase 23A). WhatsApp necesita una plantilla aprobada por Meta y un documento adjunto por su API: queda para después.

Tomé las decisiones de la Fase 28E con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Qué se manda:** el XML y el PDF de cada CFDI **timbrado** de la factura de un pedido: la factura (ingreso), sus complementos de pago y sus notas de crédito. La global no: el público en general no tiene a quién. Las cancelaciones no se avisan: el SAT se las avisa al receptor en su buzón.
2. **El correo vive en la factura:** `OrderInvoice.ReceiverEmail` (opcional, una sola dirección, hasta 254 caracteres, recortada). No es dato del CFDI. Los complementos y las notas de esa factura van al correo que la factura tenga cuando se timbran.
   - `InvoiceEmailAddress` decide qué es una dirección: ASCII imprimible sin espacios, una sola @, un dominio con punto y sin punto final, y que `MailAddress` la lea igual y sin nombre visible. «Karla <karla@…>» o dos direcciones separadas por coma se rechazan, y también «peña@…»: el SMTP de la plataforma manda direcciones de siete bits y ese correo no saldría nunca.
3. **El cliente guarda su «Correo para facturas»:** `Customer.InvoiceEmail`, junto a sus datos fiscales, con la misma regla (quien gestiona clientes, `ManageCustomers`) y la misma auditoría: el registro dice si el correo cambió, nunca cuál es. Quitar los datos fiscales quita también el correo; lo cuida el dominio, no una restricción de la base (con ella, la versión anterior no podría quitar los datos fiscales de un cliente con correo durante el despliegue).
   - Al facturar se propone, como el receptor: el «Correo para facturas» del cliente, el de la última factura del pedido o el correo del cliente si es una dirección válida.
   - «Guardar estos datos en el cliente» guarda también el correo escrito; sin correo, el cliente conserva el suyo. Solo cuenta mientras se ven los datos del receptor, donde está la casilla.
   - En Clientes, guardar los datos fiscales con el correo vacío lo quita.
4. **Envío automático:** cuando un CFDI de la factura de un pedido pasa a «Timbrado» y su factura tiene correo, `OrderInvoiceStamper` agrega un `CfdiEmail` con su evento en el **mismo** `SaveChanges` del timbre. Si otro intento ganó el timbre (concurrencia), el `ChangeTracker.Clear()` de ese guardado descarta los dos: un solo correo por timbre. El timbre es el único lugar donde un CFDI pasa a «Timbrado», también desde el worker.
5. **El historial `CfdiEmail`** (`ITenantScoped`, nunca se borra; solo cambia su resultado): la factura a la que pertenece, el tipo y el id del CFDI, el destinatario, quién lo pidió (sin persona, lo mandó TienditApp), cuándo, los intentos que el servidor de correo no tomó, y cómo terminó:
   - **Enviado**, quizá sin el PDF;
   - **No se pudo** (`Failure`): `Rejected` (el servidor la rechazó para siempre, 5xx, o la dirección no se puede mandar), `NotConfigured` (la plataforma no tiene proveedor), `NoLongerStamped` (el CFDI ya no estaba vigente cuando salió) o `NotDelivered` (el servidor de correo no lo tomó en ninguno de los intentos del outbox);
   - **Enviándose** mientras ninguna de las dos.

   Restricciones de la base: el tipo (0, 1, 2), que el de una factura sea la factura misma, un solo resultado con su motivo (0 a 3), sin PDF solo si se envió, y los intentos no negativos.
6. **El handler del outbox** (`CfdiEmailRequestedHandler`), en el alcance del negocio del evento:
   - Un CFDI que ya no está timbrado termina el correo con `NoLongerStamped`, sin mandarlo.
   - Lee el XML guardado; el PDF, el guardado o el que el PAC genera en ese momento desde el XML (como la descarga).
   - Sin PDF espera, sin contarlo como intento fallido (lanza para que el outbox reintente):
     - en el primer intento de un correo de TienditApp, al PDF que el timbre guarda justo después, para no pedírselo al PAC dos veces;
     - en los dos primeros intentos, al PDF que el PAC no dio;
     - del tercero en adelante, o en el último intento del outbox, manda solo el XML y lo dice (el XML es el comprobante).
   - El correo sale **de la plataforma** (su dirección es la que el proveedor reconoce) con el nombre visible «{Negocio} vía TienditApp» y `Reply-To` al correo de contacto del negocio.
   - Asunto «Factura F-123 de {Negocio}», «Complemento de pago P-4 de …» o «Nota de crédito NC-2 de …», en una línea. El cuerpo dice el emisor y el receptor (nombre y RFC), el folio, el folio fiscal, el total (el monto del pago en un complemento), la fecha y el enlace de verificación del SAT. El texto del negocio y del CFDI va codificado (HTML).
   - Adjuntos `{Serie}-{Folio}_{UUID}.xml` y `.pdf`.
   - El resultado: enviado, `NotConfigured` o `Rejected` terminan el correo; un servidor que no responde o lo pospone (4xx, sin conexión, credenciales, más de 60 segundos) anota el intento y lanza, y el outbox lo reintenta.
   - En el último intento del outbox (el 8.º, unas 4.7 horas después del primero; `OutboxEventContext.IsLastAttempt`), lo que falle termina el correo como `NotDelivered` en lugar de dejarlo «Enviándose» para siempre.
7. **`IEmailService.SendDocumentsAsync`** devuelve cómo terminó (`Sent`, `NotConfigured`, `Rejected`) en lugar de tragarse el resultado como los correos de las cuentas, y lanza `EmailDeliveryException` (sin la dirección) para lo que puede funcionar después.
   - Un destinatario que no es una dirección o no es de siete bits es `Rejected` sin conectarse; un `Reply-To` así se omite y el correo sale igual.
   - El envío tiene un límite de 60 segundos: un servidor que no contesta no detiene el outbox.
   - El nombre del negocio se limpia para el encabezado (una línea, sin comillas). Las direcciones van enmascaradas en los logs fuera de Development.
8. **«Enviar por correo»** (quien gestiona los pedidos, `ManageOrders`, y ve el pedido): un CFDI timbrado de los de la decisión 1, a una dirección (se propone la de la factura), con «Usar este correo para lo que siga de esta factura» (cambia `ReceiverEmail`). La casilla viene marcada cuando la factura no tiene correo o el suyo fue rechazado.
   - Límites, antes de encolar y bajo el candado de facturación del negocio (`IInvoicingLock`): 5 por CFDI y 300 por negocio en 24 horas (los automáticos cuentan). El remitente es compartido: un negocio no puede mandar correos sin fin desde la plataforma.
   - El CFDI de otro negocio o de un pedido que la persona no ve responde 404.
9. **La API se mantiene compatible:**
   - Rutas nuevas: `POST /api/v1/invoicing/invoices/{id}/email`, `/payment-complements/{id}/email` y `/credit-notes/{id}/email` con `{ email, useForInvoice }`.
   - Campos nuevos: `receiverEmail` (opcional) al facturar; `invoiceEmail` en los datos fiscales del cliente y en `CustomerDto` (también en la lista de Clientes); `OrderInvoiceDto.receiverEmail`; `emails` (los 3 más recientes) en cada factura, complemento y nota de `OrderInvoicingDto`; `OrderInvoicingDto.proposedEmail`; los enums `CfdiEmailDocument` y `CfdiEmailFailure`.
   - Un cliente anterior no cambia nada: sin `invoiceEmail`, guardar los datos fiscales conserva el «Correo para facturas»; vacío, lo quita.
10. **Se ve en Pedidos › Factura:** cada factura, complemento y nota dice cómo salió su último correo («Se envió por correo a … el …», «solo con el XML», «Enviándose…» con «El servidor de correo no lo ha tomado; TienditApp lo vuelve a intentar» si ya falló alguna vez, «No se envió a …: …») y ofrece «Enviar por correo» mientras está timbrada. La facturación del pedido se vuelve a leer cada 30 segundos mientras el último correo de algún CFDI no termina. Facturar tiene el campo «Correo del cliente (opcional)» y Clientes › datos fiscales, «Correo para facturas». El cliente valida lo mismo que la API (ASCII incluido) antes de mandarlo.
11. **Fuera de esta fase:**
    - WhatsApp;
    - avisar la cancelación por correo;
    - un reporte de correos o reenviarlos en lote;
    - que el negocio mande desde su propio dominio (SPF, DKIM);
    - saber si el correo se abrió o rebotó después de que el servidor lo tomó.
12. **La migración** `20261026032721_AddCfdiEmails`:
    - `CfdiEmails` con sus restricciones, sus índices (`TenantId, CfdiId, RequestedAt`; `TenantId, RequestedAt`; `OrderInvoiceId`) y sus llaves al negocio y a la factura;
    - las columnas nulas `OrderInvoices.ReceiverEmail` y `Customers.InvoiceEmail`, sin restricción entre el correo y el RFC del cliente (decisión 3).

## Consecuencias

- **Lo que logra:**
  - El cliente recibe su factura, sus complementos y sus notas sin que el negocio los descargue y los mande, y el negocio ve en Pedidos si salió.
  - Un CFDI se manda una vez por timbre aunque dos intentos lo timbren a la vez, y el outbox reintenta lo que el servidor de correo no tomó.
- **Lo que limita:**
  - La entrega es **al menos una vez**: si el servidor tomó el correo y la base no pudo guardar el resultado, el outbox lo manda otra vez. Es preferible a perderlo. Lo mismo pasaba si un envío tardaba más que el apartado del outbox (120 segundos: hasta 60 del SMTP más el PDF del PAC) y otro dispatcher lo tomaba; desde la Fase 28K el apartado se renueva mientras el envío trabaja ([ADR 0056](0056-stamping-robustness.md)).
  - «Enviado» quiere decir que el servidor de correo lo tomó, no que llegó a la bandeja: un rebote posterior no regresa a TienditApp.
  - Un correo que el servidor nunca tomó tarda unas 4.7 horas en decir «No se envió»: mientras, dice «Enviándose…» y que el servidor no lo ha tomado, y se puede mandar otra vez.
  - Sin proveedor de correo configurado, cada CFDI timbrado con correo queda «No se envió: TienditApp no tiene el envío de correos configurado».
  - El correo sale de la dirección de la plataforma: algunos clientes de correo lo muestran «vía» la plataforma aunque el nombre sea el del negocio.
- **Compatibilidad durante el despliegue:**
  - La versión anterior (28D) funciona con la base migrada: no conoce la tabla ni las columnas nuevas y no las escribe. Puede quitar los datos fiscales de un cliente con correo (no hay restricción); el correo se queda y esta versión lo sigue proponiendo hasta que se guarden sus datos fiscales sin él.
  - Si se vuelve a ella, los correos se quedan en la base; los que estén en el outbox se apartan (la 28D no conoce su evento) y no salen: se quedan «Enviándose» al volver a esta versión, y se pueden mandar otra vez.
- **Reversibilidad:**
  - `rollback.sql` quita la tabla y las columnas solo si no hay ningún correo de CFDI ni correo guardado en una factura o un cliente.
  - En cuanto existe uno, se detiene con el error 50041 sin cambiar nada. Solo queda corregir hacia adelante.
