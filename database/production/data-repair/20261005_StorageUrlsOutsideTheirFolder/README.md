# Imágenes con una dirección fuera de la carpeta de su dueño

Procedimiento para revisar y limpiar las fotos de producto cuya dirección guardada apunta a la carpeta de otro negocio o sale de la propia con `..`. `verify.sql` solo lee; `repair.sql` solo cambia `Products.ImageUrl` de esas filas y no borra archivos.

## Qué pasó

Hasta el commit `6ece570` (PR #11, 2026-10-01), `POST /api/v1/products` aceptaba `ImageUrl` sin validarla. Un usuario con permiso de catálogo podía crear un producto cuya foto fuera la dirección de un archivo de otro negocio (las direcciones se ven en el menú público), incluso con `../` o caracteres codificados. Al reemplazar la foto o borrar el producto, el almacenamiento de entonces borraba esos archivos.

Desde ese commit:

- la foto solo se fija subiéndola (`POST /api/v1/products/{id}/image`);
- el borrado se limita a la carpeta del dueño (`products/{negocio}`);
- desde la Fase 9 también se validan la carpeta y el nombre al subir.

Las filas creadas antes siguen ahí. Muestran la foto de otro negocio en el catálogo y en el menú público, y se rompen cuando ese negocio la cambia. La versión actual ya no borra nada fuera de la carpeta del dueño, así que no ponen en riesgo los archivos del otro negocio: la limpieza es para que cada catálogo muestre solo sus imágenes.

Logos y avatares siempre recibieron su dirección del servidor al subirse; ninguna versión de la API la aceptó del cliente. `verify.sql` los cuenta para confirmarlo.

Los avatares subidos desde la Fase 9 van a `avatars/users/{cuenta}`: la persona es la misma en todos sus negocios. Los anteriores se quedan donde están: en `avatars/{negocio}`, o en `avatars/00000000-0000-0000-0000-000000000000` si la sesión no tenía negocio. Se borran de ahí cuando la persona los cambia o los quita, así que no hay nada que migrar.

## Reglas

- Se puede correr antes o después de desplegar la Fase 9: desde el 2026-10-01 ninguna versión crea direcciones así.
- `repair.sql` no borra archivos: son del negocio en cuya carpeta están.
- La salida de `repair.sql` lista la dirección anterior de cada producto. Guárdala con el registro del cambio y no la pegues en PRs ni en tickets. Restaurar una dirección vuelve a mostrar el archivo del otro negocio: no lo hagas sin revisarlo.
- Ambos scripts traen el bloque `SET` de los `apply.sql`. El `UPDATE` sobre `Products` lo necesita por sus índices filtrados: el `sqlcmd` clásico arranca con `QUOTED_IDENTIFIER OFF` y el `UPDATE` fallaría (Msg 1934).

## Paso 1. Dimensionar

```
sqlcmd -S <servidor> -d <base> <autenticación> -W -s "|" -f o:65001 -i verify.sql
```

`verify.sql` clasifica cada dirección como la interpreta un navegador: en minúsculas, con `\` como `/` y con `%2e`, `%2f` y `%5c` decodificados.

| Clase | Qué es | `repair.sql` |
| --- | --- | --- |
| `Propia` | Dentro de la carpeta de su dueño. Para avatares, también la de un negocio de la cuenta o la de sesión sin negocio. | No la toca |
| `RutaInvalida` | Con un segmento `..`: sale de la carpeta donde parece estar. | Deja el producto sin foto |
| `OtroNegocio` | Dentro de la carpeta de otro negocio. | Deja el producto sin foto |
| `Externa` | Cualquier otra: otro sitio u otra forma de dirección. | No la toca |

- **Consulta 1.** Conteo por tipo, clase y negocio. Los avatares no llevan negocio porque son de la cuenta.
- **Consulta 2.** Los productos que `repair.sql` dejaría sin foto. `[CarpetaDe]` es el negocio a cuya carpeta apuntan.

Si la consulta 2 sale vacía, no hay nada que reparar.

Si un logo o un avatar aparece como `RutaInvalida` u `OtroNegocio`, la API no lo escribió así y `repair.sql` no lo toca. Hay una excepción: un avatar `OtroNegocio` puede ser de un `SUPER_ADMIN` que subió su foto mientras actuaba en otro negocio, y no requiere acción. Cualquier otro caso se investiga como incidente.

## Paso 2. Avisar

Avisa a cada negocio de la consulta 2 que esos productos quedarán sin foto y que puede subir la suya desde Catálogo. Si conviene avisar al negocio de `[CarpetaDe]` que otro usó sus imágenes, se decide con soporte.

## Paso 3. Reparar

Con un respaldo de la base:

```
sqlcmd -S <servidor> -d <base> <autenticación> -b -W -s "|" -f o:65001 -i repair.sql -o repair-salida.txt
```

- Corre en una transacción y aplica la misma clasificación que `verify.sql`.
- Lista cada producto que cambia (`[Negocio]`, `[Producto]`, `[DireccionAnterior]`) y el total.
- Deja `UpdatedBy = 'data-repair 20261005_StorageUrlsOutsideTheirFolder'` y `UpdatedAt` con la hora UTC de la reparación.
- Es idempotente: una segunda ejecución reporta 0.
- Un navegador con el catálogo ya cargado sigue mostrando la foto anterior hasta recargarlo.

## Paso 4. Verificar

Ejecuta de nuevo `verify.sql`. La consulta 2 sale vacía y la consulta 1 ya no tiene productos `RutaInvalida` ni `OtroNegocio`.

## Productos `Externa`

La aplicación arma las variantes agregando `_md.webp` y los demás sufijos a la dirección guardada. Por eso una dirección externa probablemente se ve rota. `repair.sql` no la toca: el negocio puede subir una foto o revisarla con soporte.

## Cómo se validó

El 2026-10-05 se ejecutó contra una copia desechable con el esquema de la Fase 8 en SQL Server 2022 (contenedor). Se usó `sqlcmd -i`, sin `-I`, como se corre en producción. No se tocó producción.

1. **Datos.** Se prepararon 16 productos de dos negocios:
   - propias, locales y de blob, incluida una con el GUID en mayúsculas y otra en una subcarpeta con el id del otro negocio;
   - tres a la carpeta del otro negocio: local, blob y con `%2F`;
   - cuatro con `..`: `../`, `%2E%2E`, `\..\` y `..` al final;
   - tres externas: `%20`, `..` dentro de un nombre y Firebase con `%2F`;
   - una vacía y una nula.

   También se prepararon logos y avatares de cada clase.
2. **Clasificación.** `verify.sql` clasificó cada fila como se esperaba. La consulta 2 listó las 7 por reparar con su `[CarpetaDe]`.
3. **Reparación.** `repair.sql` dejó esas 7 sin foto y las listó con su dirección anterior. No tocó propias, externas, vacías, logos ni avatares. Una segunda ejecución cambió 0 filas.
4. **Bloque `SET`.** Sin el bloque, un `UPDATE` sobre `Products` falló con Msg 1934 (`QUOTED_IDENTIFIER`).
