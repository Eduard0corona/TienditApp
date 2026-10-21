# ADR 0038: Transferencias entre ubicaciones

- Estado: Aceptada
- Fecha: 2026-10-21

## Contexto

Un negocio con bodega y sucursales lleva mercancía de una ubicación a otra todos los días. El libro mayor ya tenía los tipos `TransferOut` y `TransferIn` ([inventario y compras](../domain/inventory-and-purchasing.md)), pero solo el movimiento manual los creaba, una pierna a la vez y sin liga entre las dos:

- una salida podía quedar sin su entrada, y la mercancía desaparecía del inventario;
- nada decía qué salida y qué entrada eran el mismo traslado;
- la salida se valoraba al costo promedio (o sin costo si no había promedio) y la entrada al promedio, al último costo o al de referencia: con promedio en cero, las dos piernas valían distinto.

El roadmap de Inventario pedía «concurrencia robusta, transferencias atómicas y reconciliación del kardex», y el guardrail del dominio dice que las transferencias «identifican origen y destino y se ejecutan como unidad coordinada». La reconciliación con el anaquel llegó con el conteo físico ([ADR 0037](0037-inventory-counts.md)); esta fase hace las transferencias, y la del kardex con la existencia guardada llegó después ([ADR 0039](0039-ledger-reconciliation.md)).

Tomé las decisiones de la Fase 27B con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Una transferencia es de una vez** (`InventoryTransfer`): los artículos salen de una ubicación y entran a otra en la misma transacción, todo o nada. No hay «en tránsito».
   - Tiene folio `TRF-yyyyMMdd-nnnn`, del contador del negocio por día UTC, como `INV`, `PO` y `CNT`.
   - Guarda de qué ubicación a cuál (distintas y activas), cuándo y quién, notas (hasta 500), cuántos artículos (de 1 a 200) y cuánto valían, su salida y su entrada, y la llave con la que la mandó la app.
   - Hecha, no cambia. Deshacerla es hacer otra al revés.
2. **Artículos:** activos, sin repetir, con cantidad de más de 0 a 9,999,999, con 3 decimales como máximo y entera en piezas. El origen debe tener la cantidad; si no, se rechaza nombrando cada artículo con lo que hay («No hay suficiente en Bodega: Papas 45 g (hay 15)»). Los productos que controlan sus existencias se transfieren como cualquier artículo: así se lleva de la bodega a la sucursal que los vende.
3. **Las dos piernas valen lo mismo.** Cada artículo se valora a su costo promedio; sin promedio, a su último costo; sin él, a su costo de referencia; sin ninguno, sin costo. Ese costo se manda explícito a la salida y a la entrada. El costo promedio no cambia: una transferencia no es una compra.
4. **Dos movimientos del libro mayor**, en la misma transacción:
   - un `TransferOut` del origen y un `TransferIn` al destino, con el motivo nuevo «Transferencia» (`Transfer`, 11);
   - cada uno con su folio `INV`, la referencia del folio de la transferencia y la nota «Transferencia TRF-… a Mostrador» o «… de Bodega»;
   - cada uno con una referencia externa de la transferencia (`TRF:{id}:OUT` y `TRF:{id}:IN`), protegida por el índice único de las referencias.
5. **Los movimientos manuales ya no hacen transferencias.** El movimiento manual rechaza los tipos `TransferIn` y `TransferOut` y el motivo «Transferencia», y `TRF:` queda reservado como `ORDER:`, `PO:` y `COUNT:`. El modal de «Registrar movimiento» ya no los ofrece y dice que las transferencias van en su pestaña. El historial y sus filtros los siguen enseñando, también los movimientos de transferencia manuales de antes.
6. **Idempotencia.** La app manda una llave por transferencia (`IdempotencyKey`, única por negocio): la misma llave otra vez contesta la transferencia ya hecha, sin mover nada. El diálogo conserva su llave, así que reintentar después de un error de red nunca transfiere dos veces, y no se cierra mientras manda.
   - Si la misma llave llega con otras ubicaciones o artículos (se perdió la respuesta y la persona cambió la transferencia), responde 409: «Esa transferencia ya se hizo con lo primero que mandaste». No hace otra ni contesta como si fuera la nueva.
   - Después de un rechazo (400 o 409) no se guardó nada, así que lo corregido se manda con la misma llave.
7. **Concurrencia.**
   - El candado de ubicación de la 27A, renombrado `IInventoryLocationLock`, se toma para las dos ubicaciones en orden de su id (`sp_getapplock` en la transacción de la petición). Dos transferencias de la misma ubicación, o un conteo y una transferencia, van en fila; y dos transferencias en sentido contrario entre las mismas dos ubicaciones hacen fila sin interbloquearse.
   - Lo que hay en el origen se lee dentro del candado. Dos transferencias a la vez desde el mismo origen nunca se llevan más de lo que hay: la segunda ve lo que dejó la primera.
   - Las ventas no toman el candado. Una venta que se guarda entre la lectura y la transferencia choca con el token de la existencia (409) o la dejaría en negativo («Se vendió o se movió un artículo mientras se transfería», 409): no se mueve nada y se vuelve a intentar.
   - En SQL Server el recurso del candado conserva su nombre de la 27A, así que mientras conviven las dos versiones un conteo en cualquiera hace fila con el otro.
