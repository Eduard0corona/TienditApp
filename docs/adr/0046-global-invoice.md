# ADR 0046: La factura global a público en general

- Estado: Aceptada
- Fecha: 2026-10-23

## Contexto

Desde la Fase 28A (ADR 0045) el negocio timbra la factura de un pedido a un cliente con RFC. Las ventas al público en general, la mayoría en un negocio local, se quedaban sin CFDI: el SAT pide que se amparen en una **factura global** por periodo (RMF 2.7.1.21), y el negocio la hacía fuera de TienditApp.

Lo que piden las fuentes primarias (Guía de llenado del CFDI global rev. 1, Anexo 20 rev. 3, matriz de errores del 20 de julio de 2026, RMF 2026 con sus modificaciones, CFF y FAQ de cancelación 2026):

- **InformacionGlobal** obligatorio con el receptor XAXX010101000 «PUBLICO EN GENERAL» (CFDI40130): periodicidad 01 a 05, meses 01 a 12 (13 a 18 para la bimestral, CFDI40134/40135), y la bimestral solo para el régimen 621 (CFDI40132). El año: el de la expedición o hasta 5 anteriores (CFDI40136).
- **Receptor**: código postal = lugar de expedición (CFDI40149), régimen 616 (CFDI40159), uso S01.
- **Conceptos**: uno por ticket con su folio en NoIdentificacion, 01010101, ACT, «Venta», cantidad 1, valor = subtotal del ticket. Un concepto puede llevar varios traslados (el XSD no los limita); las partidas no objeto de IVA van en otro concepto con el mismo folio. Cada traslado respeta los límites de redondeo (CFDI40180) y los del comprobante son la suma exacta (CFDI40215/40221).
- **Forma de pago**: la del ticket de mayor monto; método PUE.
- **Plazo**: dentro de las 24 horas siguientes al cierre del periodo; la fecha es la de expedición.
- **Parcialidades o pagos diferidos**: en una global aparte, exclusivamente para ellas.
- **Cliente que pide su factura después**: cancelar la global con motivo 04, emitir su factura y volver a emitir la global sin ese ticket (FAQ de cancelación 2026, escenario 4). Sin límite de «mismo mes».
- **Cancelación** sin aceptación del receptor (RMF 2.7.1.35 VII).
- Semanas y quincenas que cruzan meses no tienen regla oficial; la práctica es cortarlas al terminar el mes.

Tomé las decisiones de la Fase 28B con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Alcance.** Un CFDI global de ingreso por periodo **ya terminado** (diario, semanal, quincenal, mensual; bimestral solo con régimen 621) con las ventas pagadas por completo en ese periodo que no tienen factura propia ni están en otra global viva. Receptor XAXX010101000, «PUBLICO EN GENERAL», CP del negocio, 616, S01; InformacionGlobal con periodicidad, mes(es) y año. Misma serie y contador de folios que las facturas de pedido, mismo PAC, CSD y archivos privados.
2. **El periodo** (`GlobalInvoicePeriod`): el que contiene el día elegido, en días del negocio. Las semanas van de lunes a domingo y las quincenas del 1 al 15 y del 16 al fin de mes, cortadas al terminar el mes; los bimestres son pares de meses (enero-febrero = 13). Se emite al día siguiente de su último día; pasadas 24 horas del cierre, avisa que el SAT pide emitirla antes. Un año anterior a los 5 que acepta el SAT lo bloquea.
3. **Qué venta entra** (`GlobalInvoiceDrafts`): su último cobro cae en el periodo (zona horaria del negocio), y además:
   - no está cancelada y su total es mayor a 0;
   - ya no puede cambiar su total: una cuenta abierta (rondas, cancelaciones) o una venta pendiente (partidas, envío) se queda fuera hasta cerrarse, porque lo que cobrara después no entraría en la global y tampoco podría facturarse sola;
   - está pagada por completo, sin reembolsos, condonación ni reembolso en línea en curso (lo devuelto pide una nota de crédito, 28D). La excepción: una venta que ya cubría una global timbrada y cancelada con 04 vuelve a entrar por lo que era, aunque después tuviera una devolución (avisa: la devolución pide su nota de crédito); si no, se quedaría sin CFDI;
   - no se vendió a crédito (las parcialidades van en una global aparte, fuera de esta fase);
   - no tiene factura propia viva ni está en otra global viva;
   - sus importes cuadran (su borrador de CFDI sin diferencia).

   Las que tuvieron cobros en el periodo y no entran se listan con su motivo. Y entran también, con su aviso, las de un periodo anterior que ya tiene su global viva y que no están en ninguna factura viva: una venta que solo pudo entrar después de timbrada la de su periodo (se aceptó o se cerró, se rechazó o canceló su factura, se rechazó su reembolso en línea) no se queda sin CFDI. Las de un periodo sin global esperan la suya.
