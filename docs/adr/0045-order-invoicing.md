# ADR 0045: Timbrar la factura (CFDI 4.0) de un pedido con un PAC

- Estado: Aceptada
- Fecha: 2026-10-23

## Contexto

Desde la Fase 3 TienditApp calcula la vista previa del CFDI 4.0 de un pedido (`GET /api/v1/orders/{id}/cfdi-draft`): claves SAT por línea, IVA por tasa, forma y método de pago y lo que impide timbrar. No timbraba: el negocio copiaba los datos en el portal de su PAC o del SAT.

Para timbrar hacen falta:

- **Un PAC** (proveedor autorizado de certificación) que selle el CFDI con el certificado de sello digital (CSD) del negocio, lo valide y le ponga el timbre fiscal (el UUID).
- **El CSD del negocio** (no su e.firma): `.cer`, `.key` y la contraseña de la llave, que el negocio tramita en el SAT.
- **Los datos del receptor** como aparecen en su constancia: el SAT revisa RFC, nombre, código postal y régimen contra su lista (CFDI40143 a 40148).
- **Cancelar** con motivo; pasando de $1,000 o de 24 horas, el SAT le pide al receptor que la acepte (3 días hábiles; el silencio acepta).

Comparé tres PAC por lo que la regla «el backend decide totales e impuestos» y la de no exponer secretos piden:

- **FiscalAPI** calcula los impuestos por su cuenta a partir de los conceptos: choca con que TienditApp decide los importes.
- **Facturama** (multiemisor) devuelve la llave privada y la contraseña de cada emisor por su API, y no tiene llave de idempotencia.
- **SW Sapien** recibe el CFDI con todos nuestros importes y solo los valida (CFDI40119 con lo esperado y lo reportado), guarda el CSD sin devolverlo, tiene llave de idempotencia (`customid`, 72 h: CFDI3307/307 devuelven el timbre original), cancela por UUID con el CSD guardado, consulta el estatus del SAT y genera el PDF.

Tomé las decisiones de la Fase 28A con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Alcance.** La factura (CFDI de ingreso) de **un** pedido a **un** cliente con RFC, timbrada desde el detalle del pedido, con la vista previa como fuente de los conceptos y los importes.
2. **Qué se factura.** Solo si:
   - la vista previa no tiene bloqueos y el receptor no es público en general (XAXX010101000 va en la factura global);
   - el pedido está pagado por completo (PUE), sin reembolsos, condonaciones ni un reembolso en línea en curso (lo devuelto pide una nota de crédito);
   - el pedido no tiene otra factura viva (timbrando, timbrada o cancelándose);
   - el negocio tiene su CSD vigente y del RFC de Mi negocio, y la plataforma tiene su PAC.

   El backend decide los importes: los de la vista previa, y el total del CFDI es el de la venta.
3. **El PAC es SW Sapien**, detrás de `IInvoicingGateway` (Application), con la sección `SwSapien` (`User` y `Password` con un token de 2 horas en memoria, o `Token` sin vigencia; `Sandbox`; URLs opcionales). Sin credenciales, Pedidos solo muestra la vista previa y Configuración › Facturación dice que no está disponible.
4. **El CSD** lo sube el dueño o un administrador en Configuración › Facturación:
   - TienditApp lo revisa antes de mandarlo: X.509 de sello (no e.firma), vigente, con el RFC de Mi negocio, y la llave con esa contraseña corresponde al certificado (`SealingCertificateReader`);
   - el PAC lo guarda; TienditApp solo guarda su número, RFC, vigencia, quién y cuándo (`InvoicingProfile`). Los archivos y la contraseña nunca se guardan ni se registran (tampoco en la auditoría);
   - reemplazarlo pide al PAC que olvide el anterior después de guardar el nuevo; quitarlo, que lo olvide ya. Avisa 30 días antes de vencer.
5. **La factura** (`OrderInvoice`, `ITenantScoped`) se fija antes de llamar al PAC, en una transacción (`RequestOrderInvoiceCommand`, `ITransactionalRequest`):
   - serie (la del negocio, «F» por omisión) y folio del contador por negocio y serie (`FolioSequences`, `CFDI-{serie}`; sin huecos: un rechazo de la base lo regresa);
   - fecha local del negocio, llave de idempotencia y el CFDI completo en JSON (`Document`): cada intento manda exactamente ese;
   - receptor copiado, forma y método de pago, subtotal, IVA y total;
   - con «Guardar estos datos en el cliente», los datos fiscales pasan al cliente del teléfono del pedido.

   Un índice único filtrado garantiza una factura viva por pedido; dos personas que facturan el mismo pedido a la vez: una gana y la otra recibe el aviso, con su folio regresado.
6. **Timbrar** (`OrderInvoiceStamper`) va después del commit, nunca dentro de la transacción:
   - respuesta → el XML timbrado a los archivos privados (`invoices/{negocio}/{aaaamm}/{factura}.xml`), «Timbrada» con su UUID, y el PDF si el PAC lo da (si no, se pide al descargarlo);
   - rechazo definitivo → «Rechazada» con el mensaje del PAC: el folio se usó y el pedido se puede facturar otra vez con el siguiente;
   - sin respuesta → sigue «Timbrando». La persona o el worker vuelven a pedirla con la misma llave y el mismo CFDI: el PAC devuelve el timbre que ya hizo, nunca uno segundo. Dos intentos a la vez guardan el mismo XML una vez y la versión de la fila deja ganar al primero.
