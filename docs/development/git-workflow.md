# Flujo Git

## Estrategia vigente

- `master`: la única rama permanente y la de producción. Cada push publica la API y la SPA (workflow `Deploy WebApp`, ver [despliegue](deploy-azure.md)) y espera a que la app responda con esa versión; una ejecución manual desde otra rama se omite.
- Ramas de trabajo cortas desde `master`, que vuelven a ella por Pull Request.
- Pull Requests pequeños, con una intención y validación reproducible. Antes de fusionar uno con migraciones, aplica su `apply.sql` en producción (ver [migraciones](database-migrations.md)).
- Preferencia por squash merge para historial funcional claro, si el equipo la aprueba.

`develop` se retiró el 4 de octubre de 2026: todo su contenido ya estaba en `master`. Mientras existió, también desplegaba a producción con su propio workflow, así que una rama vieja con un workflow de despliegue es un riesgo: bórrala en lugar de dejarla.

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
