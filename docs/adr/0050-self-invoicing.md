# ADR 0050: El cliente factura su ticket en línea (autofactura)

- Estado: Aceptada
- Fecha: 2026-10-26

## Contexto

Desde la Fase 28A (ADR 0045) el negocio timbra la factura de un pedido en Pedidos, y desde la 28E (ADR 0049) el XML y el PDF le llegan al cliente por correo. Pero quien quiere factura tiene que pedírsela al negocio, que escribe sus datos fiscales y la timbra. En México los clientes están acostumbrados a la «autofactura»: el ticket trae un folio y un código, y el cliente factura en la página del negocio. Así lo hacen las cadenas grandes y los PAC lo ofrecen. Sin ella, cada factura es trabajo del mostrador.

Tomé las decisiones de la Fase 28F con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **El negocio la activa** en Configuración › Facturación («Autofactura activa»). Solo el dueño o un administrador (`RequiresTenantManager`), y solo mientras el negocio puede facturar (sus datos fiscales y su CSD; la misma revisión que «Listo para facturar»). Apagarla siempre se puede. La auditoría registra `SelfInvoicingTurnedOn` u `Off` solo cuando el estado cambia.
2. **El código del ticket** (`SelfInvoiceCode`): HMAC-SHA256 del id del pedido con una llave aleatoria de 32 bytes por negocio (`InvoicingProfile.SelfInvoiceKey`). Se toman los primeros 50 bits y se escriben en base32 de Crockford: 10 caracteres sin I, L, O ni U, impresos como `ABCDE-12345`. Al leerlo se quitan espacios y guiones, se pasa a mayúsculas, y O, I y L se leen como 0, 1 y 1.
   - No se guarda: se calcula cada vez. La comparación es de tiempo constante, y un folio que no existe hace el mismo cálculo que un código equivocado.
   - La llave se crea la primera vez que se activa y **se conserva al apagarla**: los tickets ya impresos vuelven a servir si el negocio la activa otra vez. No hay forma de cambiarla desde la app (cambiarla invalidaría todos los tickets impresos).
   - La llave se guarda en la base **sin cifrar**: protege tickets de un negocio, no dinero ni datos personales, y la base ya guarda cosas de igual peso (los tokens de Mercado Pago van cifrados por ser credenciales de cobro; esto no lo es). Quien lee la base puede calcular códigos, pero también puede leer las facturas. No sale nunca de la API.
   - 50 bits contra el límite por IP (decisión 6) hacen inviable adivinar un código: haría falta del orden de 10¹⁵ intentos por ticket.
3. **El ticket impreso** de una venta pagada por completo, de un negocio con autofactura activa, lleva «Factura en línea»: la dirección de la página, el folio, el código, «Hasta el último día del mes de tu compra» y un QR a `/m/{slug}/facturar#{folio}.{código}`. El folio y el código van en el **fragmento**, que el navegador nunca manda al servidor ni queda en los logs ni en la telemetría.
   - `GET /api/v1/invoicing/orders/{orderId}/self-invoice` (`ManageOrders`, quien ve el pedido) los da; responde 204 (y el ticket no lleva nada) con la autofactura apagada, un pedido cancelado, sin importe o de una app de entrega.
   - Un ticket provisional (venta sin conexión) o con saldo no lo lleva. El ticket de texto (WhatsApp) tampoco, por ahora.
4. **La página pública** `/m/{slug}/facturar`, sin cuenta: el cliente escribe el folio y el código (o los trae el QR), ve el ticket (folio, fecha, total) y, si se puede facturar, escribe sus datos fiscales (RFC, nombre, código postal, régimen y uso) y un correo. Las rutas son anónimas (`[AllowAnonymousRequest]`); el negocio sale del slug (`PublicMenuTenant`, el mismo del menú: uno suspendido o inexistente no se encuentra) y todo se lee con el filtro de ese negocio.
   - `GET /api/v1/public/self-invoice/{slug}`: el nombre del negocio, si factura en línea ahora y si está en el entorno de pruebas del PAC.
   - `POST …/ticket`, `POST …/invoice` y `POST …/resend`: el folio, el código y lo demás van en el **cuerpo**, nunca en la URL.
   - Un folio que no existe y un código equivocado responden **lo mismo** (404, «No encontramos ese ticket: revisa el folio y el código impresos en él.»), también en el negocio equivocado: el folio y el código de un negocio no sirven en la dirección de otro.
   - El folio se acepta como el ticket lo imprime o abreviado: «ORD-000123», «ord 000123», «000123» y «123» son el mismo.
5. **Qué se factura en línea:** lo mismo que en Pedidos (el borrador del CFDI listo para timbrar, sin factura viva, fuera de una factura global, con el módulo del negocio), y además:
   - pagado por completo, en una exhibición (PUE). Lo que tiene saldo o se fió lo factura el negocio;
   - que no sea de Rappi, Uber Eats o DiDi Food: esos los factura la plataforma o el negocio;
   - **hasta el último día del mes en que se terminó de pagar** (su último cobro), en el calendario del negocio. Después la página dice hasta cuándo se podía y que la pida al negocio, que puede facturarla desde Pedidos.

   La página dice por qué no se puede, con palabras para el cliente («Esta compra se canceló: no se factura.», «Esta compra ya está en la factura global del negocio…»). Lo que se revisa al buscar se revisa otra vez al facturar, bajo el candado de facturación del negocio (`IInvoicingLock`).
