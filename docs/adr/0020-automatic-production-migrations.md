# ADR 0020: Migraciones de producción en el despliegue, ensayadas en una copia

- Estado: Aceptada
- Fecha: 2026-10-14

## Contexto

Producción no migra al arrancar (`Program.cs` solo lo hace en Development con una base local). Hasta ahora, cada migración dejaba en `database/production/<MigrationId>/` un `apply.sql`, la salida de `dotnet ef migrations script --idempotent` con un bloque `SET`, y alguien lo ejecutaba a mano con `sqlcmd` antes del merge, porque cada push a `master` despliega.

Ese paso manual tenía costos:

- olvidarlo dejaba publicada una versión que fallaba con `Invalid column name`; sin el slot, la prueba de humo solo lo avisaba después;
- cada `apply.sql` repite el script entero (unas 6,000 líneas), y nadie comprobaba que el archivo siguiera igual a su migración;
- la base recibía el esquema de una PR antes de que se fusionara;
- el ensayo con datos se hacía a mano, en bases preparadas para cada fase.

## Decisión

1. **Producción sigue sin migrar al arrancar.** La aplicación no tiene permisos de DDL, y con el slot de staging la versión del slot migraría producción antes de la aprobación del swap.
2. **El despliegue migra, con `dotnet ef database update`.** `.github/scripts/apply-migrations.sh` construye la API en Release como Production (con una llave JWT aleatoria que nada usa) y aplica las migraciones que falten. Es el mismo camino que `MigrateAsync`: cada migración en su transacción y con el candado de migraciones de EF Core. No usa `apply.sql` ni `sqlcmd`.
3. **Cada release se ensaya en una copia de producción.** El slot de staging tiene su propia base (una cadena de conexión marcada como *Deployment slot setting*). En cada push a `master`, el job `build-and-deploy`:
   1. borra la base de staging;
   2. copia en ella la de producción en el mismo servidor;
   3. le aplica las migraciones;
   4. despliega al slot y lo prueba sobre esa copia.
   Una migración que falla con los datos de hoy se detiene ahí, sin tocar producción.
4. **Producción se migra después de la aprobación y antes del swap.** El job `promote` corre en el environment `production`: con su aprobación, migra producción mientras sirve la versión anterior y después hace el swap. La regla de expandir y contraer no cambia: cada migración debe dejar funcionando la versión anterior.
5. **La identidad de despliegue firma con Entra, sin secretos.** Es la misma de OIDC:
   - en la base de producción es un usuario con `db_ddladmin`, `db_datareader` y `db_datawriter`; la copia lo hereda;
   - en el servidor tiene *SQL Server Contributor*, para la copia y una regla de firewall temporal para el runner;
   - un candado de borrado protege la base de producción.
6. **Se activa con variables.** Sin `AZURE_SQL_SERVER` todo sigue como antes, con `apply.sql` a mano. Activarlo exige el slot: producción nunca se migra sin ensayo.

## Consecuencias

- **Lo que logra:**
  - Ya no hay pasos a mano en producción, y un merge con migraciones ya no espera a nadie.
  - Cada release se ensaya con los datos reales del día, lo que el CI no hace (sus bases están vacías).
  - El resumen de la ejecución dice:
    - qué migraciones se aplicaron;
    - a qué hora se copió producción;
    - en qué dirección probar el slot;
    - a qué hora empezó a migrarse producción, para una restauración a un momento dado.
  - Las migraciones nuevas ya no necesitan `apply.sql`.
- **Lo que cuesta:**
  - Una segunda base de Azure SQL y unos minutos de copia por release.
  - La identidad de despliegue gana permisos sobre la base y el servidor.
  - La aprobación de `production` es un control de proceso, no una frontera de seguridad: el job sin aprobación usa la misma identidad (como ya pasaba con el swap).
- **Riesgos:**
  - La copia tiene los datos personales y las credenciales de WhatsApp de cada negocio. El slot no debe procesar colas (`BackgroundJobs__Enabled=false`), ni nadie debe mandar mensajes desde él.
  - Entre la copia y la aprobación, producción sigue cambiando: una migración que pasó en la copia aún puede fallar en producción. Si falla, no hay swap; las migraciones anteriores se quedan y la versión anterior sigue funcionando sobre ellas.
  - Si una migración falla, el mensaje de SQL Server queda en el log del job, y puede incluir un valor (por ejemplo, una llave duplicada).
- **Reversibilidad:**
  - Borrar `AZURE_SQL_SERVER` vuelve al proceso manual sin cambiar nada más.
  - Volver a la versión anterior con un swap no revierte la base, como antes.
  - Para revertir una migración, su `rollback.sql` (si lo tiene) o una restauración a un momento dado.

## Alternativas descartadas

- **Migrar al arrancar en producción:** la aplicación necesitaría permisos de DDL, y la versión del slot migraría producción antes de la aprobación.
- **Compartir la base de producción con el slot:** el slot solo probaría el código. La migración llegaría a producción sin ensayo con datos, aunque fuera con aprobación.
- **Una base de staging permanente con datos de prueba:** es más barata, pero el ensayo no ve los datos que hacen fallar una migración.
- **Ejecutar el `apply.sql` versionado desde el workflow:** conserva la duplicación y necesita `sqlcmd` con Entra en el runner. El script idempotente compila cada migración como un lote; `dotnet ef database update` ejecuta sentencia por sentencia y también crea una base vacía.
