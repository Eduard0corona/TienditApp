# ADR 0058: La factura le llega al cliente por WhatsApp

- Estado: Aceptada
- Fecha: 2026-10-30

## Contexto

Desde la Fase 28E ([ADR 0049](0049-cfdi-email.md)) el XML y el PDF de cada CFDI de la factura de un pedido le llegan al cliente por correo. Muchos clientes de un negocio local no dan correo, pero sí su WhatsApp, y el negocio ya habla con ellos por ahí: la deuda técnica lo tenía anotado («mandar la factura por WhatsApp»).

TienditApp ya manda mensajes de WhatsApp desde la cola `OutgoingMessage` (Fase 2) con la API de Meta del número del negocio: campañas, recordatorios de citas y del fiado. Meta solo entrega fuera de las 24 horas desde el último mensaje del cliente lo que va en una plantilla aprobada, y una plantilla puede llevar un documento en su encabezado. El XML no se puede adjuntar: WhatsApp solo acepta PDF, Office y texto como documento.

Tomé las decisiones de la Fase 28M con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Qué se manda:** el PDF de cada CFDI **timbrado** de la factura de un pedido (la factura, sus complementos de pago y sus notas de crédito) como documento, con un texto que lleva un enlace para descargar el XML y el PDF. La global y sus notas no: el público en general no tiene a quién. Las cancelaciones no se avisan.
2. **El WhatsApp vive en la factura:** `OrderInvoice.ReceiverWhatsApp` (opcional, un número en E.164 de 9 a 16 caracteres, `InvoiceWhatsApp`). Se escribe como en el registro: 10 dígitos si es de México, o con su lada internacional (`PhoneNumberRules.TryNormalizeTypedPhone`). Los complementos y las notas van al número que la factura tenga cuando se timbran. No se guarda en el cliente: su teléfono ya está en su ficha y en el pedido.
3. **Solo con el WhatsApp del negocio conectado** (`CampaignRules.IsWhatsAppConnectedAsync`): facturar o sustituir con un número, o «Enviar por WhatsApp», sin la conexión responde «Conecta el WhatsApp de tu negocio en Configuración › WhatsApp…». Al facturar se propone el último WhatsApp de una factura del pedido o el teléfono del pedido, solo con la conexión.
4. **El mensaje va por la cola** con el propósito nuevo `MessagePurpose.CfdiDocument` (5), `OutgoingMessage.ForCfdi`: el tipo y el id del CFDI, su factura, quién lo pidió (sin persona, lo mandó TienditApp) y su enlace. Cuando un CFDI pasa a «Timbrado», su factura tiene WhatsApp y el del negocio sigue conectado, `OrderInvoiceStamper` agrega el mensaje en el **mismo** `SaveChanges` del timbre, como el correo: un mensaje por timbre aunque dos intentos lo timbren a la vez. Agregarlo nunca detiene el timbre. Desconectado, no se encola: solo fallaría y contaría para el límite del día.
   - **La baja no lo detiene:** quien escribió BAJA no recibe campañas ni recordatorios, pero su factura es un documento que el negocio le debe entregar y que el cliente pidió al dar su número.
   - Restricciones de la base: el propósito 5 lleva el tipo, el CFDI, la factura y el enlace, y ningún otro propósito los lleva; el tipo (0, 1, 2); el de una factura es la factura misma; el enlace mide 43 caracteres.
5. **El texto:** la plantilla del negocio para sus facturas (Configuración › Facturación, `InvoicingProfile.WhatsAppTemplateId`) o el de TienditApp: «Hola {nombre}: {negocio} te envía {comprobante} por {monto}, del {fecha}. Descarga el XML y el PDF aquí: {enlace}».
   - Las plantillas de facturas tienen el uso «Factura por WhatsApp» en Campañas › Plantillas y solo esas variables. `{enlace}` solo existe en ellas y es obligatorio: sin él, el cliente recibiría el PDF sin manera de descargar el XML.
   - Las escriben solo el dueño y los administradores (`MessageTemplateLimits.EnsureCanWriteAsync`), como los recordatorios del fiado: elegir el texto de las facturas en Configuración › Facturación ya es suyo, y gestionar campañas no alcanza.
   - Una plantilla que, escrita con los datos del CFDI, pasa de 900 caracteres no se toma: va el texto de TienditApp. WhatsApp acepta 1,024 en el pie y en el cuerpo, y la dirección de la app se agrega al enlace al salir; cortarla perdería el enlace del final.
   - Con una plantilla aprobada en Meta, el PDF va en su encabezado de tipo documento y las variables en el cuerpo, cada una en una línea (Meta rechaza saltos de línea y tabuladores en una variable). Sin plantilla aprobada, el PDF va como documento con el texto como pie (hasta 1,024 caracteres) y WhatsApp solo lo entrega en las 24 horas desde el último mensaje del cliente: Configuración › Facturación lo advierte.
   - Una plantilla inactiva o de otro uso no se toma: va el texto de TienditApp.