6. **Límite por IP:** buscar, facturar y reenviar comparten la política `self-invoice`, 30 solicitudes por IP cada 10 minutos (300 en Development). La página del negocio usa la de las páginas públicas.
7. **La factura** es la misma que hace Pedidos. El núcleo de `RequestOrderInvoiceCommand` pasó a `OrderInvoiceRequester`, que usan los dos, con las mismas revisiones bajo el candado, el folio, la auditoría `OrderInvoiceRequested` y el mismo mensaje si otra solicitud gana la carrera.
   - Quién la pidió: `RequestedByUserId` es un valor fijo que ninguna cuenta tiene (`OrderInvoice.SelfInvoiceRequester`, `00000000-0000-0000-0000-00000000028f`). La columna sigue sin nulos y la 28E la sigue leyendo. La auditoría dice «Autofactura».
   - Se registra en una transacción (`RequestSelfInvoiceCommand`) y se timbra después del commit, en la misma solicitud (`OrderInvoiceStamper`). Si el PAC no contesta, el worker sigue como con las de Pedidos.
   - Los datos fiscales escritos **no se guardan en el cliente** del negocio: el cliente de la página no tiene cuenta y no se sabe quién es.
   - El correo escrito es el `ReceiverEmail` de la factura: el XML y el PDF salen por el outbox al timbrarse (28E), y también los complementos o notas que siguieran.
8. **Respuestas de facturar:** timbrada (número y a qué correo llega), timbrándose (llega al correo cuando quede) o rechazada por el PAC (su motivo; el cliente corrige y lo intenta otra vez, como en Pedidos).
9. **Un ticket ya facturado** dice «Este ticket ya tiene la factura F-12». Para reenviarla, el cliente escribe **el RFC de esa factura** y un correo: un `CfdiEmail` pedido por `SelfInvoiceRequester`, con los límites de la 28E (5 por CFDI y 300 por negocio en 24 horas, bajo el candado). Sin el RFC correcto no se manda nada ni se dice a quién se facturó. El reenvío no cambia el correo de la factura.
10. **Se ve en Pedidos:** la factura pedida en línea dice «La pidió el cliente en línea con su ticket.» y el correo pedido desde la página, «Lo pidió el cliente en línea.» (`OrderInvoiceDto.requestedByCustomer`, `CfdiEmailDto.byCustomer`).
11. **La API se mantiene compatible:** rutas nuevas (las públicas, `PUT /api/v1/invoicing/settings/self-invoicing` con `{ on }` y la del ticket) y campos nuevos (`InvoicingSettingsDto.selfInvoicing` y `selfInvoicePath`, los de la decisión 10, y los enums `SelfInvoiceTicketStatus` y `SelfInvoiceOutcome`). Nada cambia de forma.
12. **Fuera de esta fase:**
    - el enlace de autofactura en el ticket por WhatsApp;
    - un plazo distinto del fin de mes, o facturar lo pagado en parcialidades;
    - cambiar la llave (invalidar los tickets impresos);
    - la constancia de situación fiscal como archivo para leer los datos;
    - un captcha (el límite por IP y el código de 50 bits bastan por ahora).
13. **La migración** `20261026082041_AddSelfInvoicing`:
    - `InvoicingProfiles.SelfInvoicing` (`bit NOT NULL DEFAULT 0`) y `SelfInvoiceKey` (`varbinary(32) NULL`);
    - `CK_InvoicingProfiles_SelfInvoicing`: activa solo con llave, y la llave siempre de 32 bytes.

## Consecuencias

- **Lo que logra:**
  - El cliente factura su compra sin que el mostrador escriba nada, y le llega por correo.
  - El negocio decide si la ofrece y ve en Pedidos cuáles pidió el cliente.
  - Un ticket impreso no deja ver nada de otro: el código es del pedido y del negocio, y sin él la página no dice si un folio existe.
- **Lo que limita:**
  - Quien tiene el ticket (o una foto de él) puede facturarlo a cualquier RFC mientras nadie lo haya facturado. Es lo que pasa con la autofactura en todas partes; el negocio puede cancelar la factura desde Pedidos y hacer la correcta.
  - El código se calcula con la llave en la base: si la base se filtra, se pueden calcular códigos. No se puede cambiar la llave sin invalidar los tickets impresos.
  - El límite es por IP: muchos clientes detrás de la misma IP (una red de oficina) comparten las 30 solicitudes.
  - Lo pagado el último día del mes se puede facturar en línea solo ese día; después lo factura el negocio.
- **Compatibilidad durante el despliegue:**
  - La versión anterior (28E) funciona con la base migrada: no conoce las columnas, y al crear un perfil de facturación toma los valores por omisión (apagada y sin llave), que la restricción acepta.
  - Si se vuelve a ella, los clientes dejan de poder facturar en línea y las facturas que pidieron se ven como cualquier otra.
- **Reversibilidad:**
  - `rollback.sql` quita las columnas y la restricción solo si ningún negocio tiene llave.
  - En cuanto uno la tiene, se detiene con el error 50042 sin cambiar nada: quitarla cambiaría los códigos de los tickets impresos. Solo queda corregir hacia adelante.
