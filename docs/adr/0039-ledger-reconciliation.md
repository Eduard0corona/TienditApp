# ADR 0039: Revisar que el historial cuadre con las existencias

- Estado: Aceptada
- Fecha: 2026-10-21

## Contexto

Cada artículo tiene, en cada ubicación, una existencia guardada (`InventoryStock.OnHandQuantity`) y un historial: los movimientos del libro mayor que la cambiaron, con lo que había antes y después de cada uno ([inventario y compras](../domain/inventory-and-purchasing.md)). Todo cambio correcto mueve las dos por igual, así que la existencia debe ser lo que suman sus movimientos: lo que entra menos lo que sale.

Se separan cuando algo toca la existencia sin un movimiento o cuando un movimiento guarda mal su saldo, por ejemplo:

- una carga anterior al historial;
- un script;
- una versión con un error.

Nadie lo veía. El conteo físico ([ADR 0037](0037-inventory-counts.md)) cuadra la existencia con el anaquel, pero su ajuste mueve la existencia y el historial juntos, así que la diferencia entre los dos sigue ahí.

El roadmap de Inventario pedía «concurrencia robusta, transferencias atómicas y reconciliación del kardex». El conteo y las transferencias ([ADR 0038](0038-inventory-transfers.md)) ya llegaron, y faltaba «comparar el kardex con la existencia guardada». El guardrail del dominio dice que esas diferencias «se detectan y reconcilian mediante ajuste trazable».

Tomé las decisiones de la Fase 27C con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Revisar** (`GET /api/v1/inventory/reconciliation`) compara cada existencia del negocio, o de una ubicación, con lo que suma su historial.
   - Revisa también un historial sin existencia guardada y una existencia sin historial.
   - Dice cuántas existencias revisó y lista las que no cuadran, por ubicación y nombre, con la búsqueda por artículo o ubicación.
   - De cada una muestra la existencia, lo que suma el historial, la diferencia (existencia menos historial) y sus movimientos.
   - El historial se suma por tipo de movimiento y con el signo que aplica el libro mayor. `InventoryMovementTypes` dice qué tipos suman (entrada, ajuste a favor, transferencia de entrada, devolución de producción). El libro mayor y la revisión leen ese mismo signo, y una prueba fija el de cada tipo.
2. **Por qué no cuadra** (`GET /api/v1/inventory/reconciliation/{artículo}/{ubicación}`) recorre el historial en el orden en que se guardó: cuándo se guardó cada línea y, después, su folio. Así encuentra cada lugar donde se separa:
   - una existencia sin ningún movimiento;
   - lo que ya había antes del primer movimiento;
   - un cambio entre dos movimientos sin un movimiento;
   - un movimiento cuyo saldo no corresponde con lo que movió;
   - un cambio después del último movimiento;
   - los cuadres anteriores.

   Lo que suma cada lugar da la diferencia. Lee hasta las 50,000 líneas más recientes, y lo anterior queda como un solo lugar, «de antes de lo revisado». Lista los 50 lugares más recientes.
3. **Cuadrar** (`POST /api/v1/inventory/reconciliation`) anota en el historial lo que le faltaba para sumar la existencia, sin mover la existencia.
   - Recibe una ubicación y de 1 a 200 artículos, cada uno con la diferencia que se revisó.
   - Genera hasta dos movimientos del motivo nuevo «Cuadre del historial» (`LedgerReconciliation`, 12): un `AdjustmentIncrease` con lo que le faltaba al historial y un `AdjustmentDecrease` con lo que le sobraba.
   - Cada movimiento lleva su folio `INV` y una nota por artículo («Existencia 28; el historial sumaba 26»).
   - Cada línea guarda la existencia antes y después, sin cambio, y no lleva costo: no se movió mercancía.
   - La existencia, su token de concurrencia, su último movimiento y el costo promedio no cambian.
   - El movimiento solo acepta este motivo como ajuste, y solo este motivo anota sin mover la existencia (`InventoryMovement.AddReconciliationLine`).
   - El cuadre queda en la actividad (`InventoryLedgerReconciled`) con la ubicación, los folios y la existencia, el historial y la diferencia de cada artículo.
