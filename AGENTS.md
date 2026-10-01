# Instrucciones para agentes

## Producto y fuente de verdad

TienditApp es un SaaS multi-tenant para negocios locales; los proyectos conservan el nombre `FoodParkDigital`. `develop` es la fuente funcional activa y `master` la estable mientras se resuelve su divergencia en una tarea separada. El código y las pruebas de la rama objetivo describen el estado actual; ADRs y esta documentación fijan decisiones/guardrails.

## Mapa

- `src/FoodParkDigital.Domain`: invariantes, agregados, estados y eventos.
- `src/FoodParkDigital.Application`: Commands/Queries, DTOs, validación y puertos.
- `src/FoodParkDigital.Infrastructure`: EF Core, migraciones, Identity e integraciones.
- `src/FoodParkDigital.WebApi`: composition root, HTTP, middleware y SignalR.
- `client`: React/TypeScript/Vite.
- `tests`: xUnit de Domain/Application/Infrastructure.
- `docs`: arquitectura, dominio, desarrollo, IA, roadmap y ADRs.

## Comandos

```powershell
dotnet restore FoodParkDigital.sln
dotnet build FoodParkDigital.sln
dotnet test FoodParkDigital.sln
Set-Location client
npm ci
npm run lint
npm run build
```

`npm ci` no debe cambiar `package-lock.json`. Finaliza con `git diff --check`, `git status` y revisión completa del diff.

## Flujo obligatorio antes de modificar código

Lee el `AGENTS.md` anidado aplicable, ejecuta `git status`, conserva cambios ajenos, inspecciona código/tests/contratos, identifica capas/contextos/tenant/acceso afectados y prepara un plan si no es trivial. No hagas merge, rebase, push, PR, despliegue o acción destructiva sin autorización.

## Guardrails

- Dependencias: `Domain <- Application <- Infrastructure <- WebApi`; WebApi compone. Ningún caso de uso salta Application; controllers delgados.
- Tenant: toda entidad tenant-owned implementa `ITenantScoped`, con `TenantId` obligatorio/inmutable y filtro/pruebas. Tenant viene de identidad/acceso; no uses `IgnoreQueryFilters()` como atajo. Cross-tenant es explícito, autorizado y auditado.
- Acceso: rol, permiso y capability son distintos; backend valida. UI guards no son seguridad y `SUPER_ADMIN` no es bypass implícito.
- Seguridad: no secretos/PII/tokens en código, logs o errores. Valida uploads reales, archivos y SignalR por tenant. CORS con cualquier origen+credenciales es bloqueante preproducción.
- Dominio/finanzas: cambia estados mediante agregados/máquinas; usa `decimal`; backend decide totales/impuestos/saldos/existencias. Correcciones compensan historial, no lo borran.
- Persistencia: configuraciones/migraciones en Infrastructure; no edites migraciones aplicadas ni auto-migres producción. Cambios destructivos requieren compatibilidad, datos y rollback.
- Eventos: entidades no llaman integraciones; handlers idempotentes. Sin Outbox no afirmes atomicidad/entrega durable ni lo implementes incidentalmente.

## Definition of Done

Alcance completo, capas/tenant/acceso evaluados, pruebas relevantes verdes, backend compila, frontend lint/build si aplica, sin secretos/cambios accidentales, contratos/migraciones/docs actualizados y resumen con comandos, omisiones y riesgos.

## Code Review Rules

Bloquea bypass tenant, estado directo, dinero incorrecto, pérdida de historial, secretos, migración insegura, contrato roto o tests debilitados. Exige evidencia ejecutada y revisa el diff completo.

Detalles: [documentación](docs/README.md), [flujo IA](docs/ai/task-workflow.md), [guardrails](docs/ai/guardrails.md), [Definition of Done](docs/development/definition-of-done.md), [seguridad](SECURITY.md).
