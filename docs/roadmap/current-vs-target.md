# Estado actual vs arquitectura objetivo

| Área | Estado actual comprobado | Objetivo aprobado | Estado |
|---|---|---|---|
| Despliegue | Monolito modular API+SPA, DB compartida | Mantener; evaluar servicios solo por escalamiento/aislamiento/ownership/cadencia | Decisión |
| Capas | Cuatro proyectos Clean Architecture con pruebas de dependencias en CI (Fase 4F); quedan reglas de negocio en Infrastructure (respuestas automáticas, procesador del webhook) | Dependencias estrictas y casos de uso siempre en Application | En progreso |
| Tenant | Filtros globales para `ITenantScoped`, middleware por membresía, interceptor que rechaza `TenantId` ajeno o modificado, hubs con membresía, workers y webhook en el alcance de su negocio (Fase 6) y cada `IgnoreQueryFilters()` revisado en una lista (Fase 4) | Cobertura total, tenant inmutable, políticas explícitas y suite cross-tenant | Brecha |
| Autorización | Cada caso de uso declara su regla (permiso, membresía, módulo) y Application la evalúa; controllers sin roles y hubs por membresía (Fase 5B) | Policies/resource authorization consistentes en backend | En progreso |
| Eventos | Outbox durable con worker, reintentos y dead-letter para auditoría y avisos de pedidos (Fase 2); recordatorios, campañas y avisos de citas por la cola de mensajería (Fases 2 y 6); SignalR tras el commit (Fase 4G) con Azure SignalR opcional (Fase 5E) | Outbox durable + consumidores idempotentes antes de procesos críticos | En progreso |
| Fiscal | IVA por negocio y por producto, desglose persistido por línea, claves SAT y vista previa CFDI 4.0 (Fases 1 y 3); sin timbrado | Tax Calculation Engine México, desglose persistido, preparación CFDI | En progreso |
| Ajustes financieros | Pagos/caja sin ledger de ajustes | Créditos/débitos trazables, CxC, saldo a favor, Wallet y conciliación | Objetivo no implementado |
| Inventario | Libro mayor único para insumos y productos (Fase 5A), folios por contador (Fase 4F), tokens de concurrencia en existencias, costo y recepción; la recepción de compra aún sin llave de idempotencia | Concurrencia robusta, transferencias atómicas y reconciliación | En progreso |
| API | `/api/v1`, `ProblemDetails` uniforme con código (Fase 4F), OpenAPI versionado y cliente generado; algunos listados pequeños sin paginar | `ProblemDetails` uniforme, tokens propagados, paginación completa | En progreso |
| Frontend | Lazy routes, TanStack Query sobre el cliente generado, pruebas con Vitest (Fase 5C) y lint de accesibilidad (Fase 6F); páginas de hasta 2,500 líneas | Componentes más pequeños y pruebas automatizadas | En progreso |
| Seguridad | CORS allowlist, secretos fuera del repo (pendiente rotar), uploads validados por firma; JWT/refresh/2FA/rate limit parciales | Uploads con descarga autorizada, secretos rotados y gestionados, tokens revocables, pruebas security | Bloqueante preproducción |
| Pruebas | xUnit de Domain, Application, Infrastructure y WebApi; pruebas contra SQL Server 2022 en CI (Fase 4F) y del cliente (Fase 5C) | Pirámide con SQL Server, API, tenant y frontend | En progreso |
| Operación | Auto-migrate solo en Development con servidor local; CI y deploy con gates de build/test/lint/migraciones/audit | Migraciones productivas controladas y observabilidad | En progreso |

No hay aprobación para implementar estos objetivos dentro de tareas ajenas. Cada uno requiere issue/ADR/alcance y validación propios.
