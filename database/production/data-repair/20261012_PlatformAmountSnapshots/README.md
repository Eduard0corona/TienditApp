# Días guardados con lo que cobraron las plataformas en «Otros»

Procedimiento para revisar y volver a calcular los días del resumen diario (`DailySalesSnapshots`) que la versión anterior a la Fase 22F guardó con los pagos «Plataforma» dentro de `OtherAmount`. `verify.sql` es de solo lectura; `repair.sql` solo borra esos días, que son datos derivados, y el worker de snapshots los vuelve a calcular.

## Qué puede pasar

La Fase 22F agrega el método de pago «Plataforma» (5): lo que cobraron Rappi, Uber Eats o DiDi Food en su app. También agrega `DailySalesSnapshots.PlatformAmount`, donde el worker suma esos pagos. La versión anterior no conoce el método: su worker lo suma en `OtherAmount` y guarda `PlatformAmount` en 0, el valor por omisión de la columna.

Mientras se publica la versión nueva, las dos versiones corren sobre la misma base (swap del slot de staging). Si el swap cruza la medianoche del negocio y la instancia anterior guarda el día que acaba de cerrar, con pagos «Plataforma» ya registrados por la nueva, ese día queda con lo que cobraron las plataformas en «Otros». Las ventas, los cobros y la venta neta del día no cambian, solo en qué columna aparece ese dinero. Pero un día guardado ya no se recalcula solo.

Por eso `apply.sql` recomienda no publicar entre las 00:00 y las 00:45, hora del negocio. Si aun así pasa, se repara así.

## Reglas

- Solo se borran filas de `DailySalesSnapshots`: son datos derivados. Los pagos, los reembolsos y los pedidos no se tocan.
- Se borran **todas** las filas de cada día afectado (todas sus sucursales y tipos de pedido), no solo la que tiene «Otros»: un día con alguna fila guardada no se vuelve a calcular, y la fila que faltara dejaría de contar en los reportes.
- Borrar de más es inofensivo. Mientras el worker no guarde otra vez el día, los reportes lo calculan desde las ventas, y lo guarda en su siguiente pasada (`BackgroundJobs:SalesSnapshots:IntervalMinutes`).
- Córrelo solo con la versión nueva desplegada en todas las instancias: si la anterior sigue corriendo, puede volver a guardar el día igual que antes.
- Los dos scripts traen el bloque `SET` de los `apply.sql`.

## Paso 1. Dimensionar

```
sqlcmd -S <servidor> -d <base> <autenticación> -W -s "|" -f o:65001 -i verify.sql
```

La consulta es la 7 del `verify.sql` de `database/production/20261012005540_AddDeliveryPlatformOrders`. Lista los días de negocios con pagos «Plataforma» que tienen `PlatformAmount` en 0, `OtherAmount` mayor que 0 y se calcularon después del primer pago «Plataforma» del negocio. Un día con pagos del método «Otro» y sin pagos de plataformas también aparece: recalcularlo da lo mismo.

Si sale vacía, no hay nada que reparar.

## Paso 2. Reparar

```
sqlcmd -S <servidor> -d <base> <autenticación> -b -W -s "|" -f o:65001 -i repair.sql -o repair-salida.txt
```

`repair.sql` borra en una transacción todas las filas de los días que lista `verify.sql` y muestra cada fila borrada con sus cifras. Guarda la salida en el registro del cambio. Es idempotente: una segunda ejecución no borra nada.

## Paso 3. Confirmar

Después de la siguiente pasada del worker, vuelve a ejecutar `verify.sql`: debe salir vacío. En Reportes, las ventas por sucursal de esos días muestran lo que cobraron las plataformas en su propia columna.