8. **Conteos abiertos.** Se puede transferir durante un conteo abierto, como se puede vender: el conteo mide cada diferencia contra lo que había al contar, así que lo transferido después no se pierde.
9. **Permisos:** `ManageInventory` y la capacidad de inventario, como los movimientos y los conteos. La transferencia queda en la actividad (`InventoryTransferCreated`) con su folio, sus ubicaciones, cuántos artículos, cuánto valían y los folios de sus movimientos.
10. **Lectura:** la lista, la más reciente primero, por ubicación (de la que salió o a la que entró) y por folio o nombre de ubicación; y el detalle con cada artículo: cantidad, costo, valor, lo que tenía el origen y el destino antes y después, y los folios de las dos piernas.
11. **Se ve** en Inventario › Transferencias, después de «Entradas y salidas»:
    - la lista, con su filtro de ubicación y la búsqueda;
    - «Nueva transferencia»: de, a (otra ubicación activa), los artículos con existencias en el origen con lo que hay de cada uno, la cantidad y las notas;
    - el detalle, que abre sus dos movimientos.

    Al transferir se vuelven a leer existencias, movimientos, el historial por insumo, los insumos y los conteos abiertos. Los dos movimientos aparecen en «Entradas y salidas» y en el historial por insumo como cualquier movimiento.
12. **Fuera de esta fase:**
    - «en tránsito»: enviar, recibir y las diferencias entre lo enviado y lo recibido;
    - transferir entre negocios;
    - por lote o con caducidad;
    - deshacer una transferencia (se hace otra al revés);
    - que una sucursal pida a la bodega;
    - imprimir un remito;
    - que otra persona la apruebe.
13. **La migración** `20261021040954_AddInventoryTransfers` agrega la tabla `InventoryTransfers`:
    - tiene llaves foráneas sin borrado en cascada a sus dos ubicaciones y sus dos movimientos;
    - `CK_InventoryTransfers_Locations` exige ubicaciones distintas, `CK_InventoryTransfers_Movements` dos movimientos distintos, `CK_InventoryTransfers_Lines` de 1 a 200 artículos y `CK_InventoryTransfers_TotalCost` un valor no negativo;
    - sus índices únicos son el folio y la llave por negocio, y cada movimiento (una transferencia por movimiento);
    - tiene además un índice para la lista y los de las llaves foráneas.

## Consecuencias

- **Lo que logra:**
  - Llevar mercancía de una ubicación a otra ya no deja una salida sin su entrada.
  - Cada movimiento de una transferencia dice de qué transferencia es, y la transferencia, quién la hizo, cuándo y por qué.
  - Lo que sale vale lo mismo que lo que entra, con o sin costo promedio.
  - Un reintento o un doble toque no transfiere dos veces.
- **Lo que limita:**
  - No hay «en tránsito»: lo que sale entra al mismo tiempo. Si llega menos de lo que salió, la diferencia se corrige con un conteo o un ajuste en el destino.
  - Una transferencia hecha no se edita ni se borra: se hace otra al revés.
  - En una tienda abierta, transferir puede chocar con una venta (409). Basta con volver a intentarlo. También al revés: una venta que leyó las existencias antes de que se guardara una transferencia de los mismos artículos responde 409 y se cobra otra vez, como ya pasa con los movimientos manuales y los conteos.
  - El diálogo lee hasta 500 artículos del origen a la vez y busca en el servidor por nombre, código o código de barras; con más, dice que hay más y se encuentran buscando.
  - Ningún usuario está limitado a una sucursal: quien maneja el inventario transfiere entre todas.
- **Compatibilidad durante el despliegue:**
  - La versión anterior (27A) funciona con la base migrada: no conoce la tabla.
  - Las piernas de una transferencia son movimientos de inventario comunes (tipos 5 y 6) que la versión anterior sabe leer; el motivo nuevo lo enseña como «11».
  - Mientras conviven las dos versiones, la anterior todavía acepta transferencias manuales de una pierna y una referencia externa `TRF:` en un movimiento manual. El índice único las sigue protegiendo, porque la referencia de una transferencia lleva su identificador.
- **Reversibilidad:**
  - `rollback.sql` borra la tabla solo si no hay ninguna transferencia.
  - Si hay alguna, se detiene con el error 50031 sin cambiar nada, y solo queda corregir hacia adelante: cada pierna quedaría como un movimiento suelto, sin la transferencia que las une.
