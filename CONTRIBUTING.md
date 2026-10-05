# Contribuir a TienditApp

## Antes de cambiar

1. Lee el `AGENTS.md` raíz y el anidado que aplique.
2. Parte de `master`, la única rama permanente; cada merge a ella despliega a producción.
3. Conserva cambios locales ajenos y revisa `git status` antes de editar.
4. Clasifica la tarea como bug, feature, refactor, migración o deuda.
5. Identifica capas, contexto, contratos, tenant, autorización y capabilities afectados.
6. Para cambios no triviales, prepara un plan y mantén el alcance mínimo.

## Reglas de implementación

- Respeta `Domain <- Application <- Infrastructure <- WebApi`; WebApi es el composition root.
- Implementa casos de uso en Application con Commands/Queries y no desde controllers.
- No confíes en `TenantId` enviado por el cliente. Toda entidad tenant-owned nueva implementa `ITenantScoped`, recibe un `TenantId` obligatorio que no cambia y debe quedar bajo filtro global.
- No uses `IgnoreQueryFilters()` para eludir un problema. Una operación cross-tenant requiere propósito administrativo, autorización explícita, alcance por tenant y auditoría.
- Roles, permisos y capabilities son controles distintos; el backend debe validar los tres cuando correspondan.
- Usa `decimal` para dinero y cálculos autoritativos en backend.
- Cambia estados mediante métodos del agregado y sus máquinas de estado.
- Cambia existencias mediante movimientos trazables e idempotentes; no borres historial para compensar.
- Propaga `CancellationToken`, usa DTOs en límites HTTP y FluentValidation para entradas.
- No edites migraciones aplicadas. Cada cambio de esquema lleva una migración descriptiva en Infrastructure.
- No agregues secretos, tokens, PII ni datos financieros sensibles a código, logs o fixtures.

## Pruebas y revisión

Cada bug requiere regresión y cada regla de dominio pruebas positivas y negativas. Ejecuta lo relevante:

```powershell
dotnet restore FoodParkDigital.sln
dotnet build FoodParkDigital.sln
dotnet test FoodParkDigital.sln
Set-Location client
npm ci
npm run lint
npm test
npm run build
```

`npm ci` no debe modificar `client/package-lock.json`. Antes de entregar ejecuta `git diff --check`, revisa el diff completo y confirma que no hay cambios accidentales. No debilites assertions ni elimines datos/código para hacer pasar pruebas.

## Pull Requests

Usa ramas cortas desde la rama objetivo y PRs pequeños. No mezcles refactors extensos con comportamiento funcional. Squash merge es la preferencia propuesta si el equipo la aprueba. Nunca hagas force push sobre ramas compartidas. Documenta validaciones no ejecutadas y riesgos restantes.

Consulta [Definition of Done](docs/development/definition-of-done.md), [coding standards](docs/development/coding-standards.md) y [git workflow](docs/development/git-workflow.md).
