# ADR 0059: El aviso por correo de que el SAT canceló un CFDI

- Estado: Aceptada
- Fecha: 2026-10-30

## Contexto

Desde la Fase 28E ([ADR 0049](0049-cfdi-email.md)) el XML y el PDF de cada CFDI de la factura de un pedido le llegan al cliente por correo, y desde la 28M ([ADR 0058](0058-cfdi-whatsapp.md)) también por WhatsApp. Cuando el negocio cancela uno, el cliente se queda con un comprobante que ya no vale y nadie se lo dice: el SAT lo avisa en su buzón, que casi nadie revisa, y solo cuando la cancelación necesita su aceptación. Un cliente que dedujo una factura cancelada lo descubre con su contador. Los dos ADR anteriores y la deuda técnica lo tenían anotado («avisar la cancelación por correo»).

Una cancelación pasa por dos momentos: el negocio la **pide** (el CFDI queda «Cancelándose») y el SAT la **confirma**, al instante o cuando el cliente la acepta o deja pasar el plazo (lo revisa el worker de facturación). También puede rechazarla, y el CFDI sigue vigente. Hay seis lugares que confirman: cancelar la factura, un complemento, una nota, la global, la sustitución con motivo 01 y el worker.

Tomé las decisiones de la Fase 28N con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Cuándo se avisa:** cuando el SAT confirma la cancelación de un CFDI de la factura de un pedido (la factura, un complemento de pago o una nota de crédito) y su factura tiene correo. No al pedirla (el SAT o el cliente pueden rechazarla), no si se rechaza, y nunca de la global ni de las notas de sus ventas (el público en general no tiene a quién).
2. **Un solo lugar confirma:** `CfdiCancellations.ConfirmAsync` cambia el CFDI a «Cancelado» solo si esperaba la cancelación y, en ese caso, agrega el aviso en el **mismo** `SaveChanges`, como el correo del timbre. Lo llaman las seis rutas, incluido el worker y la global (para la global no agrega nada). Si la petición y el worker lo confirman a la vez, la versión del CFDI deja guardar solo a uno: un aviso por cancelación. Agregarlo nunca detiene la cancelación (una dirección que ya no pasa la regla lo omite).
3. **El aviso es un correo del CFDI más:** `CfdiEmail` con `Kind = Cancellation` (`CfdiEmailKind`: `Documents` = 0, `Cancellation` = 1), el mismo evento del outbox, el mismo historial que no se borra y los mismos resultados. Lo pide siempre TienditApp (sin persona) y no lleva archivos, así que nunca se envía «sin el PDF». La base lo exige (`CK_CfdiEmails_Kind`).
4. **A dónde:** al correo que la factura tenga cuando el SAT la cancela, también el de sus complementos y notas. En una sustitución, el aviso de la anterior va al correo de la anterior, a donde llegó lo que se cancela; la nueva sale con sus archivos al correo que se escribió al sustituir.
5. **Qué dice:** «Cancelación de la factura F-1 de {Negocio}» (o del complemento de pago, o de la nota de crédito), del mismo remitente «{Negocio} vía TienditApp» y con las respuestas al correo del negocio. El cuerpo dice qué canceló el negocio ante el SAT (folio, monto y fecha) y que ya no tiene validez fiscal; el motivo en palabras («tenía errores», «tenía errores y se emitió otra en su lugar», «la operación no se llevó a cabo»); con el motivo 01, la factura que la sustituye y su folio fiscal, solo si es para el mismo RFC (una sustitución que corrigió a quién iba la factura es el CFDI de otro, y el aviso va a donde llegó la anterior); el emisor, el receptor, el folio fiscal, la fecha de cancelación en la zona horaria del negocio (cuando TienditApp supo que el SAT la canceló: al pedirla o en la revisión del worker) y el enlace para verificarlo en el portal del SAT. El texto del negocio va codificado.
6. **El envío** (`CfdiEmailRequestedHandler`) se ramifica por el tipo: el aviso solo sale si el CFDI está cancelado (siempre lo está: un cancelado no vuelve atrás); termina como enviado, rechazado, sin proveedor o no entregado después del último intento, igual que un correo de archivos.
7. **Se ve en Pedidos › Factura:** el último correo de cada CFDI es el aviso: «Se avisó la cancelación por correo a … el …», «Avisando la cancelación por correo a …» o «No se avisó la cancelación a …: {motivo}. Avísale al cliente por otro medio.», en una sola región que el lector de pantalla anuncia cuando cambia. Si entre los últimos correos los archivos llegaron a otra dirección, agrega «Los archivos también llegaron a …: avísale por otro medio.». La línea del WhatsApp de un CFDI cancelado dice «Por WhatsApp no se avisa la cancelación». La facturación se vuelve a leer mientras el aviso no termina. `CfdiEmailDto` lleva `kind`.
8. **Fuera de esta fase:**
   - avisar la cancelación por WhatsApp, o a las otras direcciones a las que se mandaron los archivos;
   - avisar al pedirla, o que el cliente debe aceptarla en el buzón del SAT;
   - mandar el aviso otra vez a mano o a otra dirección;
   - el acuse de cancelación adjunto.
9. **La migración** `20261030215308_AddCfdiCancellationNotices` agrega a `CfdiEmails` la columna `Kind` (`int`, no nula, con valor por omisión 0) y la restricción `CK_CfdiEmails_Kind`.

## Consecuencias

- **Lo que logra:**
  - El cliente se entera por correo, en cuanto el SAT la confirma, de que su comprobante ya no vale, por qué y, si hubo sustitución, cuál usar.
  - El negocio ve en Pedidos si el aviso salió.
- **Lo que limita:**
  - Sin correo en la factura no hay aviso; tampoco por WhatsApp.
  - La entrega es **al menos una vez**, como los demás correos: si el servidor lo tomó y la base no pudo guardar el resultado, el outbox lo manda otra vez.
  - Los avisos cuentan en el límite de 300 correos de facturas por negocio en 24 horas, como los del timbre; el de 5 por CFDI no aplica (uno cancelado ya no se manda).
  - Si un CFDI se timbra y se cancela antes de que salga el correo de sus archivos, ese correo termina como «ya no está vigente» y al cliente solo le llega el aviso.
- **Compatibilidad durante el despliegue:**
  - La versión anterior (28M) funciona con la base migrada: no conoce la columna y sus correos toman el valor por omisión (0, con archivos).
  - Si la versión anterior tomara un aviso del outbox, lo terminaría como «ya no está vigente» sin mandar nada: su envío exige un CFDI timbrado.
  - Si se vuelve a la 28M sin bajar la base, Pedidos mostraría un aviso enviado como «Se envió por correo»; no se manda nada de más.
- **Reversibilidad:**
  - `rollback.sql` quita la columna y la restricción solo si no hay ningún aviso. Se corre después de regresar el código a la 28M: la guarda no bloquea la tabla, y esta versión podría guardar un aviso entre la guarda y el borrado.
  - En cuanto existe uno, se detiene con el error 50047 sin cambiar nada: bajar la columna convertiría los avisos en correos de archivos. Solo queda corregir hacia adelante.
