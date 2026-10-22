# ADR 0040: Importar el estado de cuenta de cada plataforma

- Estado: Aceptada
- Fecha: 2026-10-21

## Contexto

Desde la Fase 22G ([ADR 0015](0015-delivery-platform-settlements.md)), cada depósito de Rappi, Uber Eats o DiDi Food se concilia en Reportes › Liquidaciones. La persona marca uno por uno los pedidos que trae el estado de cuenta y explica el resto con ajustes. Con cientos de pedidos por semana, marcar a mano cuesta y se equivoca. Las diferencias por pedido, como la parte de una promoción o un faltante, no se ven hasta que la liquidación no cuadra.

El ADR 0015 descartó importar el estado de cuenta: cada plataforma usa un formato distinto y lo cambia sin aviso. Lo dejó como el primero de sus siguientes pasos, y el roadmap de Plataformas de reparto lo pide.

Tomé las decisiones de la Fase 22H con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **El archivo se lee en el navegador; no se sube.**
   - La persona elige un CSV y la pantalla lo lee: UTF-8, con o sin su marca, UTF-16 con su marca («Texto Unicode» de Excel) o Windows-1252, como Excel lo guarda en español. Un libro de Excel o un PDF se rechazan con cómo guardarlos como CSV.
   - Reconoce como separador la coma, el punto y coma o el tabulador (el que parte igual los primeros renglones), y las comillas de RFC 4180.
   - El encabezado es el primer renglón con dos columnas o más: un título arriba no estorba.
   - Lee archivos de hasta 5 MB y 5,000 renglones de datos.
   - Al servidor solo llegan los renglones elegidos (línea, folio, importe y concepto). No hay subida de archivos ni nada que validar de un archivo.
   - Un estado de cuenta en Excel se guarda como CSV. No hay dependencia nueva.
2. **Las columnas las elige la persona.**
   - La pantalla propone las columnas de folio, importe y concepto (opcional) por las palabras del encabezado: folio, ID de pedido u order id; neto, importe, monto o total; concepto, descripción o tipo.
   - Recuerda por plataforma, en ese dispositivo, las que se usaron.
   - Muestra cómo se leen los primeros renglones antes de comparar.
3. **Los importes, como los escriben los estados de cuenta.**
   - Formatos que lee: «$1,234.56», «-1,234.56», «(1,234.56)», «+12», «$-12», «1.234,56», «12,5», «MX$12.00» y «1234.5 MXN».
   - El separador decimal sale de la columna: si sus importes usan coma decimal, «1.234» son mil doscientos; si usan punto, «1,234» también. Sin saberlo, tres cifras después del separador agrupan miles y «12.345» no se lee.
   - Un renglón cuyo importe no se lee, con más de dos decimales o con un folio de más de 60 caracteres no se manda, y la pantalla dice cuál y por qué.
   - Tampoco se manda un renglón de totales del archivo («Total», «Total:», «TOTAL A PAGAR», «Gran total», «Subtotal»…) que no trae folio, porque contaría todo dos veces. El último total que no es subtotal se compara con lo que se tomó.
4. **El servidor compara y no guarda nada.**
   - La comparación es `POST /api/v1/platform-settlements/statement-matches` (`MatchPlatformStatementQuery`). Recibe la plataforma, la liquidación abierta (o ninguna, para una nueva), el periodo y de 1 a 5,000 renglones.
   - Cada folio se normaliza como lo guardan los pedidos (`PlatformOrderNumber`).
   - Los renglones de un mismo folio se suman: el pedido y lo que se le descuenta en otra línea, como una promoción.
   - Los renglones sin folio se agrupan por concepto.
   - Los folios que no pueden ser de la plataforma, como «R/9», van aparte.
   - Cada folio dice qué es (`PlatformStatementMatchStatus`):
     - **ya en esta liquidación** o **pendiente**, registrado hasta el final del periodo: la liquidación lo toma. Trae su neto esperado y la diferencia contra el estado de cuenta. Un pedido del folio que la liquidación ya tiene es el que la persona eligió, aunque otro del mismo folio esté pendiente;
     - **después del periodo:** lo toma una liquidación posterior. También uno que la liquidación tiene, si se acortó el periodo;
     - **en otra liquidación vigente**, con cuál;
     - **sin nada que esperar:** se devolvió completo o el negocio todavía no lo cobra;
     - **varios pedidos que puede tomar con el mismo folio,** ninguno o más de uno ya en ella: Uber Eats y DiDi Food repiten folios entre semanas, y la persona elige cuál;
     - **ninguno:** no es un pedido de la plataforma en TienditApp.
   - Lee los pedidos que la liquidación puede tomar y, de los demás folios, solo el pedido más reciente que dice por qué: el historial de un folio repetido no se lee completo.
   - Responde también los totales: lo que suma el estado de cuenta, lo de los pedidos que entran contra lo esperado, los conceptos y lo que no entra.
   - Lee con el filtro del negocio. Una liquidación de otro negocio responde 404, una de otra plataforma o una que no está abierta, 400.
   - Rechaza (400) lo que guardar rechazaría: un periodo que termina después de hoy, del negocio, y una liquidación nueva con otra de la plataforma abierta.
   - Pasado el límite de renglones responde un solo mensaje, y la petición admite hasta 8 MB.
   - Pueden usarla los dueños y administradores (`[RequiresTenantManager]`), los mismos que guardan una liquidación.
