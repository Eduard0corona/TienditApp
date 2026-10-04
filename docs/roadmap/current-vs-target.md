# Estado actual vs arquitectura objetivo

| Área | Estado actual comprobado | Objetivo aprobado | Estado |
|---|---|---|---|
| Despliegue | Monolito modular API+SPA, DB compartida | Mantener; evaluar servicios solo por escalamiento/aislamiento/ownership/cadencia | Decisión |
| Capas | Cuatro proyectos Clean Architecture; algunas fugas puntuales | Dependencias estrictas y casos de uso siempre en Application | En progreso |
| Tenant | Filtros globales para `ITenantScoped`, middleware por rutas, interceptor que rechaza `TenantId` ajeno o modificado, hubs con membresía | Cobertura total, tenant inmutable, políticas explícitas y suite cross-tenant | Brecha |
| Autorización | Roles + permisos/capabilities en middleware y Application parcial | Policies/resource authorization consistentes en backend | Brecha |
| Eventos | Outbox durable con worker, reintentos y dead-letter para auditoría y avisos (Fase 2); SignalR best-effort | Outbox durable + consumidores idempotentes antes de procesos críticos | En progreso |
| Fiscal | IVA por negocio y por producto, desglose persistido por línea, claves SAT y vista previa CFDI 4.0 (Fases 1 y 3); sin timbrado | Tax Calculation Engine México, desglose persistido, preparación CFDI | En progreso |
| Ajustes financieros | Pagos/caja sin ledger de ajustes | Créditos/débitos trazables, CxC, saldo a favor, Wallet y conciliación | Objetivo no implementado |
| Inventario | Stock almacenado + movimientos; idempotencia parcial; tokens de concurrencia en stock, costo y recepción | Concurrencia robusta, transferencias atómicas y reconciliación | Brecha |
| API | `/api/v1`, DTOs/MediatR, errores mixtos | `ProblemDetails` uniforme, tokens propagados, paginación completa | Brecha |
| Frontend | Lazy routes, API central, guards, SignalR | Componentes más pequeños y pruebas automatizadas | Brecha |
| Seguridad | CORS allowlist, secretos fuera del repo (pendiente rotar), uploads validados por firma; JWT/refresh/2FA/rate limit parciales | Uploads con descarga autorizada, secretos rotados y gestionados, tokens revocables, pruebas security | Bloqueante preproducción |
| Pruebas | Domain/Application/WebApi xUnit en CI | Pirámide con SQL Server, API, tenant y frontend | Brecha |
| Operación | Auto-migrate solo en Development con servidor local; CI y deploy con gates de build/test/lint/migraciones/audit | Migraciones productivas controladas y observabilidad | En progreso |

No hay aprobación para implementar estos objetivos dentro de tareas ajenas. Cada uno requiere issue/ADR/alcance y validación propios.
