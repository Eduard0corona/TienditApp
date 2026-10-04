# Flujo Git

## Estrategia vigente

- `master`: producción. Es la única rama que despliega: cada push publica la API y la SPA (workflow `Deploy WebApp`), y una ejecución manual desde otra rama se omite.
- `develop`: integración activa mientras se resuelve la divergencia; ya no despliega a producción.
- Ramas de trabajo cortas desde la rama objetivo.
- Pull Requests pequeños, con una intención y validación reproducible.
- Preferencia por squash merge para historial funcional claro, si el equipo la aprueba.

Al documentar el repositorio, `origin/master...origin/develop` devolvió `5 27`: master tenía 5 commits exclusivos y develop 27. La `master` local estaba 19 commits detrás de `origin/master`. No resuelvas esa divergencia incidentalmente; requiere inventario, estrategia y revisión separada.

## Flujo

```powershell
git status --short --branch
git fetch origin develop master
git switch develop
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
- No hacer merge/rebase de `master` y `develop` dentro de otra tarea.
- No incluir secretos, artefactos generados o cambios accidentales.
- No declarar validación que no se ejecutó.
