# ADR 0055: Notas de descuento o bonificación y notas de las condonaciones de una factura global

- Estado: Aceptada
- Fecha: 2026-10-29

## Contexto

Desde la Fase 26B (ADR 0034) el negocio condona lo que un cliente le debe con un ajuste trazable, y desde la 28D (ADR 0048) cada condonación de un pedido con factura propia lleva su nota de crédito con el concepto «Condonación». La 28I (ADR 0054) dio nota de crédito a los reembolsos de las ventas de una factura global, pero dejó fuera dos casos:

- **Un descuento o una bonificación sin reembolso:** el negocio baja el precio después de la venta (un defecto, un retraso, un trato). Hoy se registra como condonación con motivo «Cortesía» u «Otro», y su nota dice «Condonación», aunque el negocio no renunció a una deuda: rebajó el precio.
- **Una venta al público saldada con una condonación:** la global no la tomaba (`WrittenOffReason`). Sin datos fiscales del cliente se quedaba sin ningún CFDI, aunque lo cobrado sí era ingreso.

Lo que piden las fuentes primarias del SAT:

- **Anexo 20, guía de llenado del CFDI 4.0, comprobante de egreso:** ampara devoluciones, descuentos y bonificaciones; se relaciona con el CFDI de ingreso con TipoRelacion 01 («Nota de crédito de los documentos relacionados»). Su forma de pago es la del ingreso que corrige, salvo que ese ingreso no se haya pagado: entonces va con la clave 15 «Condonación».
- **Guía de llenado del CFDI global 4.0** (rev. 1, 2023-03-08), apéndice 2: el egreso de una venta de una global va a público en general y relacionado con ella. Ya lo aplica la 28I.

Tomé las decisiones de la Fase 28J con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Un motivo nuevo para la condonación:** `OrderWriteOffReason.Discount = 4`, «Descuento o bonificación».
   - Es una condonación como las demás: solo baja lo que se debe (`IsOwedByCustomer` y saldo mayor a 0), se deshace igual y cuenta igual en el estado de cuenta.
   - Para rebajar algo que ya se pagó, el negocio registra un reembolso en el pedido (con su nota). El panel «Condonar» lo dice al elegir el motivo.
   - Es de **una** venta: con «Toda la cuenta», la condonación se reparte del pedido más viejo al más nuevo, y la nota de cada parte acreditaría el CFDI de un pedido que no se descontó. La API lo rechaza sin pedido (`DiscountNeedsOrderMessage`), y el panel lo pide antes de dejar condonar.
   - No cambia el esquema: `OrderWriteOffs.Reason` ya es un entero sin restricción de valores.
2. **El concepto de la nota sigue al motivo** (`SatCatalog.WriteOffDescriptionFor`): «Descuento o bonificación» para el motivo nuevo y «Condonación» para los demás. Vale para las notas de la factura de un pedido y para las de una global. La forma de pago sigue siendo 15: lo descontado nunca se cobró.
3. **La global toma las ventas saldadas con una condonación:**
   - Entran si no se vendieron a crédito y lo cobrado más lo condonado cubre su total. Van por su total, como las devueltas en parte (ADR 0054), y cada condonación en pie lleva su nota.
   - La que todavía debe algo sigue fuera, con el motivo «No está pagada por completo: entra en una factura global cuando se termine de pagar o se condone lo que falta».
   - La vendida a crédito sigue fuera (`CreditReason`): el SAT pide su global de parcialidades, que TienditApp todavía no genera.
   - La que entre devoluciones y condonaciones ya no tiene nada que facturar queda fuera con el motivo «Entre devoluciones y condonaciones no queda nada que facturar».
   - La vista previa avisa cuántas ventas con condonaciones o descuentos incluye.
   - Las de un periodo que ya tiene su global timbrada (por ejemplo, condonadas al día siguiente) entran en la siguiente que se emita, como ventas de periodos anteriores, con su aviso.
4. **El agregado `CreditNote`** acepta la nota de una condonación de una venta de la global (`ForWriteOffInGlobalInvoice`). Exige:
   - una global timbrada con su UUID;
   - la venta de esa global, no liberada;
   - una condonación de esa venta y del mismo negocio, que no se haya deshecho;
   - no más que lo que la venta cobró en la global.

   Esos requisitos los cumple el dominio. La base solo exige, con `CK_CreditNotes_Invoice`, una exhibición en la nota de una global (`AfterInstallment` = 0), sin pedir ya un reembolso, y una sola nota viva por condonación (`IX_CreditNotes_OrderWriteOffId_Live`, Fase 28D), también en una global.
5. **El documento** es el de la 28I, con estas diferencias: forma 15, y el concepto que dice el punto 2. Va a XAXX010101000 «PUBLICO EN GENERAL», con régimen 616, uso S01 y el lugar de expedición actual como domicilio fiscal. Lleva TipoRelacion 01 con el UUID de la global, sin `InformacionGlobal`, y el folio de la venta en `NoIdentificacion`.
6. **Cuánto puede acreditar:** lo que la venta cobró en la global menos las notas de sus otros ajustes en esa global, reembolsos y condonaciones juntos (`SaleRoom`).
   - Cuenta la última nota de cada ajuste aunque esté rechazada o cancelada, como en la 28D.
   - La nota de una condonación deshecha ya no cuenta.
