# Checklist de revisión para agentes

## Alcance y evidencia

- [ ] Leí los `AGENTS.md` aplicables y el código relacionado.
- [ ] Preservé cambios locales ajenos.
- [ ] Distinguí estado actual, decisión, objetivo, deuda y propuesta.
- [ ] No añadí archivos/cambios fuera de alcance.

## Arquitectura y dominio

- [ ] Dependencias respetan Domain/Application/Infrastructure/WebApi.
- [ ] Controllers delegan a Application y contratos usan DTOs.
- [ ] Invariantes y transiciones están en Domain.
- [ ] Colecciones de agregados son de solo lectura.
- [ ] Side effects externos no salen de entidades.

## Tenant, acceso y seguridad

- [ ] Entidades nuevas tenant-owned implementan `ITenantScoped` y filtro global.
- [ ] Tenant efectivo viene de identidad validada.
- [ ] Probé aislamiento con dos tenants y acceso por ID/archivo/hub cuando aplica.
- [ ] Evalué rol, permiso, capability y acceso al objeto.
- [ ] No añadí `IgnoreQueryFilters()` sin justificación administrativa/pre-tenant y tests.
- [ ] No hay secretos/PII en diff, logs o errores.

## Datos, dinero e inventario

- [ ] Dinero usa `decimal` y redondeo explícito.
- [ ] Backend conserva autoridad sobre totales, saldos y existencias.
- [ ] Correcciones son movimientos/ajustes trazables, no borrado.
- [ ] Reintentos y concurrencia están cubiertos.
- [ ] Migración es descriptiva, compatible y reversible/roll-forward.

## Calidad y entrega

- [ ] Añadí regresión y casos positivo/negativo.
- [ ] Propagué `CancellationToken` en I/O.
- [ ] Ejecuté build/tests/lint/build frontend relevantes.
- [ ] `npm ci` no cambió `package-lock.json`.
- [ ] Ejecuté `git diff --check` y revisé el diff completo.
- [ ] Reporté comandos, resultados, omisiones y riesgos.
