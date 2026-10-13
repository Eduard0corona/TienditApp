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
- Un solo comentario del PR resume los hallazgos vigentes con su severidad (BLOCKER, MAJOR, MINOR) y se actualiza en cada ejecución. Con algún BLOCKER el check queda en rojo.
- No modifica archivos, no aprueba ni fusiona: la revisión humana y `CI` siguen siendo necesarias.
- Necesita el secreto `CLAUDE_CODE_OAUTH_TOKEN` del repositorio (lo da `claude setup-token`) y la app de GitHub de Claude instalada en el repositorio, con la que publica los comentarios en línea.

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