5. **Aplicar lo deja en la pantalla; guardar es lo de siempre.**
   - **Pedidos:** se incluyen los que entran y se quitan los incluidos que el estado de cuenta no trae, que siguen pendientes. De un folio con varios pedidos ninguno se incluye solo: se queda incluido el que la persona ya había marcado o guardado, y los demás aparecen en la lista para que elija.
   - **Ajustes:** se proponen con el prefijo «Estado de cuenta:», y la persona los revisa:
     - uno «Otro» con la diferencia de los pedidos que entran;
     - uno «Otro» con los folios que no entran aquí y los que no se leen;
     - uno por concepto, con el motivo que sugieren sus palabras: promociones, reclamos, cancelaciones, cargos de la plataforma o saldo anterior.
   - Importar otro estado de cuenta reemplaza los ajustes con ese prefijo. Los escritos a mano se quedan.
   - Nunca pasa de 100 ajustes: los conceptos que no caben van juntos en uno, y con 100 escritos a mano la pantalla dice que no caben. Un ajuste de más de $1,000,000 se avisa para partirlo.
   - **Depósito y notas:** lo depositado toma el total del estado de cuenta si estaba vacío, solo cuando el archivo cuadra: sin renglones con importe fuera y con su total, si lo trae, igual a lo que se tomó. Si no, la pantalla dice por qué y la persona escribe lo que llegó al banco. Las notas anotan el archivo y cuántos renglones tenía.
   - Si después cambia el periodo, la pantalla avisa que el estado de cuenta se comparó con otro y pide importarlo otra vez. Un 409 porque otra liquidación tomó un pedido olvida los pedidos que trajo el estado de cuenta y deja la lista leída otra vez.
   - **Guardar:** se guarda con el `PUT`/`POST` de siempre, que vuelve a validar y a calcular cada pedido. La liquidación se cierra igual, solo con la diferencia en $0.00.
6. **Sin migración ni cambio de dominio.** El contrato agrega una ruta, sus DTO y el enum `PlatformStatementMatchStatus`. Nada se quita.
7. **Fuera de esta fase:**
   - PDF y XLSX;
   - el ID largo del pedido (llegó en la Fase 22I, [ADR 0041](0041-platform-order-ids.md));
   - guardar el estado de cuenta en la base;
   - importarlo solo, por correo o por la API de la plataforma;
   - un formato fijo por plataforma.

## Consecuencias

- **Lo que logra:**
  - Una liquidación de cientos de pedidos se arma con un archivo.
  - La diferencia de cada pedido queda a la vista.
  - Los cargos sin folio llegan como ajustes con su motivo sugerido, y cada peso del depósito sigue explicado.
  - Funciona con los formatos de las tres plataformas, y con sus cambios, porque las columnas las elige la persona.
- **Lo que limita:**
  - **Formato:** depende de que el estado de cuenta traiga el folio corto que se captura en Vender. Con otra llave, como el ID largo, no encuentra los pedidos. Resuelto en la Fase 22I: cada renglón se compara también con el ID largo del pedido ([ADR 0041](0041-platform-order-ids.md)).
  - **Dónde se ven los cambios:** la comparación no guarda nada. Lo aplicado vive en la pantalla hasta guardar, y recargar lo pierde, como cualquier cambio sin guardar.
  - **Motivos:** se sugieren por palabras. Un concepto con otro nombre llega como «Otro» y la persona lo cambia.
  - **Diferencias por pedido:** van en un solo ajuste «Otro». La pantalla de la comparación las enlista, pero la liquidación no guarda cuál pedido trajo cuánto.
  - **Folios repetidos:** un folio con varios pedidos pendientes no se resuelve solo.
  - **Columnas recordadas:** viven en el dispositivo y se pierden con sus datos o en una ventana privada.
- **Compatibilidad durante el despliegue:** la versión anterior (27C) no tiene la ruta ni el botón; las liquidaciones guardadas con un estado de cuenta importado son liquidaciones comunes.
- **Reversibilidad:** no hay migración. Volver a la versión anterior quita la importación, y lo guardado se queda igual.