6. **El enlace:** `/factura#{enlace}`, un token de 43 caracteres Base64Url (el de los enlaces del seguimiento, imposible de adivinar), único en la base (índice filtrado) y que funciona 90 días desde que se encoló. Va en el fragmento, que el navegador nunca manda a un servidor. El mensaje lo guarda sin la dirección de la app y se la agrega al salir (`IAppLinks.BaseUrl`).
7. **La página pública `/factura`** (sin cuenta): `POST /api/v1/public/cfdi` con `{ token }` dice el negocio (su nombre y su logo), el tipo, la serie y el folio, el folio fiscal, el total (el pago en un complemento), la fecha, el emisor y el receptor, si sigue vigente y hasta cuándo funciona el enlace. `POST /api/v1/public/cfdi/files` con `{ token, kind }` descarga el XML o el PDF (el guardado o el que el PAC arma en ese momento).
   - Un enlace desconocido, vencido, de un negocio que ya no está activo o de un CFDI que nunca se timbró responde 404. Uno cuyo CFDI se pidió cancelar o se canceló se muestra, pero no se descarga: «Este CFDI ya no está vigente: el negocio pidió cancelarlo. Si lo sustituyó por otro, pídeselo.» (no todos tienen uno que los sustituya).
   - La búsqueda del enlace cruza negocios antes de que haya uno en contexto (`PublicCfdiLinks`, en la lista de `IgnoreQueryFilters`): solo lee el negocio, el CFDI y cuándo se encoló, y el CFDI se lee ya dentro de su negocio. La comparación es exacta (la columna compara sin mayúsculas).
   - El límite de peticiones del menú público (`public-menu`), `no-store` y `Content-Security-Policy: sandbox` en los archivos.
8. **El envío** (`CfdiWhatsAppSender`, desde `OutgoingMessageSender`):
   - Un CFDI que ya no está timbrado se omite con «El CFDI ya no está vigente.».
   - El PDF es el guardado. El mensaje de TienditApp espera el PDF que el timbre guarda justo después: mientras no está y no han pasado 2 minutos desde que se encoló, el envío se **pospone** 20 segundos (`OutgoingSendDecision.Postponed`, `OutgoingMessage.Postpone`) sin contar un intento ni anotar un error, así que la pantalla no dice que falló. Después el PAC lo arma desde el XML. Sin PDF, el intento falla como temporal y la cola lo reintenta (6 intentos).
   - Sube el PDF a Meta (`POST /{phone-number-id}/media`) y manda el documento con su media id, con el nombre `{Serie}-{Folio}_{UUID}.pdf`. Una subida que Meta acepta sin devolver el media id se reintenta, como una respuesta 5xx.
   - Lo que Meta rechaza para siempre termina el mensaje como «No se envió» con su motivo; lo temporal se reintenta. Entregado, leído y lo que Meta no entregó después (por ejemplo 131047, fuera de las 24 horas) llegan por el webhook de estados, como los demás mensajes.
9. **«Enviar por WhatsApp»** (quien gestiona los pedidos, `ManageOrders`, y ve el pedido): un CFDI timbrado de los de la decisión 1, a un número (se propone el de la factura, el del último mensaje o el que propone el pedido), con «Usar este WhatsApp para lo que siga de esta factura» (cambia `ReceiverWhatsApp`; viene marcada si la factura no tiene número).
   - Límites, antes de encolar y bajo el candado de facturación del negocio (`IInvoicingLock`): 5 por CFDI y 300 por negocio en 24 horas (los automáticos cuentan). Cada mensaje cuesta y sale del número del negocio: un error de dedo no debe mandar la factura sin fin a un desconocido.
   - El CFDI de otro negocio o de un pedido que la persona no ve responde 404.
10. **La API se mantiene compatible:**
    - Rutas nuevas: `POST /api/v1/invoicing/invoices/{id}/whatsapp`, `/payment-complements/{id}/whatsapp` y `/credit-notes/{id}/whatsapp` con `{ phone, useForInvoice }`; `PUT /api/v1/invoicing/settings/whatsapp-template` con `{ templateId }` (dueños y administradores, en la auditoría); las dos públicas de la decisión 7.
    - Campos nuevos: `receiverWhatsApp` (opcional) al facturar y al sustituir; `OrderInvoiceDto.receiverWhatsApp`; `whatsApps` (los 3 más recientes) en cada factura, complemento y nota; `OrderInvoicingDto.whatsAppConnected` y `proposedWhatsApp`; en la configuración de facturación, `whatsAppConnected`, `whatsAppTemplateId`, `whatsAppTemplates` y `whatsAppDefaultText`; `MessagePurpose.CfdiDocument`.
