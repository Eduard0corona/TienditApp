# ADR 0086: Un nombre por categoría en cada negocio

- Estado: Aceptada en la Fase 31E (2026-11-10).
- Fecha: 2026-11-10
- Continúa: la Fase 30P ([estrategia de pruebas](../development/testing-strategy.md)), que cerró en Categorías las dos maneras de crear dos veces desde la pantalla y dejó anotado que el servidor no rechazaba nombres repetidos.

## Contexto

- `CreateCategoryCommand` solo revisaba el tope de 300 categorías por negocio, y `UpdateCategoryCommand` no revisaba el nombre. `CategoryConfiguration` solo tenía el índice `TenantId`.
- La instalación de plantillas ya omitía los nombres que el negocio tenía, comparándolos sin mayúsculas ni espacios repetidos. El catálogo de ejemplo los reutilizaba, pero con otra comparación (solo las orillas), y fallaba si el negocio tenía dos que daban lo mismo.
- Después de la 30P, dos personas o dos pestañas todavía podían crear el mismo nombre.

El dueño del producto eligió la regla: un nombre por negocio, sin importar mayúsculas ni espacios, contando las archivadas; los acentos sí cuentan. Una categoría repetida de antes se revisa solo cuando cambia de nombre. Pidió también el índice único en la base, en esta misma fase.

## Decisión

1. **La regla vive en Application.** Crear o editar con un nombre que el negocio ya tiene responde `Result.Failure`, que el controller convierte en 400 con el mensaje en `detail`:
   - «Ya tienes una categoría llamada «Bebidas». Ponle otro nombre.»
   - «Ya tienes una categoría archivada llamada «Temporada». Restáurala o ponle otro nombre.»

   El mensaje lleva el nombre como está guardado, aunque se haya escrito con otras mayúsculas.
2. **Cuándo dos nombres son el mismo:** `Category.NameKey` (Domain) quita los espacios de las orillas, deja uno donde había varios (también tabuladores, saltos de línea y espacios duros) y pasa a minúsculas. «Café» y «Cafe» son dos. Los handlers comparan esa clave en memoria con los nombres del negocio, que son 300 como máximo, así que el resultado no depende de la intercalación de la base. La instalación de plantillas y el catálogo de ejemplo usan la misma clave. El catálogo de ejemplo ya no falla con dos repetidas de antes: reutiliza la activa.
3. **Editar:** solo se revisa un nombre nuevo, es decir, una clave distinta de la que la categoría ya tenía. Cambiarle las mayúsculas o los espacios a su propio nombre, o editar la descripción o el orden de una repetida de antes, funciona.
4. **El nombre se guarda como se escribió.** La regla no cambia el valor de `Name`.
5. **El índice único `IX_Categories_TenantId_Name` (`TenantId`, `Name`) respalda la regla** cuando dos guardados se cruzan. Compara con la intercalación de la columna (`SQL_Latin1_General_CP1_CI_AS`): no distingue mayúsculas, espacios al final ni el ancho de las letras («Ｂebidas» es «Bebidas»), toma «ß» por «ss» y sí distingue acentos.
   - Lo que solo la intercalación tiene por igual (el ancho, «ß») también se rechaza con el nombre de la categoría que lo tiene: después de comparar la clave, la revisión pregunta a la base si tiene ese nombre, con la misma comparación que el índice. La plantilla omite esos nombres como los demás que el negocio tiene. *Lo encontró la revisión de la PR: antes, el índice rechazaba el guardado sin que la aplicación supiera por qué, y respondía 500.*
   - Crear, editar e instalar una plantilla atrapan el `DbUpdateException`, deshacen lo suyo y vuelven a revisar. Si otra categoría ya tiene el nombre, responden el mismo mensaje (la plantilla, que se instale otra vez); si no, el error sube.
   - El índice no ve iguales los nombres que solo difieren en espacios al inicio o repetidos. Esos los revisa la aplicación, salvo en un guardado que se cruza con otro al mismo tiempo.
6. **La migración `AddUniqueCategoryNamePerTenant`:**
   - Si un negocio tiene dos categorías iguales para esa intercalación, se detiene con el error 50057 sin cambiar nada.
   - Bloquea `Categories` hasta crear el índice, en la misma transacción, para que la versión desplegada no repita un nombre entre la revisión y el índice.
   - La consulta 3 de su `verify.sql` lista las repetidas. Se renombran desde la app o con `data-repair/20261110_RepeatedCategoryNames`: la activa, o la más antigua, conserva el nombre, y las demás reciben « (2)», « (3)»...
   - No agrega columnas ni cambia datos.

## Alternativas descartadas

- **Una columna normalizada (calculada o escrita por el dominio) con índice único.** Haría que la base aplicara la regla completa. Pero la columna calculada repetiría `NameKey` en SQL, que tendría que coincidir con .NET carácter por carácter y no correría en las pruebas con SQLite ni InMemory. Y una columna escrita por el dominio quedaría vacía o vieja en lo que guarde la versión desplegada mientras se publica. El índice sobre `Name` cubre el caso que se veía (el mismo nombre escrito dos veces) sin esos riesgos.
- **Guardar el nombre limpio** (sin espacios en las orillas ni repetidos). Haría que el índice viera más casos iguales. Cambia lo que el negocio escribió y no hacía falta para la regla. Queda fuera de esta fase.
- **Renombrar las repetidas dentro de la migración**, como hizo `CashSessionSingleOpenAndRevision` con las cajas abiertas. Se descartó porque cambia lo que ven los clientes en el menú sin que nadie lo revise: la migración se detiene, y la reparación se corre aparte, después de avisar a los negocios.

## Consecuencias

- **Lo que logra:** dos personas o dos pestañas ya no dejan al negocio con dos categorías iguales. La segunda recibe un 400 que dice cuál lo tiene, también si las dos guardan al mismo tiempo.
- **Lo que limita:**
  - Si producción tiene repetidas, el despliegue espera a que se renombren (consulta 3 de `verify.sql`).
  - Las repetidas de antes que solo difieren en espacios se quedan. La consulta 4 de `verify.sql` las lista para avisar al negocio.
  - Entre el `apply.sql` y el merge, la versión desplegada responde 500, no 400, a quien repita un nombre: no atrapa el error del índice.
  - Una plantilla que se cruza con otro guardado no instala nada y pide instalarse otra vez. El catálogo de ejemplo, que solo corre con el catálogo vacío, no atrapa ese cruce.
- **Decisión por omisión** (sin preferencia expresa del dueño): los textos; nombrar la categoría guardada en el mensaje; qué categoría conserva el nombre en la reparación y el sufijo « (n)»; detener la migración en lugar de renombrar; reutilizar la activa en el catálogo de ejemplo.
- **Reversibilidad:** `rollback.sql` borra el índice sin tocar ninguna fila, y la base vuelve a aceptar repetidas. Volver al código anterior funciona sin tocar la base. Los nombres que cambió `repair.sql` no regresan solos: su salida los lista.