4. **Conceptos e importes.** Uno por venta (ObjetoImp 02) con un traslado por cada tasa que cobró (16 %, 8 %, 0 % y exento), con la base y el IVA del borrador de esa venta y dentro de los límites del SAT; una tasa con solo partidas gratis no lleva traslado (el SAT no toma una base de 0); lo no objeto, otro concepto (01) con el mismo folio. Los traslados del comprobante son la suma por tasa y el total, la suma de los totales de las ventas: el backend decide los importes. La forma de pago es la de la venta más grande (en un empate, la que no es 99); «Otro» (99) la bloquea. Hasta 2,000 ventas por global (una solicitud al PAC).
5. **El agregado** `GlobalInvoice` (`ITenantScoped`) comparte con `OrderInvoice` la base `CfdiInvoice` (serie, folio, fecha, llave de idempotencia, estado, documento, importes, UUID, archivos, rechazo, cancelación y versión) y suma periodicidad, primer y último día, mes(es), año y sus ventas (`GlobalInvoiceOrder`: pedido, folio, total y `Released`). Al rechazarse o cancelarse, sus ventas quedan libres (se conservan las filas). Un índice único filtrado `(TenantId, OrderId) WHERE Released = 0` garantiza que una venta esté en una global viva a lo más.
6. **El candado de facturación por negocio** (`IInvoicingLock`, `sp_getapplock` en la transacción de la solicitud): la factura de un pedido y la global lo toman primero y revisan dentro de él que la venta no esté en la otra. Así nunca quedan las dos, y dos personas que facturan el mismo pedido a la vez reciben «ya tiene la factura» en lugar de chocar con el índice.
7. **Una venta en una global viva no se factura sola**: Pedidos dice «Esta venta está en la factura global F-…: cancela esa global con el motivo 04, factura esta venta y emite otra vez la global del periodo».
8. **Lo revisado es lo que se timbra**: el cliente manda el número de ventas, el total y la huella de la vista previa que vio (un SHA-256 de las ventas y sus totales: dos vistas con las mismas cifras pero otras ventas no la comparten); si algo cambió, la API responde «Las ventas del periodo cambiaron desde que viste la vista previa» y no timbra.
9. **Timbrar, reintentar y el worker** son los de la 28A (`OrderInvoiceStamper` genérico, misma llave y mismo CFDI en cada intento); el worker de facturación también timbra las globales que siguen «Timbrando» y consulta sus cancelaciones. El JSON de SW Sapien lleva `InformacionGlobal` antes del emisor y los traslados de cada concepto.
10. **Cancelar** una global: dueño o administrador, motivo 02 (errores) o 04 (un cliente pidió la factura de una de sus ventas); no requiere aceptación. Nunca se borra.
11. **Acceso:** todo lo de la global, dueños y administradores (`RequiresTenantManager`): es el lado fiscal del negocio, como Configuración › Facturación. La global, sus ventas y sus archivos de otro negocio responden 404 (suite entre negocios).
12. **Se ve en Reportes › Factura global:** la lista (folio, estado, periodo, ventas, total) con búsqueda por folio, UUID o folio de una venta («¿en qué global quedó esta venta?»); «Nueva factura global» con la periodicidad y un día del periodo (ayer por omisión), la vista previa con las ventas que entran, las que no y por qué, los importes, avisos y bloqueos, y «Timbrar factura global»; y el detalle con descargas, «Volver a pedir el timbre» y «Cancelar factura global» (04 por omisión).
13. **La API se mantiene compatible:** rutas nuevas bajo `/api/v1/invoicing/global-invoices` (la vista previa con su `fingerprint`, que timbrar pide como `expectedFingerprint`) y el enum `GlobalInvoicePeriodicity`. Nada se quita ni cambia de nombre. Lo único que cambia de comportamiento: dos facturas del mismo pedido a la vez ya no regresan «otra persona está facturando este pedido» sino «ya tiene la factura» (el candado las forma).
14. **Fuera de esta fase:** sustitución con motivo 01, global de ventas a crédito (parcialidades), notas de crédito y el egreso relacionado con la global (28D), PPD (28C), global por sucursal, timbrado ZIP para más de 2,000 ventas y el concepto único de RIF/RESICO.
15. **La migración** `20261023175930_AddGlobalInvoices`:
    - `GlobalInvoices` con sus restricciones (estado, folio, importes que cuadran, PUE, periodo con su año y meses, timbre, rechazo, cancelación y motivo 02/04) y sus índices (serie y folio por negocio, la llave, el UUID por negocio, el periodo y lo pendiente para el worker);
    - `GlobalInvoiceOrders` con `CK_GlobalInvoiceOrders_Total` y el índice único filtrado de una global viva por venta.

## Consecuencias

- **Lo que logra:**
  - Las ventas al público en general quedan amparadas sin salir de TienditApp, con los importes que se cobraron y el mismo folio de la serie.
  - Una venta nunca está a la vez en su factura y en una global, ni en dos globales vivas.
  - El cliente que pide su factura después tiene un camino que el SAT acepta, guiado desde Pedidos.
- **Lo que limita:**
  - Las ventas a crédito, con reembolsos o condonación no entran: esperan sus fases (parcialidades y notas de crédito).
  - Una cuenta que sigue abierta al terminar el periodo entra en la global del periodo en que se termine de pagar; si ya estaba pagada y se cierra después de timbrada la global de ese periodo, en la siguiente global que se emita, no en la de su periodo.
  - Más de 2,000 ventas en un periodo piden una periodicidad más corta.
  - La forma de pago es una por global (la de la venta más grande), como el SAT la pide.
  - El candado espera hasta 10 segundos: si otra factura del negocio tarda más, la persona lo intenta otra vez.
- **Compatibilidad durante el despliegue:**
  - La versión anterior (28A) funciona con la base migrada: no conoce las tablas nuevas y no las escribe.
  - Si se vuelve a ella, las globales se quedan en la base y en el PAC; la 28A no revisa si una venta está en una global, así que se puede facturar sola (se cancela una de las dos).
- **Reversibilidad:**
  - `rollback.sql` quita las dos tablas solo si no hay ninguna factura global (aunque sea rechazada).
  - En cuanto existe una, se detiene con el error 50038 sin cambiar nada. Solo queda corregir hacia adelante.
