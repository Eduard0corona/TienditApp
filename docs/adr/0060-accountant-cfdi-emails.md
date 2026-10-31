# ADR 0060: Los CFDI del mes al contador por correo

- Estado: Aceptada
- Fecha: 2026-10-31

## Contexto

Desde la Fase 28H ([ADR 0052](0052-issued-cfdi-report.md)) Reportes › CFDI emitidos descarga los CFDI de un mes para el contador: un ZIP con el XML de cada uno, por tipo, y la lista de todos en CSV. El dueño lo descarga y se lo manda por su cuenta cada mes, y muchos se acuerdan tarde: la declaración mensual vence el día 17. La ADR 0052 y la deuda técnica lo tenían anotado («mandar esa descarga al contador por correo»).

Desde la 28E ([ADR 0049](0049-cfdi-email.md)) TienditApp ya manda correos de un negocio con adjuntos por el outbox, y sabe cómo terminó cada uno (enviado, rechazado, sin proveedor o no entregado).

Tomé las decisiones de la Fase 28O con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **El correo del contador** vive en el perfil de facturación: `AccountantEmail` (una sola dirección simple, la misma regla que el correo de una factura) y `SendsMonthToAccountant` (mandarle cada mes los CFDI, solo). Sin correo no hay envío de cada mes: el dominio lo rechaza y la base lo exige (`CK_InvoicingProfiles_Accountant`). Lo cambian el dueño o un administrador en Configuración › Facturación › «Contador»; la auditoría guarda qué cambió, nunca la dirección.
2. **Cada envío es un registro que no se borra:** `CfdiMonthEmail`, por negocio, con el año y el mes, a quién, quién lo pidió (nadie cuando lo mandó TienditApp al empezar el mes), cuándo, los intentos que el servidor de correo no tomó y cómo terminó: enviado (con cuántos CFDI llevó la lista) o no enviado y por qué (`CfdiMonthEmailFailure`: rechazado, sin proveedor, no entregado, demasiado grande, demasiados). Solo cambia su resultado, una vez. Sale por el outbox con `CfdiMonthEmailRequestedEvent`.
3. **«Mandar al contador»** en Reportes › CFDI emitidos (dueños y administradores): el archivo del mes a la dirección que se escriba (se propone la del contador), que queda como la del contador para la próxima vez (y para el envío de cada mes, si está activo; cuando cambia se audita como en Configuración, sin la dirección). Pide un mes desde enero de 2022 que ya empezó en la zona del negocio y que tenga al menos un CFDI (de cualquier estado: la lista también lleva los rechazados). Hasta 5 al día por negocio, contados bajo el candado de facturación para que dos solicitudes a la vez no pasen las dos el último; el envío de cada mes no cuenta.
4. **Lo que se manda es la misma descarga:** el ZIP de Reportes › CFDI emitidos, armado en un solo lugar (`IssuedCfdiFiles`) para la descarga y el correo, con lo que el mes tiene al salir el correo. Asunto «CFDI de octubre de 2026 de {Negocio}», remitente «{Negocio} vía TienditApp», respuestas al correo del negocio; el cuerpo dice los días del mes (hasta hoy, y que el mes no ha terminado, si se manda antes de que termine) y cuántos de cada tipo van vigentes, cancelados y sin timbre (solo en la lista), con el texto del negocio codificado. El diálogo de «Mandar al contador» también avisa que el mes en curso no ha terminado, y que la dirección nueva recibe también el envío de cada mes cuando está activo.
5. **Lo que no cabe en un correo no sale:** un mes de más de 5,000 CFDI (lo que junta una descarga) termina «demasiados», y un ZIP de más de 7 MB «demasiado grande» (codificado para el correo crece un tercio, y muchos servidores rechazan un mensaje de más de 10 MB). Los dos se descargan y se mandan por otro medio.
6. **El envío** (`CfdiMonthEmailRequestedHandler`) sigue el de los correos de la 28E: un servidor que no lo toma ahora cuenta el intento y el outbox lo repite; uno que rechaza la dirección para siempre, o sin proveedor, lo termina; en el último intento del outbox, cualquier falla lo termina como no entregado, así nunca queda pendiente.
7. **Cada mes, solo:** el worker de facturación, en los primeros días de cada mes y a lo más cada hora (en la siguiente pasada si quedaron más de los que toma una), busca los negocios activos que lo pidieron y no lo han encolado esos días; a cada uno le manda, en su alcance, `QueueMonthlyAccountantEmailCommand`, que encola el mes anterior desde el día 1 a las 8:00 hasta el final del día 7 en la zona del negocio, una vez, y solo si ese mes tiene CFDI. Después del día 7 ya no: un mes viejo nunca llega por sorpresa. Un índice único filtrado (`UX_CfdiMonthEmails_Monthly`: negocio, año y mes, sin quien lo pidió) deja uno solo por mes aunque dos workers lo encolen a la vez. Como el resto del worker, sin el PAC configurado no hace nada.
8. **Se ve en Reportes › CFDI emitidos:** debajo de las descargas, cómo salió el último correo del mes: «Se mandaron a … el …, con 128 CFDI», «Mandando los CFDI a …» o «No se mandaron a …: {por qué}», con «TienditApp los mandó solo, al empezar el mes» cuando fue el de cada mes; en una región que el lector de pantalla anuncia cuando cambia. Mientras uno va en camino, la pantalla lo vuelve a leer; «Mandar al contador» no espera a que termine (un envío que nunca termina no lo bloquea, y el límite de 5 al día cuida el resto).
9. **Rutas:** `PUT /api/v1/invoicing/settings/accountant`, `GET` y `POST /api/v1/invoicing/issued-cfdis/accountant-emails`. `InvoicingSettingsDto` lleva `accountantEmail` y `sendsMonthToAccountant`.
10. **Fuera de esta fase:**
    - los PDF y los acuses de cancelación en el archivo, un enlace en lugar del adjunto, o partir un mes grande en varios correos;
    - varios destinatarios, otro día del mes, o el mes por WhatsApp;
    - los CFDI recibidos (la descarga masiva del SAT).
