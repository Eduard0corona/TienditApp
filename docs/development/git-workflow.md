# Flujo Git

## Estrategia vigente

- `master`: la única rama permanente y la de producción. Cada push publica la API y la SPA (workflow `Deploy WebApp`, ver [despliegue](deploy-azure.md)) y espera a que la app responda con esa versión; una ejecución manual desde otra rama se omite.
- Ramas de trabajo cortas desde `master`, que vuelven a ella por Pull Request.
- Pull Requests pequeños, con una intención y validación reproducible. Antes de fusionar uno con migraciones, aplica su `apply.sql` en producción (ver [migraciones](database-migrations.md)).
- Preferencia por squash merge para historial funcional claro, si el equipo la aprueba.

`develop` se retiró el 4 de octubre de 2026: todo su contenido ya estaba en `master`. Mientras existió, también desplegaba a producción con su propio workflow, así que una rama vieja con un workflow de despliegue es un riesgo: bórrala en lugar de dejarla.

## Revisión automática con Claude

Cada Pull Request hacia `master` pasa, además de `CI`, por el workflow `Claude Code Review` (`.github/workflows/claude-code-review.yml`) al abrirse, al recibir commits, al reabrirse y al salir de borrador. Los PR hacia otra rama, en borrador, desde un fork o de Dependabot no se revisan.

- Cuatro revisores corren en paralelo, uno por área, cada uno en su job `review (<área>)` con Claude Opus (`--model claude-opus-5-5`): `domain-application` (`src/FoodParkDigital.Domain` y `Application`), `infrastructure-webapi` (`Infrastructure`, `WebApi` y `database`), `tests`, y `client-and-rest` (`client`, `docs`, `.github` y lo demás). Hasta el PR #111, una sola sesión con el modelo por omisión revisaba toda la fase (de 17 mil a 24 mil líneas, 15 mil de ellas generadas) en unos 50 pasos. Reportó 0/0/0 en once PR seguidos, y una revisión por áreas del mismo código del #111 encontró una conciliación mensual mal calculada y otros cinco problemas.
- Cada revisor lee el diff de su área sin archivos generados (`.claude-review/diff.patch`, desde la base común con `master`, como lo muestra GitHub) y puede leer cualquier archivo del head del PR como contexto. El prompt le pide buscar bugs, no confirmar el checklist: por cada función, consulta, componente o prueba que cambia, qué le puede llegar (vacíos, límites, duplicados, desorden, rangos que se traslapan, concurrencia, otro tenant, rutas de error) y si el resultado es correcto. Revisa contra el [`AGENTS.md`](../../AGENTS.md) de la raíz, los `AGENTS.md` de cada capa, los [guardrails](../ai/guardrails.md), el [checklist](../ai/review-checklist.md) y los ADR que el PR agrega. Deja un comentario en la línea de cada hallazgo nuevo, con su escenario, y no repite los que ya están en el PR.
- Cada revisor trabaja solo, sin subagentes (`--disallowedTools "Agent,Task"`): en el PR #97 lanzó subagentes en segundo plano y entregó el resumen antes de que terminara alguno. El paralelismo viene de los jobs, no de subagentes.
- El job `claude-review` (el nombre del check de siempre) junta los resultados en un solo comentario del PR: los conteos (BLOCKER, MAJOR, MINOR), una sección por área con sus hallazgos o, sin hallazgos, los casos de más riesgo que siguió, y las áreas sin cambios. El comentario se actualiza en cada ejecución. El check queda en rojo con algún BLOCKER, si parte del diff quedó sin revisar o si la revisión de un área falló; en ese caso el comentario dice «Revisión incompleta», lista lo que faltó y sus conteos cubren solo lo revisado. Salvo en un PR que cambia el workflow (último punto), un check verde es una revisión completa, sin contar los archivos generados, y sin BLOCKER.
- Para comprobar un «sin hallazgos», el paso `Show what Claude read` de cada área lista lo que Claude leyó, buscó y comentó (rutas, patrones y comandos, sin el contenido) y las llamadas que los permisos le negaron.
- Los archivos generados no entran al diff de ningún revisor ni cuentan como sin revisar, aunque Claude los liste: `**/Migrations/*.Designer.cs`, `**/Migrations/*ModelSnapshot.cs`, `database/production/**/{apply,rollback,verify}.sql`, `client/openapi/openapi.json` y `client/src/api/generated/**`. Salvo `verify.sql` y los scripts de las carpetas anteriores a la más nueva, los cubre `CI`: el job `Backend` corre `dotnet ef migrations has-pending-model-changes` y compara `openapi.json` con el contrato que sirve la API; `Migrations` ejecuta en SQL Server 2022 el script generado otra vez desde las migraciones y compara con él, debajo de su cabecera, el `apply.sql` y el `rollback.sql` de la carpeta más nueva; `Frontend` regenera `client/src/api/generated` y `ClientEnumContractTests` compara sus enums con los del dominio. La migración misma (`Up` y `Down`) y los DTO y controllers detrás del contrato sí se revisan. `verify.sql` son consultas de solo lectura escritas a mano que `CI` no ejecuta, y las carpetas anteriores son la foto del código de su día, que `CI` no compara ([migraciones](database-migrations.md#deuda-actual)): no los cubre ninguna de las dos revisiones automáticas.
- Un área muy grande puede quedar incompleta por tamaño aun sin los generados: lo que no alcanzó a revisar queda en la lista y el check en rojo. Entonces quien hace el merge decide con los demás checks (`CI`, migraciones) y la revisión humana, a partir de lo que el comentario dice que faltó.
- No modifica archivos, no aprueba ni fusiona: la revisión humana y `CI` siguen siendo necesarias.
- Necesita el secreto `CLAUDE_CODE_OAUTH_TOKEN` del repositorio (lo da `claude setup-token`) y la app de GitHub de Claude instalada en el repositorio, con la que publica los comentarios en línea. Son hasta cuatro sesiones de Opus por ejecución (un área sin cambios no llama a Claude): consumen bastante más del límite de uso de la cuenta que una sola sesión con el modelo por omisión.
- Si la llamada a Claude falla (token inválido o vencido, límite de uso, modelo no disponible), la action solo dice que Claude no devolvió `structured_output`; el motivo real queda en el paso `Show why Claude failed` del job del área.
- La action solo corre si el workflow del PR es idéntico al de `master`: en un PR que lo cambia, el comentario dice que Claude no corrió y el check sale en verde sin revisión. El cambio se prueba hasta el siguiente PR después del merge.
- Las áreas están en dos lugares del workflow: la matriz del job `review` y `REVIEW_AREAS` del job `claude-review`, que sirve para notar un área cuyo job no dejó resultado. Si se cambian, cámbialas en los dos: un área que solo está en `REVIEW_AREAS` cuenta como fallida.

## Flujo

```powershell
git status --short --branch
git fetch origin master
git switch master
git pull --ff-only
git switch -c tipo/descripcion-corta
```

No cambies de rama si hay cambios locales que podrían afectarse. Conserva trabajo ajeno y pide dirección. Antes de entregar:

```powershell
git diff --check
git status --short
git diff --stat
git diff
```

## Prohibiciones

- No force push en ramas compartidas.
- No mezclar refactor amplio con cambio funcional.
- No crear ramas permanentes además de `master` ni ramas con su propio workflow de despliegue.
- No incluir secretos, artefactos generados o cambios accidentales.
- No declarar validación que no se ejecutó.
