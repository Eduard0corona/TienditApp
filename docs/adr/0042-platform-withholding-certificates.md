# ADR 0042: Conciliar las retenciones de cada plataforma (CFDI de retenciones)

- Estado: Aceptada
- Fecha: 2026-10-22

## Contexto

Rappi, Uber Eats y DiDi Food le retienen al negocio ISR e IVA de lo que cobran por él, y cada mes (Uber Eats) o cada semana (DiDi) le emiten un CFDI de Retenciones e información de pagos 2.0 con la clave 26, «servicios mediante plataformas tecnológicas». Las retenciones son pagos a cuenta de los impuestos del negocio: lo que dice ese CFDI es lo que el negocio acredita en su declaración.

TienditApp ya calcula por pedido el ISR y el IVA que la plataforma debería retener cuando cobró ella ([ADR 0013](0013-delivery-platform-orders.md)), con los porcentajes de la configuración de la plataforma, y la liquidación ([ADR 0015](0015-delivery-platform-settlements.md)) los cuenta en lo que espera de cada depósito. Faltaba comparar ese cálculo con lo que la plataforma dice que retuvo. El roadmap de Plataformas de reparto lo pide como «CFDI de retenciones».

Lo que importa del XML 2.0 (anexo 20 de retenciones del SAT):

- la raíz `retenciones:Retenciones` (espacio de nombres `http://www.sat.gob.mx/esquemas/retencionpago/2`), con `Version="2.0"`, `CveRetenc` y `FechaExp` (sin zona);
- el emisor (`RfcE`, `NomDenRazSocE`), el receptor nacional (`Nacional/@RfcR`) y el periodo (`MesIni`, `MesFin`, `Ejercicio`);
- en `Totales`, `MontoTotOperacion`, `MontoTotRet` y un `ImpRetenidos` por impuesto: `ImpuestoRet` 001 (ISR), 002 (IVA) o 003 (IEPS), `MontoRet` y, opcional, `BaseRet`;
- el folio fiscal en `Complemento/tfd:TimbreFiscalDigital/@UUID`.

El CFDI no nombra pedidos: se concilia por los totales del periodo. Desde 2026 la retención de ISR es de 2.5 % (también para personas morales); de 2021 a 2025 fue de 1 %, solo a personas físicas. El IVA retenido es la mitad del IVA, y solo cuando la plataforma cobra.

Tomé las decisiones de la Fase 22J con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Se guarda lo que dice el XML, no el archivo.**
   - `PlatformWithholdingCertificate` (Domain.Orders.Platforms, `ITenantScoped`) guarda la plataforma que eligió la persona, el UUID, el emisor, el RFC del receptor, el periodo, la fecha de emisión tal como la escribe el XML (sin zona), el total de operaciones, el ISR y el IVA retenidos con sus bases (cuando el XML las trae) y el total retenido.
   - El total retenido incluye cualquier IEPS, así que no es menor que ISR más IVA, salvo por los centavos del redondeo (hasta 2 centavos, también en el CHECK de la tabla): un XML con más de dos decimales se redondea impuesto por impuesto y total aparte.
   - Si el XML dice que sustituye a otro CFDI (`CfdiRetenRelacionados`, la relación 04 que usa la plataforma cuando cancela uno y emite otro en su lugar), guarda ese folio.
   - El dominio rechaza lo que ningún CFDI dice: meses fuera de 01 a 12 o al revés, un ejercicio antes de 2019 (la clave 26 existe desde junio de 2019), un periodo que empieza después del mes del negocio, y un importe negativo, de más de dos decimales o de más de $1,000,000,000.
2. **El lector, en Application, no confía en el archivo.**
   - `PlatformWithholdingXml` lee hasta 512 KB, sin DTD ni resolutor: un XML que define entidades o apunta a otro archivo no se lee.
   - Exige la versión 2.0, la clave 26, un receptor nacional y el UUID del timbre.
   - Suma cada `ImpRetenidos` por impuesto tal como viene y redondea la suma una sola vez. La base de un impuesto cuenta solo si todas sus líneas la traen. Una línea de más de $1,000,000,000 se rechaza antes de sumarla: la suma no cabría en un `decimal` y respondería 500.
   - Cada cifra se redondea a centavos, alejándose de cero, como el SAT redondea un importe a los decimales de su moneda.
   - Busca el timbre en cualquiera de sus complementos.
   - Dice qué es lo que no lee: una factura (CFDI de ingreso), un CFDI de retenciones 1.0, otra clave, un PDF o un dato que falta.
   - No valida el sello ni consulta al SAT si el CFDI está vigente.
3. **Registrar: dueños y administradores, una vez por negocio.**
   - `POST /api/v1/platform-withholdings` (`RegisterPlatformWithholdingCertificateCommand`, `RequiresTenantManager`) recibe la plataforma y el texto del XML.
   - Si el negocio escribió su RFC en Configuración y el CFDI va a otro, se rechaza. El mensaje no nombra ningún RFC.
   - Un UUID vigente en el negocio responde 409 `conflict` y dice en qué plataforma y periodo está. El índice único filtrado `(TenantId, Uuid) WHERE RemovedAt IS NULL` lo garantiza también cuando dos personas lo suben a la vez. El mismo CFDI en otro negocio es otro registro.
   - Un CFDI que sustituye a otro vigente lo quita en el mismo guardado, con el motivo «Lo sustituye el CFDI con folio …».
   - Una carpeta descargada trae los dos, en cualquier orden, y solo cuenta el nuevo. Uno que llega después del que lo sustituye (vigente o ya quitado) se guarda quitado desde el principio, con ese motivo, y quita al que él sustituía. Así se conoce la cadena: A sustituido por B y B por C, subidos A, C, B, dejan vigente solo a C. Si ese CFDI ya estaba guardado, responde 409 `conflict`.
   - Todo se busca dentro del negocio: un CFDI de otro negocio que dice sustituir uno de este no lo quita.
   - La bitácora (`PlatformWithholdingCertificateRegistered`) va en el mismo guardado, con la plataforma, el UUID, el que sustituye, el periodo y los importes, sin RFC.
