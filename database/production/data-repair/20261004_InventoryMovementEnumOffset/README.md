# Movimientos de inventario con la numeración desfasada

Procedimiento para revisar y compensar los movimientos manuales que la pantalla de inventario registró con el tipo y el motivo equivocados. `verify.sql` es de solo lectura; las correcciones se hacen con movimientos nuevos desde la aplicación, nunca editando la base.

## Qué pasó

Desde que existe la pantalla (commit `029f205`, 2026-02-16) hasta la corrección, `client/src/types/index.ts` numeraba `InventoryMovementType` e `InventoryMovementReason` desde 0, mientras el dominio, la API y la base empiezan en 1 (`Other = 99`). La API recibe y guarda el número tal cual, así que cada movimiento de "Registrar movimiento" se guardó con el tipo y el motivo siguientes a los elegidos:

| Elegido en pantalla | Guardado | Efecto aplicado | Efecto elegido |
| --- | --- | --- | --- |
| Entrada | rechazado (0) | — | — |
| Salida | `Entry` (1) | suma | resta |
| Ajuste (+) | `Exit` (2) | resta | suma |
| Ajuste (-) | `AdjustmentIncrease` (3) | suma | resta |
| Transferencia entrada | `AdjustmentDecrease` (4) | resta | suma |
| Transferencia salida | `TransferIn` (5) | suma | resta |
| Devolucion produccion | `TransferOut` (6) | resta | suma |
| Devolucion proveedor | `ReturnFromProduction` (7) | suma | resta |

Con los motivos pasó lo mismo: "Compra" (0) se rechazaba, "Venta" se guardó como `Purchase` (1), "Produccion" como `Sale` (2) y así hasta "Otro", que quedó como `ManualAdjustment` (10).

Consecuencias:

