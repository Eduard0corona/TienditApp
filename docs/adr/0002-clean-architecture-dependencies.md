# ADR 0002: Dependencias de Clean Architecture

- Estado: Aceptada
- Fecha: 2026-08-10

## Contexto

La solución ya separa Domain, Application, Infrastructure y WebApi, pero necesita reglas persistentes para evitar saltos de capa.

## Decisión

`Domain` no depende de capas externas; `Application` depende de Domain; `Infrastructure` implementa puertos de Application; `WebApi` es composition root y depende de Application/Infrastructure. El cliente solo consume contratos.

Casos de uso pasan por Application, controllers permanecen delgados, invariantes viven en Domain, validación de entrada en Application y detalles de persistencia/integración en Infrastructure.

## Consecuencias

Mejora testabilidad y coherencia, con costo de DTOs/puertos y orquestación explícita. Las fugas existentes se registran como deuda y no legitiman nuevas dependencias.
