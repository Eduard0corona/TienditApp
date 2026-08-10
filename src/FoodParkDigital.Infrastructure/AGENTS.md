# Reglas de Infrastructure

- Implementa interfaces de Application; no expongas providers/EF al Domain ni pongas casos de uso en servicios técnicos.
- `ApplicationDbContext`, configuraciones y migraciones permanecen aquí.
- Toda entidad `ITenantScoped` debe recibir filtro global; valida también escrituras y relaciones del mismo tenant.
- Revisa cualquier `IgnoreQueryFilters()` como operación privilegiada y limita explícitamente por tenant/usuario.
- No edites migraciones aplicadas ni el snapshot manualmente. Genera migración descriptiva y script/revisión para cada esquema.
- Integraciones, archivos y handlers susceptibles a retry son idempotentes, observables y tenant-isolated; no registres secretos/PII.
- Azure Blob/local deben usar rutas normalizadas y namespaced por tenant; validar descarga y borrado.
- Sin Outbox, describe eventos externos como post-commit best-effort; no ocultes fallos ni afirmes atomicidad.
- Prueba con SQL Server cuando el comportamiento dependa de rowversion, índices, filtros o transacciones.