11. **La migración** `20261031053720_AddAccountantCfdiEmails` agrega a `InvoicingProfiles` las columnas `AccountantEmail` (`nvarchar(254)`, nula) y `SendsMonthToAccountant` (`bit`, no nula, 0 por omisión) con `CK_InvoicingProfiles_Accountant`, y la tabla `CfdiMonthEmails` con sus restricciones (mes desde 2022, un solo resultado con lo que lleva, intentos) e índices.

## Consecuencias

- **Lo que logra:**
  - El contador recibe los CFDI del mes el día 1 sin que el dueño se acuerde, o cuando el dueño lo pide, con el mismo archivo que descargaría.
  - El negocio ve si salió, con cuántos CFDI, o por qué no.
- **Lo que limita:**
  - El archivo es el del momento en que sale el correo: lo que se timbre o se cancele después del mes no lo cambia; se manda otra vez.
  - La entrega es **al menos una vez**, como los demás correos: si el servidor lo tomó y la base no pudo guardar el resultado, el outbox lo manda otra vez.
  - El ZIP se arma en memoria, como la descarga (hasta 5,000 XML); un mes grande ocupa al worker del outbox mientras se arma.
  - Si el servidor del contador acepta menos de 10 MB, un archivo de menos de 7 MB también puede rebotar después de que lo tomó, y TienditApp no se entera. Si el que lo rechaza por tamaño es el servidor de correo de la plataforma, el envío se repite como cualquier falla y termina «no entregado», no «demasiado grande».
  - El outbox manda un mensaje a la vez: el día 1 a las 8:00, los archivos de muchos negocios de la misma zona retrasan los demás correos mientras se arman y se mandan.
  - Si el envío de cada mes no pudo salir del día 1 al 7 (por ejemplo, la facturación estuvo apagada esos días), ese mes ya no se manda solo.
- **Compatibilidad durante el despliegue:**
  - La versión anterior (28N) funciona con la base migrada: no conoce las columnas ni la tabla, y sus perfiles guardan el contador vacío y sin envío de cada mes (los valores por omisión).
  - Si la versión anterior tomara del outbox un envío mientras corren las dos, no conoce el evento y lo aparta sin mandarlo: ese envío se queda «Mandando…» y se manda otra vez a mano (el de cada mes no se vuelve a encolar ese mes).
- **Reversibilidad:**
  - `rollback.sql` quita la tabla, las columnas y la restricción solo si no hay ningún envío. Se corre después de regresar el código a la 28N.
  - En cuanto existe uno, aunque haya fallado, se detiene con el error 50048 sin cambiar nada: se perdería el historial de lo que salió del negocio. Solo queda corregir hacia adelante.