- Ninguna "Entrada" ni ningún motivo "Compra" se pudo registrar desde la pantalla: la API respondía 400 con el mensaje de FluentValidation.
- Cada movimiento registrado movió las existencias en sentido contrario al elegido; el error de cada línea es el doble de su cantidad.
- Algunas operaciones fallaron con "No hay stock suficiente para realizar la salida." aunque el usuario quería sumar (por ejemplo, una "Transferencia entrada" en una sucursal vacía). Eso pudo llevar al negocio a usar otra opción hasta lograr el efecto que buscaba.
- Los movimientos del sistema (recepciones de compra, consumos de ventas y de piezas de servicio, y sus reversos) se guardaron bien; la pantalla solo los mostraba con otra etiqueta (una recepción aparecía como "Salida / Venta").
- El reporte operativo no cambia: su costo de ventas solo suma los consumos del sistema (`ORDER:...`).
- El costo promedio pudo moverse; ver [Costo promedio](#costo-promedio).

## Reglas

- No se usa `UPDATE` ni `DELETE` sobre `InventoryMovements`, `InventoryMovementLines`, `InventoryStocks` ni `SupplyItems`: las correcciones de inventario compensan el historial ([AGENTS.md](../../../../AGENTS.md)).
- Las compensaciones se registran **después** de desplegar la corrección; con la pantalla anterior se volverían a desfasar.
- Las registra un usuario del propio negocio con permiso de inventario, o alguien con su autorización explícita, desde la pantalla corregida o con `POST /api/v1/inventory/movements`.
- `verify.sql` solo lee. Su consulta 1 recorre todos los tenants, pero solo devuelve conteos; el detalle exige `@TenantId`. Las consultas 2 a 4 muestran usuarios, notas y contrapartes del negocio: no las copies a tickets ni a PRs.

## Paso 1. Dimensionar

1. Toma la hora UTC desde la que **todo** lo desplegado incluye la corrección. Sale de `gh run list --workflow deploy-azure-webapp.yml`. Hasta el 4 de octubre de 2026 `develop` y `master` desplegaban la misma app: si después se desplegó una rama sin la corrección, la ventana sigue abierta hasta que se volvió a desplegar con ella.
2. En `verify.sql`, asigna esa hora a `@CorreccionDesplegadaUtc` y deja `@TenantId = NULL`.
3. Ejecuta `sqlcmd -S <servidor> -d <base> <autenticación> -W -s "|" -f o:65001 -i verify.sql`. La consulta 1 lista los tenants con movimientos sospechosos.

## Paso 2. Revisar cada movimiento con el negocio

Asigna `@TenantId` y vuelve a ejecutar el script. La consulta 2 trae una fila por línea:

- `[TipoElegido]` / `[MotivoElegido]`: lo que el usuario eligió en la lista.
- `[TipoGuardado]` / `[MotivoGuardado]`: lo que aplicó el sistema, y lo que la pantalla corregida mostrará para ese folio.
- `[EfectoAplicado]`, `[EfectoElegido]` y `[CorreccionNeta]`: los cambios de existencia de cada caso y su diferencia.

Decide cada folio con el negocio:

- **Corregir.** Lo elegido era la intención; por ejemplo, una "Salida" por merma que sumó existencias.
- **Dejar como está.** El efecto aplicado era lo que el negocio necesitaba. Pasa cuando, ante el rechazo de "Entrada", cargaron existencias con otra opción, como un "Ajuste (-) / Carga inicial" que sumó. Una `[ExistenciaProyectada]` negativa en la consulta 3 indica que lo elegido no pudo ser la intención.
- **Ya corregido.** El negocio registró después un movimiento que lo deshizo; revisa el kardex del insumo en esa ubicación.

Anota folio, decisión, quién la confirmó y los folios de compensación en el registro del tenant.

## Paso 3. Elegir cómo compensar

- **Opción A, por movimiento (paso 4):** cuando el negocio confirma la intención de cada folio.
- **Opción B, conteo físico:** cuando no se puede confirmar, son muchos movimientos o ya hubo un conteo después. Cuenta los insumos y ubicaciones de la consulta 3 y registra la diferencia con "Ajuste (+)" o "Ajuste (-)", motivo "Conteo fisico" y referencia externa `CORR:CONTEO:<aaaammdd>:<n>`. Desde ese momento manda el conteo y `[CorreccionPendiente]` deja de aplicar a esos insumos.

## Paso 4. Opción A: reverso y recaptura

Por cada folio a corregir se registran dos movimientos, en este orden, con el insumo, la ubicación y la cantidad de cada línea, y el `[CostoUnitario]` original:

| | Reverso | Recaptura |
| --- | --- | --- |
| Tipo | "Ajuste (-)" si `[EfectoAplicado]` es positivo; "Ajuste (+)" si es negativo | `[TipoElegido]` |
| Motivo | "Ajuste manual" | `[MotivoElegido]` |
| Referencia externa | `CORR:<Folio>:R` | `CORR:<Folio>:I` |
| Notas | `Reverso de <Folio>, registrado con la numeración desfasada` | `Recaptura de <Folio>` |

- Un folio con varias líneas se compensa con un reverso y una recaptura que llevan esas mismas líneas.
- Deja la fecha actual: el sistema calcula la existencia antes y después al registrar, y fechar en el pasado desordenaría esos saldos en el kardex.
- La referencia externa es única por tenant: si se intenta registrar dos veces, la API responde "Ya existe un movimiento con esa referencia externa." y no cambia nada.
- Si la API responde "No hay stock suficiente para realizar la salida.", detente con ese insumo y ubicación y pasa a la opción B.
- Ninguno de los dos cambia el costo promedio: el reverso es un ajuste y la recaptura nunca es "Entrada", porque la pantalla anterior no podía enviarla.
- Si la recaptura es una "Transferencia salida" y la mercancía sí llegó a otra sucursal, registra también ahí la "Transferencia entrada". Probablemente falló con la pantalla anterior, y es un movimiento nuevo, no una compensación.

## Paso 5. Verificar

Ejecuta de nuevo `verify.sql` con el mismo `@TenantId`:

- en la consulta 1, `[Compensados]` cuenta los folios con reverso y recaptura;
- en la consulta 2, `[Reverso]` y `[Recaptura]` muestran sus folios;
- en la consulta 3, los insumos corregidos quedan con `[CorreccionPendiente]` = 0 y `[DiferenciaKardex]` = 0.

Los folios que se decidió dejar como están siguen como pendientes; su decisión queda en el registro del tenant.

## Costo promedio

La columna `[CostoPromedio]` de la consulta 2 y la consulta 4 señalan los insumos afectados:

- **"Sí".** El folio se guardó como `Entry` con costo (una "Salida" en pantalla), y su costo se promedió como si fuera una compra.
- **"Solo si se registró antes del 2026-10-01".** Hasta el commit `6b488a3` (PR #9, 2026-10-01), cualquier línea que sumaba existencias con costo movía el promedio. Desde entonces solo lo mueven las entradas.

Las compensaciones no revierten el costo promedio. El dominio no tiene una operación para revaluarlo, y editar `SupplyItems.AverageCost` con SQL lo dejaría sin rastro (además, es token de concurrencia). Con los valores de la consulta 4 se evalúa la magnitud. Las recepciones de compra posteriores diluyen la desviación; la revaluación quedó como deuda en [deuda técnica](../../../../docs/roadmap/technical-debt.md). `LastCost`, del insumo y de la existencia, puede mostrar el costo del folio desfasado hasta el siguiente movimiento con costo.

## Qué ve el negocio al desplegar

Los movimientos manuales anteriores se mostrarán con el tipo y el motivo que aplicó el sistema: una antigua "Salida / Merma" aparecerá como "Entrada / Consumo". Eso coincide con el kardex y las existencias; conviene avisar a los tenants afectados antes del despliegue. Los movimientos del sistema se verán con su etiqueta correcta ("Entrada / Compra" para una recepción).

## Cómo se validó

El 2026-10-04 se ejecutó contra una base desechable de SQL Server LocalDB, con la API local y sin tocar producción:

1. Con la pantalla anterior se reprodujo el desfase. "Entrada / Compra" se rechazó, "Transferencia entrada" falló por stock, y cinco movimientos quedaron desfasados. Una "Salida" de 3 sumó existencias y movió el costo promedio de 18 a 18.7059.
2. `verify.sql` clasificó esos cinco y excluyó la recepción de compra y la venta. Proyectó la existencia de Harina en 43, igual al cálculo manual con lo elegido. Para el "Ajuste (-) / Carga inicial" de Azúcar proyectó un negativo.
3. Con la pantalla corregida se registraron "Entrada / Compra", "Salida", "Ajuste (+)", "Ajuste (-)" y una transferencia en dos sucursales; cada uno cambió la existencia en su sentido.
4. Se compensaron cuatro folios: un reverso desde la pantalla y los otros siete movimientos con la API. Un reverso repetido se rechazó. La consulta 3 quedó en `[CorreccionPendiente]` = 0, con existencia igual a la proyectada y `[DiferenciaKardex]` = 0, y el costo promedio no cambió con las compensaciones.
