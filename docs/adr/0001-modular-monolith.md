# ADR 0001: Monolito modular

- Estado: Aceptada
- Fecha: 2026-08-10

## Contexto

TienditApp cubre varios contextos que comparten identidad, tenant y transacciones. El repositorio despliega una WebApi con SPA y una base SQL Server.

## Decisión

Mantener un monolito modular. Separar contextos por namespaces/carpetas, casos de uso y agregados sin crear servicios desplegables por defecto.

Solo evaluar extracción si existe evidencia de escalamiento independiente, aislamiento operativo, ownership separado o ciclos de despliegue distintos.

## Consecuencias

Se conserva simplicidad de despliegue y transacciones locales, a cambio de disciplina fuerte de módulos y riesgo de acoplamiento en DB compartida. Microservicios, mensajería distribuida y bases por servicio no son el objetivo actual.