7. **Emisión automática:** el worker de facturación también busca las condonaciones en pie de ventas de una global timbrada sin ninguna nota de esa global (`PendingWriteOffs`), con las mismas reglas, límites y turnos. «Emitir ahora» y «Emitir otra vez» usan el mismo comando.
8. **Deshacer y cancelar:**
   - Una condonación con nota viva no se deshace: primero se cancela su nota en Pedidos, como en la 28D.
   - Tampoco la de una venta que cubre una global viva, tenga nota o no: la global la tomó saldada y la ampara como pagada en una exhibición, así que deshecha la venta debería dentro de ese CFDI. El mensaje dice el camino: cancelar con 02 sus notas y la global, deshacer la condonación y emitir otra vez la global, que ya no la incluye mientras deba.
   - La nota de una condonación se cancela con 02 (después se emite otra) o con 03 (la condonación no se llevó a cabo y después se deshace).
   - Una condonación deshecha no lleva nota, y la global no se cancela mientras tenga notas vivas (ADR 0054).
   - Ningún complemento de pago cuenta las notas de una global (se paga en una exhibición), así que no detienen el deshacer.
9. **Nunca va por correo:** es para el público en general, como toda nota de una global.
10. **Dónde se ve:**
    - **Clientes › Estado de cuenta › Condonar:** el motivo «Descuento o bonificación», con su aviso; con «Toda la cuenta» pide elegir el pedido. El historial de condonaciones y Pedidos muestran el motivo.
    - **Pedidos › Factura de la venta:** cada condonación de una venta de la global con su nota «al público en general, de la factura global F-x», con forma 15 (Condonación) y sin «Enviar por correo». El texto de arriba dice que cada reembolso y cada condonación lleva su nota.
    - **Reportes › Factura global › una:** sus notas de crédito, de reembolsos y de condonaciones.
    - **Reportes › CFDI emitidos** (ADR 0052): sin cambios; la nota se relaciona con la global y va al público en general.
11. **Acceso:** el de la 28D. Condonar y deshacer es de dueños y administradores. Ver, emitir y descargar lo hace quien gestiona los pedidos y ve el pedido. Cancelar es de dueños y administradores. Todo lee con el filtro del negocio. El worker lee sin él, cada conjunto unido por su negocio, y la lista de excepciones del filtro (`TenantFilterBypassAllowlistTests`) cuenta sus dos lecturas nuevas.
12. **La API se mantiene compatible:**
    - el enum `OrderWriteOffReason` gana el valor 4;
    - `AboveSaleMessage` nombra el ajuste («El reembolso» o «Lo condonado»);
    - sin campos ni rutas nuevas, y nada se quita ni cambia de nombre;
    - cambia de comportamiento: la global toma las ventas saldadas con una condonación, y sus condonaciones llevan nota; condonar con el motivo 4 exige un pedido; y deshacer la condonación de una venta de una global viva se rechaza.
13. **La migración** `20261029163102_AddGlobalInvoiceWriteOffNotes` solo cambia `CK_CreditNotes_Invoice` (la borra y la vuelve a crear). No cambia datos. Su `Down` se detiene con el error 50045 en cuanto existe la nota de una condonación de una global.
14. **Fuera de esta fase:**
    - el reembolso como saldo a favor, que el SAT pide con el procedimiento de anticipos (forma 30 y relación 07);
    - una nota que acredite varias ventas o varios ajustes a la vez;
    - el descuento sobre lo ya pagado sin devolver el dinero;
    - sustituir la global o sus notas (motivo 01);
    - la global de ventas a crédito.

## Consecuencias

- **Lo que logra:**
  - Una venta al público saldada con una condonación ya tiene su CFDI (la global) y su egreso, sin datos fiscales del cliente.
  - El contador distingue en el CFDI un descuento de una deuda condonada.
- **Lo que limita:**
  - La global ampara la venta por su total y la nota resta lo condonado, en lugar de una global por lo cobrado. Es lo que ya hacía la 28I con las devoluciones, y el SAT recibe los dos CFDI.
  - Al desplegar, la siguiente global de cada negocio puede traer ventas condonadas de periodos anteriores, y el worker emite sus notas con la fecha de ese día. La consulta 4 del `verify.sql` da un tope de cuántas son, y la vista previa las cuenta antes de timbrar. Hasta hoy la vista previa pedía consultar al contador por esas ventas: si alguna ya tiene un CFDI hecho fuera de TienditApp, hay que saberlo antes de emitir la siguiente global, porque la vista previa no deja quitar una venta (el encabezado del `apply.sql` lo advierte).
  - Deshacer la condonación de una venta de una global pide cancelar la global entera, aunque la condonación todavía no tenga nota.
  - El motivo «Descuento o bonificación» solo se aplica a lo que se debe: una venta ya pagada se corrige con un reembolso.
  - El formato de `CfdiRelacionados` en el JSON de SW Sapien sigue sin probarse en su ambiente de pruebas (ADR 0048).
- **Compatibilidad durante el despliegue:**
  - La versión anterior (28I) funciona con la base migrada: nunca crea notas de condonaciones de una global, y la restricción nueva admite todo lo que ella guarda.
  - Una condonación con el motivo 4 se lee en esa versión como un número sin nombre, y el cliente anterior la muestra como «Otro».
  - Volver a esa versión funciona mientras no exista ninguna nota de una condonación de una global. Con una, esa versión la muestra en el pedido pero no la entiende (dice que no hay factura timbrada): conviene corregir hacia adelante. La consulta 3 del `verify.sql` las cuenta.
- **Reversibilidad:** `rollback.sql` deja la restricción como estaba solo si no hay ninguna nota de una condonación de una global. En cuanto existe una (aunque esté rechazada), se detiene con el error 50045 sin cambiar nada.