4. **Quitar, con motivo; nunca borrar.**
   - `POST /api/v1/platform-withholdings/{id}/remove` marca el CFDI como quitado con su motivo. Deja de contar en la conciliación pero se queda en la lista, y ese UUID puede subirse otra vez (con otra plataforma, por ejemplo).
   - Uno ya quitado (por alguien más, después de que la persona cargó la lista) responde 409 `concurrency_conflict` y la lista se vuelve a leer. `RemovedAt` es token de concurrencia: dos que lo quitan a la vez dejan el primero.
   - Entra a la bitácora como `PlatformWithholdingCertificateRemoved`, con el motivo.
5. **La conciliación, por mes del negocio.**
   - `GET /api/v1/platform-withholdings?channel=&year=` (`ViewReports`) devuelve los 12 meses del año y el total.
   - Lo calculado son los pedidos de la plataforma que cobró ella, registrados en cada mes en la zona horaria del negocio. Para cada uno, su ISR, su IVA retenido y sus ventas sin IVA, en proporción a lo que sigue pagado (una devolución de la mitad retiene la mitad), con redondeo bancario por pedido, como la liquidación (`PlatformSettlementCalculator.InProportion`).
   - Lo del CFDI: los vigentes cuyos periodos se traslapan se comparan juntos, por ejemplo una semana de DiDi que cruza el cambio de mes con las semanas de los dos meses. Cuentan completos en el último mes que cubren entre todos, y ese mes compara contra lo calculado en todos esos meses (el mes lo dice). Los meses anteriores que cubren dicen en qué mes se comparan y no tienen diferencia propia. Así ningún mes se compara dos veces; un CFDI de varios meses solo es el caso más simple.
   - La diferencia por impuesto es CFDI menos calculado: positiva cuando la plataforma retuvo de más. Sin CFDI en el mes, no hay diferencia. La del año suma solo los meses que cubre algún CFDI, cada uno una vez: el mes en curso todavía no tiene el suyo.
   - Cada CFDI dice la tasa que aplicó (retenido entre base). La del IVA solo cuando su base es el IVA (la retención resulta de 25 % o más); si la base es el importe sin IVA, no se da. Junto con los porcentajes con los que TienditApp calcula hoy, eso explica una diferencia por tasa, como el cambio de 1 % a 2.5 %.
   - La lista trae los CFDI del año, vigentes y quitados, hasta 200, y avisa si hay más. Los meses los cuentan todos.
6. **Cliente.**
   - Reportes › Liquidaciones › «Retenciones» (`/reports/settlements/withholdings`) tiene plataforma y año.
   - Muestra la tabla por mes hasta el mes del negocio, con «Cuadra» cuando la diferencia es $0 y guiones cuando no hay CFDI, y la lista de CFDI con «Quitar».
   - «Subir CFDI de retenciones» toma varios XML a la vez. Manda cada uno por separado y dice si entró, si se guardó sin contar (ya sustituido) o por qué no; un archivo que el navegador ya no puede leer también se dice.
7. **Migración `AddPlatformWithholdingCertificates`, compatible con la versión anterior.**
   - Agrega solo la tabla, con sus restricciones (plataforma, periodo, importes, quitado con motivo) y sus dos índices.
   - La versión anterior no la conoce y sigue registrando y moviendo pedidos.
   - `rollback.sql` se detiene con 50033 si hay algún CFDI guardado, quitados incluidos, porque borrarlos perdería lo que el negocio subió. Desde ese momento solo se avanza.
   - `apply.sql` va a producción antes del merge.
8. **Fuera de esta fase:**
   - validar el sello o el estado del CFDI en el SAT;
   - la descarga masiva desde el SAT y las APIs de las plataformas;
   - el CFDI de retenciones 1.0;
   - la conciliación por pedido (el CFDI no los nombra);
   - escoger la tasa por año o por tipo de persona (sale de la configuración de la plataforma);
   - conciliar el IEPS.

## Consecuencias

- **Lo que logra:**
  - El negocio ve cada mes si lo que la plataforma le retuvo coincide con lo que TienditApp calculó, y dónde no.
  - La tasa de cada CFDI explica una diferencia por la configuración de la plataforma.
  - Los CFDI subidos quedan juntos, sin duplicados y con su historial.
- **Lo que limita:**
  - Lo calculado depende de que los pedidos de la plataforma se registren en TienditApp y de los porcentajes configurados cuando se registraron. Un pedido que no se capturó no está en el cálculo, pero sí en el CFDI.
  - Un pedido se cuenta en el mes en que se registró. La plataforma puede contarlo en el mes en que lo pagó, así que alrededor del cambio de mes se ven diferencias que se compensan en el mes siguiente.
  - Lo calculado de un año completo se lee pedido por pedido para redondear igual que la liquidación. Un negocio con muchísimos pedidos de una plataforma en un año lee más filas.
  - Se confía en el XML: un archivo alterado con UUID y montos inventados se registra igual. Lo que acredita el negocio ante el SAT es el CFDI timbrado de verdad.
- **Compatibilidad durante el despliegue:** la versión anterior (22I) funciona sobre la base migrada, y `/health/schema` sigue sano.
- **Reversibilidad:** volver a la versión anterior no necesita `rollback.sql`. Antes de que alguien suba un CFDI, `rollback.sql` quita la tabla. Después, solo se avanza.
