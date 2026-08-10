# Guardrails para agentes de IA

Un agente no debe:

- inventar requisitos, módulos, endpoints, comandos o madurez;
- presentar una propuesta/objetivo como implementado;
- cambiar arquitectura silenciosamente o proponer microservicios por defecto;
- eliminar código/datos, desactivar tests o debilitar assertions para obtener verde;
- desactivar autorización, capabilities, filtros tenant, validación, CORS seguro o auditoría;
- confiar en tenant, estado, importe o existencia enviados por frontend;
- introducir secretos, PII o datos financieros sensibles;
- hacer cambios destructivos, merge, rebase, push, PR o despliegue sin autorización;
- editar migraciones aplicadas ni borrar bases como actualización;
- usar `IgnoreQueryFilters()` como atajo;
- modificar archivos no relacionados o sobrescribir cambios preexistentes;
- declarar éxito sin ejecutar y reportar validaciones;
- implementar roadmap/arquitectura objetivo dentro de una tarea documental.

## Reglas críticas

- Domain no depende de capas externas; Application coordina todos los casos de uso.
- Toda entidad tenant-owned implementa `ITenantScoped`, con tenant inmutable y filtro/pruebas.
- `SUPER_ADMIN` no es bypass implícito de tenant.
- Roles, permisos y capabilities se validan por separado en backend.
- `decimal` y reglas explícitas para dinero; no hay cálculo fiscal autoritativo en UI.
- Estados pasan por agregados/máquinas; correcciones de inventario/finanzas compensan historial.
- Side effects externos viven en handlers/Infrastructure y requieren idempotencia.
- Sin Outbox no se afirma entrega transaccional/durable.

Ante ambigüedad, toma la suposición de menor alcance que preserve seguridad/datos, documéntala y solicita decisión si cambia comportamiento o contrato.
