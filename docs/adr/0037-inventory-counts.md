# ADR 0037: Conteo físico de inventario

- Estado: Aceptada
- Fecha: 2026-10-21

## Contexto

El inventario lleva un libro mayor: cada cambio de existencias es un movimiento con su folio y el saldo antes y después ([inventario y compras](../domain/inventory-and-purchasing.md)). Pero lo que dice el sistema y lo que hay en el anaquel se separan con el tiempo:

- una merma que nadie anotó;
- un error al capturar una compra;
- algo que se tomó sin venderse.

El roadmap pedía para Inventario «concurrencia robusta, transferencias atómicas y reconciliación», y el guardrail del dominio dice que las diferencias se reconcilian «mediante ajuste trazable». Hasta ahora la única forma de cuadrar era un ajuste manual, artículo por artículo y sin rastro del conteo que lo motivó.

El conteo físico es la reconciliación que usa un negocio de barrio: alguien cuenta lo que hay, el sistema compara y ajusta. Las transferencias atómicas entre ubicaciones quedan para la Fase 27B.

Tomé las decisiones de la Fase 27A con la autorización permanente del dueño del producto, sin esperar su respuesta a cada una.

## Decisión

1. **Un conteo es de una ubicación** (`InventoryCount`):
   - tiene folio `CNT-yyyyMMdd-nnnn`, del contador del negocio por día UTC, como `INV` y `PO`;
   - tiene un estado: abierto (1), aplicado (2) o cancelado (3);
   - guarda la categoría, si se contó una, y las notas;
   - guarda quién lo empezó, lo aplicó o lo canceló, y cuándo;
   - guarda el motivo de la cancelación y los ajustes que generó.

   Una ubicación tiene un solo conteo abierto (índice único filtrado), porque dos conteos ajustarían dos veces la misma existencia. Un conteo tiene como máximo 2,000 artículos; uno más grande se divide por categoría.
2. **Qué se cuenta.** Al empezar se elige una de tres opciones:
   - los artículos activos con existencia registrada en la ubicación (también los que están en cero o en negativo);
   - solo los de una categoría;
   - los artículos que se elijan.

   Mientras está abierto se le agregan artículos, aunque no tengan existencia ahí: lo que apareció en el anaquel. Los productos que controlan sus existencias entran como cualquier artículo.
3. **Se cuenta a ciegas.** La hoja no muestra lo que dice el sistema, para que nadie copie el número. Al guardar una cantidad (`InventoryCountLine`) se anota lo que el sistema tenía de ese artículo en la ubicación en ese momento («sistema al contar»), quién y cuándo.
   - Escribir otra cantidad la reemplaza y vuelve a tomar el sistema. La misma cantidad solo cambia la nota.
   - Borrarla deja el artículo sin contar.
   - La cantidad va de 0 a 9,999,999, con 3 decimales como máximo y entera en piezas.
4. **La diferencia se mide contra lo que había al contar.** Diferencia = contado − sistema al contar.
   - Al aplicar, cada diferencia mueve la existencia que haya en ese momento, no la reemplaza. Así no se pierde lo vendido o recibido después de contar ese artículo. Por ejemplo, si se contaron 22 refrescos contra 24 y después se vendieron 3, la existencia queda en 19, no en 22.
   - Si un artículo quedaría en negativo, el conteo no se aplica y el mensaje lo nombra para que se vuelva a contar.
5. **Aplicar** (`PostInventoryCountCommand`) genera hasta dos movimientos de motivo «Conteo físico» (`InventoryCount`, 7):
   - un `AdjustmentIncrease` con lo que sobró y un `AdjustmentDecrease` con lo que faltó;
   - cada uno con su folio `INV`, la referencia del folio del conteo y una nota por artículo («Contado 22; sistema al contar 24»).

   Se valoran al costo promedio, que no cambia, como cualquier ajuste del libro mayor. Lo que no se contó se queda como está, y hace falta al menos un artículo contado. Sin diferencias, el conteo se aplica sin movimientos.

   Aplicado, el conteo ya no cambia. Queda en la actividad (`InventoryCountPosted`) con lo que valió.
6. **No se aplica dos veces.**
   - Cada ajuste lleva una referencia externa del conteo (`COUNT:{id}:IN` y `COUNT:{id}:OUT`), y el índice único de las referencias rechaza un segundo intento.
   - Los movimientos manuales no pueden usar el prefijo `COUNT:`, como ya no pueden usar `ORDER:` ni `PO:`.
