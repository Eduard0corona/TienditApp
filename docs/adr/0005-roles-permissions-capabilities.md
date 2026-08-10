# ADR 0005: Roles, permisos y capabilities

- Estado: Aceptada
- Fecha: 2026-08-10

## Contexto

El producto necesita clasificación global, autorización granular y módulos habilitados por tenant.

## Decisión

Tratar por separado:

- rol: clasificación general;
- permiso: acción autorizada al usuario dentro del tenant;
- capability: módulo habilitado para el tenant.

Una operación sensible requiere backend. Capability no concede permiso; permiso no habilita módulo. UI guards son UX. `SUPER_ADMIN` no omite aislamiento implícitamente.

## Consecuencias

La matriz de acceso es más explícita y testeable. El mapeo actual por roles/prefijos es transitorio y debe evolucionar a policies/resource authorization sin reescribirlo dentro de tareas no relacionadas.
