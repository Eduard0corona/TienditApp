# Flujo Git

## Estrategia vigente

- `master`: la única rama permanente y la de producción. Cada push publica la API y la SPA (workflow `Deploy WebApp`, ver [despliegue](deploy-azure.md)) y espera a que la app responda con esa versión; una ejecución manual desde otra rama se omite.
- Ramas de trabajo cortas desde `master`, que vuelven a ella por Pull Request.
- Pull Requests pequeños, con una intención y validación reproducible. Antes de fusionar uno con migraciones, aplica su `apply.sql` en producción (ver [migraciones](database-migrations.md)).
- Preferencia por squash merge para historial funcional claro, si el equipo la aprueba.

`develop` se retiró el 4 de octubre de 2026: todo su contenido ya estaba en `master`. Mientras existió, también desplegaba a producción con su propio workflow, así que una rama vieja con un workflow de despliegue es un riesgo: bórrala en lugar de dejarla.

## Revisión automática con Claude

Cada Pull Request hacia `master` pasa, además de `CI`, por el workflow `Claude Code Review` (`.github/workflows/claude-code-review.yml`) al abrirse, al recibir commits, al reabrirse y al salir de borrador. Los PR hacia otra rama, en borrador, desde un fork o de Dependabot no se revisan.

- Claude lee el diff contra el [`AGENTS.md`](../../AGENTS.md) de la raíz, los `AGENTS.md` de cada capa que toca, los [guardrails](../ai/guardrails.md) y el [checklist](../ai/review-checklist.md), deja un comentario en la línea de cada hallazgo nuevo y no repite los que ya están en el PR.
- Revisa solo y en orden, sin subagentes (`--disallowedTools "Agent,Task"`): en el PR #97 lanzó subagentes en segundo plano y entregó el resumen antes de que terminara alguno.
- Si el diff es demasiado grande para revisarlo entero, avanza por capas: Domain, Application, Infrastructure (primero `Persistence/Migrations` y `database/production`), WebApi, `tests` y al final `client` y lo demás.
- Un solo comentario del PR dice qué se revisó, resume los hallazgos vigentes con su severidad (BLOCKER, MAJOR, MINOR) y se actualiza en cada ejecución. El check queda en rojo con algún BLOCKER o si parte del diff quedó sin revisar; en ese caso el comentario dice «Revisión incompleta», lista lo que faltó y sus conteos cubren solo lo revisado. Salvo en un PR que cambia el workflow (último punto), un check verde es una revisión completa sin BLOCKER.
- No modifica archivos, no aprueba ni fusiona: la revisión humana y `CI` siguen siendo necesarias.
- Necesita el secreto `CLAUDE_CODE_OAUTH_TOKEN` del repositorio (lo da `claude setup-token`) y la app de GitHub de Claude instalada en el repositorio, con la que publica los comentarios en línea.
- Si la llamada a Claude falla (token inválido o vencido, límite de uso, modelo no disponible), la action solo dice que Claude no devolvió `structured_output`; el motivo real queda en el paso `Show why Claude failed`.
- La action solo corre si el workflow del PR es idéntico al de `master`: un PR que lo cambia sale en verde sin revisión, y el cambio se prueba hasta el siguiente PR después del merge.

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
