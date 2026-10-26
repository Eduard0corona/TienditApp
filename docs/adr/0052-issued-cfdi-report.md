# ADR 0052: Los CFDI emitidos del mes y sus XML para el contador

- Estado: Aceptada
- Fecha: 2026-10-26

## Contexto

Desde la Fase 28A (ADR 0045) el negocio timbra la factura de un pedido. Después llegaron la global del periodo (28B, ADR 0046), los complementos de pago (28C, ADR 0047) y las notas de crédito (28D, ADR 0048). Cada CFDI vive donde nació: la factura, sus complementos y sus notas en el detalle del pedido, y la global en Reportes › Factura global. No había un lugar con todo lo que el negocio timbró en el mes ni una forma de bajar sus XML, que es lo primero que pide el contador cada mes. Las ADR 0047 y 0048 dejaron fuera «un reporte de complementos por periodo» y «un reporte de notas por periodo».

Tomé las decisiones de la Fase 28H con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Qué entra:** todo CFDI que TienditApp pidió al PAC para el negocio con fecha en el mes elegido, en la zona del negocio:
   - facturas de pedido y globales (ingreso), complementos de pago (pago) y notas de crédito (egreso);
   - en cualquier estado: timbrando, timbrado, rechazado, cancelándose o cancelado. Los rechazados no son CFDI, pero explican los folios que faltan.
   - La fecha es la del CFDI (`IssuedAt`, que se fija al pedirlo y no cambia en los reintentos). El mes empieza a la medianoche del negocio (`TenantDateTimeResolver`), como sus reportes.
2. **Quién:** el dueño o un administrador (`RequiresTenantManager`), como la global: la lista lleva el RFC y el nombre de los clientes. En el menú va en Reportes › CFDI emitidos (área `businessSettings`).
3. **La lista** (`GET /api/v1/invoicing/issued-cfdis?year&month&kind&status&search&pageNumber&pageSize`):
   - Una sola consulta con `UNION ALL` de las cuatro tablas. Cada una conserva su filtro de negocio y se acota al mes antes de unirse. Las cuatro proyecciones nombran los mismos campos en el mismo orden.
   - Del más reciente al más antiguo (fecha, serie, folio). Con un tipo elegido se consulta solo su tabla.
   - El receptor de un complemento o de una nota es el de su factura, porque lo copian. El de la global es el público en general.
   - Cada fila dice:
     - su pedido;
     - la factura de la que es un complemento o una nota, o a la que sustituye una factura (Fase 28G), y la que la sustituye;
     - el periodo de una global y sus ventas;
     - si tiene XML;
     - por qué la rechazó el PAC.
   - La búsqueda encuentra:
     - el folio (F-12 o 12);
     - el UUID;
     - el RFC o una parte del nombre del receptor (los nombres se guardan en mayúsculas, como los pide el SAT);
     - el folio del pedido.
4. **El resumen** (`GET …/issued-cfdis/summary?year&month`), por tipo:
   - cuántos están vigentes y lo que suman (subtotal, impuestos trasladados y total). Vigentes son los timbrados y los que esperan que el cliente acepte su cancelación: el SAT los tiene vigentes hasta entonces. El CFDI de un complemento no tiene subtotal ni impuestos propios: suma lo que declara pagado;
   - cuántos se cancelaron, esperan su cancelación, se están timbrando o fueron rechazados.
   - Se suma en el servidor sobre lo que se leyó del mes, en `decimal`. No da un «neto» de ingresos menos notas: eso lo decide el contador.
