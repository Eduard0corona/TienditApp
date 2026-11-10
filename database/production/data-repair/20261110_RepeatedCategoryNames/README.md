# Categorías con el mismo nombre en un negocio

Procedimiento para revisar y, si hace falta, renombrar las categorías que un negocio tiene repetidas antes de aplicar `20261110022936_AddUniqueCategoryNamePerTenant` (Fase 31C). `verify.sql` solo lee; `repair.sql` solo cambia `Categories.Name` (y `UpdatedAt`/`UpdatedBy`) de las repetidas: no borra categorías ni mueve productos.

## Qué pasó

Hasta la Fase 31C el servidor aceptaba dos categorías con el mismo nombre en un negocio: `CreateCategoryCommand` solo revisaba el tope de 300 y `UpdateCategoryCommand` no revisaba el nombre. En el cliente, la Fase 30P cerró las dos maneras de crear dos veces desde la pantalla, pero dos personas o dos pestañas podían hacerlo.

Desde la Fase 31C:

- crear o editar con un nombre que el negocio ya tiene, sin importar mayúsculas ni espacios y contando las archivadas, responde 400 con el nombre de la que lo tiene (`Category.NameKey`);
- el índice único `IX_Categories_TenantId_Name` respalda esa regla cuando dos guardados se cruzan. Compara con la intercalación de la columna (`SQL_Latin1_General_CP1_CI_AS`: sin mayúsculas, espacios al final ni el ancho de las letras, «ß» como «ss», con acentos).

La migración no puede crear el índice mientras un negocio tenga dos categorías iguales para esa intercalación: se detiene con el error 50057 sin cambiar nada.

## Reglas

- Primero se intenta que el negocio decida: renombrar desde Catálogo › Categorías funciona aunque la categoría esté repetida (la aplicación solo revisa un nombre nuevo). `repair.sql` es para lo que quede.
- `repair.sql` conserva el nombre de la activa y, entre iguales, de la más antigua. Las demás reciben « (2)», « (3)»... con el primer número libre en su negocio, recortando el final del nombre si no cabe en 150 caracteres. El nombre nuevo se ve en el menú en línea si la categoría está activa.
- No toca los nombres que solo difieren en espacios al inicio o repetidos (la consulta 4 del `verify.sql` de la migración): el índice no los ve iguales y la aplicación los tolera.
- La salida lista cada nombre anterior y el nuevo: guárdala con el registro del cambio.
- Ambos scripts traen el bloque `SET` de los `apply.sql`, como las demás reparaciones.

## Paso 1. Dimensionar

```
sqlcmd -S <servidor> -d <base> <autenticación> -W -s "|" -f o:65001 -i verify.sql
```

- **Consulta 1.** Cuántos grupos hay y cuántas categorías renombraría `repair.sql`.
- **Consulta 2.** Cada categoría repetida con su negocio, si está activa, sus productos, cuándo se creó y su `[Lugar]`: la 1 conserva su nombre; las demás se numeran en ese orden.

Si la consulta 1 dice 0 grupos, no hay nada que hacer: sigue con el `apply.sql` de la migración.

## Paso 2. Avisar

Avisa a cada negocio de la consulta 2 qué categorías tiene repetidas y que puede renombrarlas desde Catálogo › Categorías antes de la fecha del despliegue. Lo que no renombre queda con el nombre de `repair.sql`, que puede cambiar después.

## Paso 3. Reparar

Con un respaldo de la base, justo antes del `apply.sql` de la migración:

```
sqlcmd -S <servidor> -d <base> <autenticación> -b -W -s "|" -f o:65001 -i repair.sql -o repair-salida.txt
```

- Corre en una transacción y bloquea `Categories` hasta terminar (milisegundos): nada que se guarde mientras tanto repite un nombre.
- Lista cada categoría que cambia (`[Negocio]`, `[Id]`, `[Antes]`, `[Ahora]`).
- Deja `UpdatedBy = 'data-repair 20261110_RepeatedCategoryNames'` y `UpdatedAt` con la hora de la reparación.
- Es idempotente: una segunda ejecución no lista nada.

Entre este paso y el `apply.sql`, la versión desplegada todavía puede crear una repetida. Si el `apply.sql` se detiene con el 50057, repite los pasos 1 y 3.

## Paso 4. Verificar y aplicar

Ejecuta de nuevo `verify.sql`: la consulta 1 dice 0 grupos. Después sigue el orden de la migración: su `verify.sql` (la consulta 3 vacía), su `apply.sql` y su `verify.sql` otra vez.

## Cómo se validó

El 2026-11-10 se ejecutó contra una base desechable en SQL Server 2022 (contenedor), creada con `dotnet ef database update` en `20261108150147_AddOrderCancellationEmailsAndAnswerReminder`, con el `sqlcmd` clásico y `-b`. No se tocó producción.

1. **Datos.** Once categorías de dos negocios: «Bebidas» activa, «bebidas » archivada y más antigua, «BEBIDAS» activa y más nueva y «Bebidas (2)» ya existente; «Postres» y « postres»; dos nombres de 150 caracteres que solo difieren en mayúsculas; «Café» y «Cafe»; y «Bebidas» en el otro negocio.
2. **Antes.** El `verify.sql` de la migración listó en la consulta 3 los dos grupos que bloquean (las tres «Bebidas» y los dos nombres largos), en la 4 «Postres» y « postres», y no listó «Café» ni «Cafe». Su `apply.sql` se detuvo con el error 50057 (código de salida 1) sin cambiar el historial, los índices ni las filas.
3. **Reparación.** `verify.sql` contó 2 grupos y 3 por renombrar. `repair.sql` dejó «Bebidas» como estaba, renombró «BEBIDAS» a «BEBIDAS (3)» (la «(2)» ya existía), «bebidas » a «bebidas (4)» y el nombre largo a 146 caracteres más « (2)», y los listó. No tocó «Postres», « postres», «Café», «Cafe» ni el otro negocio. Una segunda ejecución no listó nada.
4. **Después.** El `apply.sql` creó el índice; repetirlo con la caché de planes vacía no cambió nada. El índice rechazó «postres  » junto a «Postres» (error 2601).