7. **El worker de facturación** (`InvoicingWorkerService`, en el alcance de cada negocio):
   - cada 2 minutos vuelve a pedir las facturas que siguen «Timbrando» con más de un minuto y menos de 71 horas (la ventana de la llave del PAC y de la fecha del CFDI);
   - cada 30 minutos pregunta al SAT por las cancelaciones que esperan al receptor: cancelada (aceptó o pasó el plazo) o, si la rechazó, «Timbrada» otra vez con el aviso.
8. **Cancelar**: el dueño o un administrador, por errores sin relación (02) o porque la venta no se llevó a cabo (03). Primero se le pide al PAC; si se niega, nada cambia. Si el SAT pide la aceptación del receptor, queda «Cancelándose»; si ya estaba cancelada, «Cancelada». Una cancelada deja facturar el pedido otra vez (factura nueva, folio nuevo). Nunca se borra una factura.
9. **Acceso:** timbrar, volver a pedir, ver y descargar, quien gestiona los pedidos (`ManageOrders`, como la vista previa); cancelar, el CSD y la serie, dueños y administradores (`RequiresTenantManager`); los datos fiscales del cliente, `ManageCustomers`. La factura, sus archivos y el CSD de otro negocio responden 404 (suite entre negocios).
10. **Se ve:**
    - **Pedidos › Factura:** las facturas del pedido con su estado, folio fiscal, «Descargar XML/PDF», «Volver a pedir el timbre» y «Cancelar factura»; la vista previa con los datos fiscales propuestos (los del cliente o los de la última factura) y «Timbrar factura» cuando la vista previa revisada es la de esos datos.
    - **Configuración › Facturación:** lo que falta en Mi negocio, el CSD (subir, reemplazar, quitar, vigencia y aviso de vencimiento), la serie, el modo de pruebas y «Cómo funciona».
    - **Clientes:** «Datos fiscales» por cliente y su RFC en la lista.
11. **La API se mantiene compatible:** rutas nuevas bajo `/api/v1/invoicing` y `PUT /api/v1/customers/{id}/fiscal-data`; `CustomerDto` suma `fiscalData`; llegan los enums `OrderInvoiceStatus` y `CfdiCancellationMotive`. Nada se quita ni cambia de nombre.
12. **Fuera de esta fase:** factura global de público en general (28B), PPD y complementos de pago (28C), notas de crédito (28D), sustitución con motivo 01, autofactura del cliente, mandar la factura por correo o WhatsApp, logo en el PDF, otras monedas, retenciones y varios CSD o series por sucursal.
13. **La migración** `20261023064133_AddOrderInvoicing`:
    - `OrderInvoices` con sus restricciones (estado, folio, importes que cuadran, PUE, timbre, rechazo, cancelación y motivo 02/03), el receptor en columnas `Receiver_*`, los índices únicos (una viva por pedido, serie y folio por negocio, la llave, el UUID por negocio) y el de lo pendiente para el worker;
    - `InvoicingProfiles` (uno por negocio) con `CK_InvoicingProfiles_Certificate` (el CSD completo o nada);
    - `Customers`: `FiscalData_*` (con nulos) y `CK_Customers_FiscalData` (completos o nada, nunca en público en general).

## Consecuencias

- **Lo que logra:**
  - El negocio timbra, descarga y cancela la factura de una venta sin salir de TienditApp, con los mismos importes que cobró.
  - Una respuesta perdida del PAC nunca timbra dos veces ni deja la factura sin resolver mientras el PAC guarda la llave.
  - La llave privada y su contraseña no viven en TienditApp.
- **Lo que limita:**
  - Solo ventas pagadas por completo y a un cliente con RFC: las de público en general esperan la factura global.
  - El CFDI lleva la hora local de la zona del negocio: si su zona no es la de su código postal de expedición, el PAC puede rechazar la fecha (se ve el mensaje y se corrige la zona).
  - Si el PAC no responde durante más de 71 horas, el worker deja de pedirla; volver a pedirla a mano después puede recibir un rechazo por la fecha aunque el PAC la hubiera timbrado (se revisa en el portal del PAC).
  - El PDF es el del PAC, sin logo del negocio.
  - Una cancelación pedida al PAC cuya respuesta no se guarda (caída entre la respuesta y el guardado) deja la factura «Timbrada»; pedirla otra vez la resuelve.
- **Compatibilidad durante el despliegue:**
  - La versión anterior (24K) funciona con la base migrada: no conoce las tablas ni las columnas nuevas y no las escribe.
  - Si se vuelve a ella, las facturas y el CSD se quedan en la base y en el PAC, y Pedidos vuelve a mostrar solo la vista previa.
- **Reversibilidad:**
  - `rollback.sql` quita las tablas, las columnas y las restricciones nuevas solo si no hay ninguna factura (aunque sea rechazada), ningún CSD ni datos fiscales de clientes.
  - En cuanto alguno existe, se detiene con el error 50037 sin cambiar nada: quitarlos borraría facturas y datos que el negocio ya usa. Solo queda corregir hacia adelante.