5. **La descarga** (`GET …/issued-cfdis/export?year&month&format`):
   - `Zip` (la de siempre): `CFDI_{aaaa-mm}.zip` con el XML timbrado de cada CFDI que se timbró, vigente, cancelándose o cancelado. Van en `ingresos/`, `pagos/` y `egresos/` como `{serie}-{folio}_{UUID}.xml`, y al final la lista `CFDI_{aaaa-mm}.csv`.
   - `Csv`: la lista sola.
   - **La lista** (UTF-8 con marca para que Excel lea los acentos, coma y punto decimal):
     - del documento: tipo de comprobante, documento, serie, folio y UUID;
     - fechas y estado: la fecha del CFDI, la de timbrado, el estado como lo dice el SAT, y la fecha y el motivo de la cancelación;
     - receptor: RFC, nombre y uso, tomados del CFDI que se pidió;
     - pago: método y forma;
     - importes: subtotal, impuestos, total y lo pagado de un complemento;
     - relación: el UUID relacionado, el pedido y el archivo del XML en el ZIP.
   - Una celda de texto que empieza con `=`, `+`, `-`, `@`, tabulador o retorno va con un apóstrofo: una hoja de cálculo no la corre como fórmula.
   - Un XML que no está en los archivos privados se marca «No se encontró» en la lista, y el log registra el tipo y el id del CFDI, sin datos del cliente.
   - Hasta 5,000 CFDI por descarga, leídos a memoria; los XML se leen de ocho en ocho. Con más, responde 400 y dice que se pidan con la descarga masiva del SAT.
   - Sin PDF: el PAC tendría que dibujar cada uno que no se guardó. Se descargan uno por uno desde la fila.
6. **La pantalla:**
   - Se abre en el mes actual del negocio, o en el de la dirección (`?year=&month=`, para volver al mismo mes desde un pedido), con flechas al mes anterior y al siguiente (desde enero de 2022, cuando empezó CFDI 4.0, hasta el actual).
   - «Descargar XML» y «Descargar lista» llevan todo el mes, sin los filtros de la lista, y se apagan en un mes sin CFDI.
   - El resumen en una tabla, y la lista con filtros de tipo y estado, búsqueda y paginación. Otro mes, tipo, estado o búsqueda no deja a la vista lo anterior mientras llega lo nuevo; otra página de la misma lista sí, atenuada.
   - Cada fila lleva un enlace a su pedido o a su global, y «Descargar XML» y «Descargar PDF» con las rutas de cada tipo.
   - Mientras un CFDI se timbra, la lista y el resumen se leen otra vez cada 30 segundos.
7. **La API se mantiene compatible:** solo agrega las tres rutas, `IssuedCfdiDto`, `IssuedCfdiSummaryDto`, `IssuedCfdiKindSummaryDto` y los enums `IssuedCfdiKind` e `IssuedCfdiExportFormat`. Nada se quita ni cambia de nombre.
8. **Sin migración:** los índices `(TenantId, Serie, Folio)` de cada tabla ya acotan por negocio, y un negocio timbra pocos CFDI al mes.
9. **Fuera de esta fase:**
   - los PDF dentro del ZIP y los acuses de cancelación;
   - los CFDI recibidos (la descarga masiva del SAT);
   - mandar el ZIP al contador por correo;
   - rangos libres de fechas;
   - más de 5,000 CFDI en un mes;
   - el IVA cobrado de los complementos en el resumen.

## Consecuencias

- **Lo que logra:**
  - El negocio entrega al contador, en un archivo, los XML del mes y la lista con el estado de cada uno, sin abrir pedido por pedido.
  - Los complementos y las notas del periodo ya se ven juntos.
  - Un folio que falta se explica en la lista.
- **Lo que limita:**
  - El mes se calcula con la zona que el negocio tiene hoy. Si cambió de zona, un CFDI cerca del cambio de mes puede caer en el otro mes; la lista dice su fecha tal como la lleva el CFDI.
  - La lista es fiscal: también trae los CFDI de pedidos de un módulo que el negocio ya no tiene, y el ZIP los incluye. Sus descargas sueltas siguen la regla del pedido (ADR 0045) y no se encuentran.
  - Un mes con más de 5,000 CFDI no se descarga desde aquí.
  - La búsqueda por nombre en SQL Server distingue acentos: «PUBLICO EN GENERAL» no se encuentra con «público».
  - La primera visita sube 296 B (de 180,037 a 180,333 B, contra `master` en `1e52cf7`) por la entrada del menú, con sus palabras de búsqueda, y los dos enums del contrato, que viven en el primer chunk. El presupuesto sube a 180,400 B.
- **Compatibilidad durante el despliegue:** no hay cambios de base. La versión anterior (28G) no conoce las rutas nuevas, y volver a ella solo quita la pantalla.
- **Reversibilidad:** total. Es de solo lectura y no guarda nada.
