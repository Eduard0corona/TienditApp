# ADR 0004: CQRS con MediatR

- Estado: Aceptada
- Fecha: 2026-08-10

## Contexto

Application ya organiza comportamiento por Commands, Queries, handlers, validators y behaviors.

## Decisión

Usar Commands para cambios y Queries para lecturas, enviados con MediatR. Aplicar FluentValidation y behaviors transversales. Controllers traducen HTTP y delegan; DTOs cruzan límites.

CQRS aquí no significa bases separadas, event sourcing ni consistencia eventual automática.

## Consecuencias

Cada caso de uso tiene una entrada y responsabilidad claras. Se acepta cierta cantidad de clases/DTOs. Evita handlers gigantes, commits intermedios ocultos y acceso al DbContext desde WebApi.