4. **La diferencia se calcula otra vez al cuadrar**, dentro del candado de la ubicación (`IInventoryLocationLock`, el de los conteos y las transferencias):
   - **ya en 0:** el artículo se salta, porque otro cuadre llegó primero o fue un doble toque;
   - **distinta de la revisada:** no se anota nada y responde 409, nombrando el artículo y su diferencia nueva;
   - **igual:** se anota.

   Un movimiento correcto cambia la existencia y el historial por igual, así que una venta que no espera el candado no cambia lo que se cuadra. No hace falta llave de idempotencia: mandar lo mismo otra vez encuentra todo cuadrado.
5. **Los movimientos manuales** rechazan el motivo «Cuadre del historial», y «Registrar movimiento» no lo ofrece. El historial y sus filtros lo enseñan como cualquier motivo.
6. **Permisos:** `ManageInventory` y la capacidad de inventario, como los movimientos, los conteos y las transferencias.
7. **Se ve** en Inventario › Historial por insumo › «Revisar que cuadre», una vista dentro de la pestaña. Tiene:
   - el filtro de ubicación, la búsqueda y el resumen («Se revisaron 24 existencias: 2 no cuadran con su historial», o «Todo cuadra»);
   - por artículo, «Por qué», que explica cada lugar en palabras, y «Cuadrar»;
   - con una ubicación elegida, «Cuadrar las N que se ven».

   Antes de cuadrar se confirma que la existencia no cambia y que, para saber cuánto hay de verdad, se cuenta. Al cuadrar se vuelven a leer el historial y las entradas y salidas.
8. **Sin migración:** el motivo es un entero de una columna sin restricción, y el resto son lecturas y movimientos comunes. No hay `apply.sql`.
9. **Fuera de esta fase:**
   - revisar sola o avisar en Inicio que algo no cuadra;
   - revisar el valor del historial o el costo promedio;
   - reparar el saldo antes y después de cada línea;
   - revisar todos los negocios desde la plataforma.

## Consecuencias

- **Lo que logra:**
  - Una existencia que alguien tocó sin un movimiento, o un movimiento que guardó mal su saldo, aparece, con dónde y cuánto.
  - Cuadrar deja el historial completo sin borrar ni reescribir nada: el cuadre es un movimiento más, con folio, quién y cuándo.
  - Después de cuadrar, cada movimiento correcto mantiene la existencia y el historial juntos, y la revisión vuelve a decir «Todo cuadra».
- **Lo que limita:**
  - Cuadrar no dice cuánto hay en el anaquel: toma la existencia guardada como la que cuenta. Para saber cuánto hay se cuenta ([ADR 0037](0037-inventory-counts.md)), antes o después.
  - Un cuadre no lleva costo. El historial anota la cantidad que le faltaba, pero no su valor; el valor de la existencia sigue siendo la existencia por el costo promedio.
  - El orden del historial es el de guardado y, en el mismo instante, el del folio. Dos líneas del mismo artículo y ubicación en el mismo movimiento se encadenan por su saldo. Más de 9,999 movimientos en un día romperían el orden del folio dentro de un mismo instante.
  - Revisar lee las sumas de todo el historial del negocio, o de la ubicación, cada vez que se abre. Es una consulta agrupada sobre el índice de las líneas por artículo y ubicación, para abrir de vez en cuando y no a cada venta.
  - Quien maneja el inventario puede cuadrar. Esa persona no puede separar la existencia del historial, así que un cuadre no esconde una merma; la actividad lo registra.
- **Compatibilidad durante el despliegue:**
  - La versión anterior (27B) funciona igual: no hay cambios en la base.
  - Un cuadre hecho con la versión nueva es, para la anterior, un ajuste común cuyo motivo enseña como «12».
  - Volver a ella deja la revisión sin pantalla y sus cuadres en el historial.
- **Reversibilidad:** no hay migración que revertir. Un cuadre no se borra, porque es historial. Si después aparece otra diferencia, se revisa y se cuadra otra vez.
