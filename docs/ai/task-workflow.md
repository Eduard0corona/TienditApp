# Flujo obligatorio para agentes de IA

1. Lee `AGENTS.md` raíz y cualquier `AGENTS.md` aplicable al directorio.
2. Ejecuta `git status` y conserva cambios preexistentes.
3. Inspecciona archivos, tests, configuración y contratos relacionados antes de proponer.
4. Identifica capas, contextos, tenant, autorización, capabilities e integraciones afectados.
5. Clasifica la solicitud: bug, feature, refactor, migración o deuda.
6. Prepara un plan para cambios no triviales y confirma límites de alcance.
7. Implementa la solución mínima coherente sin cambios incidentales.
8. Agrega/actualiza pruebas positivas, negativas y de regresión.
9. Ejecuta validaciones proporcionales a backend/frontend/base/seguridad afectados.
10. Revisa el diff completo, incluyendo archivos generados y lockfiles.
11. Actualiza documentación si cambió comportamiento, contrato, operación o arquitectura.
12. Reporta qué cambió, comandos/resultados, qué no se validó y riesgos restantes.

## Fuentes de verdad

Para comportamiento actual, manda el código de la rama objetivo y sus pruebas. Para reglas aprobadas, mandan ADRs/AGENTS/documentación vigente. No uses nombres, comentarios o UI como única evidencia. Si difieren, registra deuda y no inventes reconciliación.

## Cambios no triviales

El plan debe incluir superficie, invariantes, autorización/tenant, estrategia de datos, pruebas y rollback cuando corresponda. Detente si hace falta autoridad para un cambio destructivo, contrato público, secreto, migración riesgosa o ampliación material.