7. **Cancelar.** Un conteo abierto se cancela con un motivo opcional y no mueve nada. Lo contado se queda en el conteo y la cancelación queda en la actividad (`InventoryCountCancelled`).
8. **Concurrencia.**
   - Un candado por ubicación (`IInventoryCountLock`, `sp_getapplock` en la transacción de la petición) pone en fila empezar, capturar, agregar, aplicar y cancelar. Una captura nunca se cuela en un conteo que se está aplicando, y una ubicación nunca abre dos conteos.
   - La revisión del conteo (`Revision`) sube con cada artículo agregado o capturado y es token de concurrencia. Aplicar exige la revisión que se revisó: si cambió, responde «El conteo cambió mientras lo revisabas».
   - Si una venta mueve la misma existencia mientras se aplica, choca con su token y el conteo se vuelve a intentar: aplicar es todo o nada.
9. **Permisos:** `ManageInventory` y la capacidad de inventario, como los movimientos. Cada artículo guarda quién lo contó y el conteo quién lo aplicó.
10. **Se ve** en Inventario › Conteos:
    - la lista, por estado, ubicación, folio o ubicación buscada;
    - «Nuevo conteo»;
    - la hoja: buscar o escanear un código lleva al artículo, cada cantidad se guarda al salir del campo y Enter pasa al siguiente;
    - «Revisar diferencias»: contados, con diferencia, sobrante y faltante; y por artículo lo que había al contar, lo contado, la diferencia, lo que hay ahora, en qué quedará y lo que vale;
    - «Aplicar conteo», después de preguntar;
    - de un conteo aplicado, sus ajustes, que abren el movimiento; de uno cancelado, su motivo.

    Los ajustes aparecen en «Entradas y salidas» y en el historial por insumo como cualquier movimiento.
11. **Lotes y caducidad:** no se cuenta por lote, porque las existencias no son por lote.
12. **Fuera de esta fase:**
    - que otra persona apruebe el conteo antes de aplicarlo;
    - el conteo por lote;
    - congelar las ventas mientras se cuenta;
    - contar sin conexión;
    - imprimir la hoja;
    - comparar el kardex con la existencia guardada;
    - las transferencias entre ubicaciones (Fase 27B).
13. **La migración** `20261021002749_AddInventoryCounts` agrega las tablas `InventoryCounts` e `InventoryCountLines`:
    - tienen llaves foráneas (sin borrado en cascada) a ubicaciones, artículos, conteos y movimientos;
    - `CK_InventoryCounts_Status` exige cuándo y quién según el estado, `CK_InventoryCounts_Lines` exige que los contados vayan de cero al número de artículos, y `CK_InventoryCountLines_Counted` exige que lo contado, nunca negativo, vaya con el sistema, cuándo y quién, o nada de eso;
    - sus índices únicos son el folio por negocio, un conteo abierto por ubicación (filtrado con `[Status] = 1`) y cada artículo una vez por conteo;
    - tiene además un índice para la lista y los de las llaves foráneas.

## Consecuencias

- **Lo que logra:**
  - El negocio cuadra lo que dice el sistema con lo que hay, sin cerrar la tienda.
  - Cada ajuste dice de qué conteo salió, quién contó cada artículo y contra qué.
  - Lo que se vende mientras se cuenta no se pierde.
  - Dos personas pueden contar el mismo conteo desde dos teléfonos.
- **Lo que limita:**
  - El sistema al contar se toma al guardar la cantidad. Lo contado en papel y capturado horas después, con ventas en medio, se mide contra el sistema de la captura: hay que capturarlo antes de vender otra vez.
  - Si dos personas cuentan el mismo artículo, la última cantidad guardada es la que vale.
  - No hay aprobación de otra persona: quien maneja el inventario aplica.
  - En una tienda abierta, aplicar puede chocar con una venta (409). Basta con volver a intentarlo.
- **Compatibilidad durante el despliegue:**
  - La versión anterior (26D) funciona con la base migrada: no conoce las tablas.
  - Los ajustes de un conteo son movimientos de inventario comunes (tipos 3 y 4, motivo 7) que la versión anterior sabe leer.
  - Mientras conviven las dos versiones, la anterior acepta una referencia externa `COUNT:` en un movimiento manual. El índice único la sigue protegiendo, porque la referencia de un conteo lleva su identificador.
- **Reversibilidad:**
  - `rollback.sql` borra las tablas solo si no hay ningún conteo: abierto, aplicado o cancelado.
  - Si hay alguno, se detiene con el error 50030 sin cambiar nada, y solo queda corregir hacia adelante. Los ajustes de los conteos aplicados se quedarían sin el conteo que los explica.