11. **Los mensajes de facturas se ven solo en el pedido:** llevan el enlace que descarga el CFDI, así que Campañas › Mensajes no los lista (`GetOutgoingMessagesQuery` los excluye) e Inicio no los cuenta entre los mensajes que no se enviaron; quien ve el pedido ve cómo salió cada uno.
12. **Se ve en Pedidos › Factura:** cada factura, complemento y nota dice cómo salió su último WhatsApp («Se envió», «Se entregó», «Se leyó», «Enviándose…» con «No ha salido todavía; TienditApp lo vuelve a intentar» si ya falló alguna vez, «No se envió a …. {motivo}») y, con la conexión, ofrece «Enviar por WhatsApp» mientras está timbrada. La facturación del pedido se vuelve a leer cada 30 segundos mientras el último mensaje de algún CFDI está en la cola. Facturar tiene el campo «WhatsApp del cliente (opcional)». Configuración › Facturación tiene la tarjeta «Factura por WhatsApp»: el mensaje, el texto que se manda y el aviso de las 24 horas (un consejo que se queda, no una alerta que el lector de pantalla anuncia en cada visita), o el enlace para conectar WhatsApp.
13. **Fuera de esta fase:**
    - avisar la cancelación por WhatsApp o por correo;
    - el XML como documento (WhatsApp no lo acepta);
    - revocar un enlace o emitir uno nuevo sin mandar otro mensaje;
    - el WhatsApp para facturas en la ficha del cliente;
    - mandar por WhatsApp desde la autofactura.
14. **La migración** `20261030181829_AddCfdiWhatsApp`:
    - en `OutgoingMessages`, las columnas nulas `CfdiDocument`, `CfdiId`, `OrderInvoiceId` (llave a la factura), `RequestedByUserId` y `LinkToken`, sus restricciones y sus índices filtrados (`TenantId, CfdiId, CreatedAt`; `OrderInvoiceId`; `LinkToken` único);
    - las columnas nulas `OrderInvoices.ReceiverWhatsApp` e `InvoicingProfiles.WhatsAppTemplateId`.

## Consecuencias

- **Lo que logra:**
  - El cliente recibe el PDF de su factura, de sus complementos y de sus notas por WhatsApp, y el XML con el enlace, sin que el negocio los descargue y los mande.
  - El negocio ve en Pedidos si salió, si llegó y si se leyó.
- **Lo que limita:**
  - Sin una plantilla aprobada en Meta con encabezado de documento, el mensaje solo llega si el cliente escribió al negocio en las últimas 24 horas; fuera de ellas Meta lo rechaza y queda «No se envió».
  - El enlace es un portador: quien lo tenga ve y descarga el CFDI durante 90 días. Es el mismo riesgo que el PDF reenviado, y el XML no tiene más datos que el PDF.
  - La entrega es **al menos una vez**: si Meta tomó el mensaje y la base no pudo guardar el resultado, la cola lo manda otra vez.
  - Cada mensaje lo cobra Meta al negocio según su tarifa de mensajes de utilidad.
- **Compatibilidad durante el despliegue:**
  - La versión anterior (28L) funciona con la base migrada: no conoce las columnas nuevas y no las escribe; sus mensajes dejan las columnas del CFDI vacías, como la restricción pide para los demás propósitos.
  - El slot de staging corre sin workers (`BackgroundJobs__Enabled=false`): los mensajes de facturas solo los encola y los manda esta versión en producción.
  - Entre el despliegue a staging y el swap, producción sigue en la 28L con su worker sobre la misma base: un mensaje de factura que staging encole lo tomaría ese worker, que no conoce el propósito 5. En ese lapso no se timbra en staging una factura con WhatsApp; si pasara, se cancela con la misma sentencia de abajo antes de que el worker lo tome.
  - Si se vuelve a la 28L (swap de regreso) con mensajes de facturas pendientes, su worker no conoce el propósito 5: los mandaría como texto, sin el PDF y con el enlace sin la dirección de la app (o Meta rechazaría la plantilla sin su documento). Antes de volver se cancelan: `UPDATE [OutgoingMessages] SET [Status] = 4, [LockedUntil] = NULL WHERE [Purpose] = 5 AND [Status] = 0;`. La factura se puede mandar otra vez al volver a esta versión.
- **Reversibilidad:**
  - `rollback.sql` quita las columnas, las restricciones y los índices solo si no hay ningún mensaje de factura, ningún WhatsApp guardado en una factura ni plantilla elegida para facturas.
  - En cuanto existe uno, se detiene con el error 50046 sin cambiar nada. Solo queda corregir hacia adelante.
