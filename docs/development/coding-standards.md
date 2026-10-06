# Estándares de código

## Backend

- Nullable e implicit usings están habilitados; evita `null!` fuera de materialización justificada.
- Modela invariantes en constructores/métodos de Domain y usa colecciones de solo lectura.
- Un Command cambia estado; una Query lee y proyecta. Ambos viven en el contexto Application correspondiente.
- FluentValidation valida formato, rangos y presencia de entrada; no sustituye invariantes ni autorización.
- Usa DTOs en límites, mapeados a mano junto a ellos (`XxxMappings.ToDto` para una entidad cargada, `AsDto` como expresión para listas en SQL). Si un DTO tiene ambas formas, una prueba las compara miembro a miembro (`DtoMappingTests`).
- Propaga `CancellationToken` desde controller hasta EF/provider. Toda acción lo recibe y lo pasa a su caso de uso (lo exige `ArchitectureTests`); `RequestCancellation` decide su efecto: una lectura (GET, HEAD) se detiene si el cliente se va y un cambio (POST, PUT, PATCH, DELETE) termina siempre, para no dejar a medias un caso de uso que guarda en varios pasos.
- Usa async para I/O y `AsNoTracking()` en lecturas cuando no se actualizará la entidad.
- Pagina listados con crecimiento posible; filtra antes de materializar. Un listado que se devuelve completo necesita un tope por negocio (`Category.MaxCategoriesPerTenant`, `InventoryLocation.MaxLocationsPerTenant`, `AiAgent.MaxAgentsPerTenant`) o un `Take` con orden explícito, como la cocina.
- Usa `decimal` para dinero y especifica redondeo/precisión.
- Captura concurrencia cuando el recurso pueda modificarse simultáneamente.
- Logs estructurados con identificadores/correlación; nunca secretos o PII innecesaria.

## API

- Rutas versionadas bajo `/api/v1` y controllers delgados.
- `CreatedAtAction` en creaciones con recurso direccionable.
- Errores consistentes con `ProblemDetails`; no mezcles formas `{ error }` sin una estrategia explícita.
- No rompas contratos sin versión/migración y compatibilidad.
- Autenticación, tenant, permission, capability y acceso al objeto se validan en backend.
- Desde la Fase 11 todas las acciones reciben `CancellationToken`. Algunos controllers todavía arman respuestas ad hoc en lugar de `this.ApiError`: son deuda a corregir en cambios futuros focalizados.

## Frontend

- Centraliza HTTP en `client/src/api`; tipos públicos en módulos/types coherentes.
- Componentes pequeños y reutilizables; extrae hooks/sections al crecer.
- TypeScript estricto; no introduzcas `any` sin comentario justificativo.
- React Hook Form en formularios complejos; valida UX sin duplicar reglas autoritativas.
- Conserva loading, empty, error y success; limpia conexiones/efectos.
- Sigue Bootstrap y estilos existentes; no agregues otra librería global sin decisión.

## Comentarios y documentación

Comenta el porqué, no la sintaxis. Actualiza documentación cuando cambie arquitectura, contrato, operación, seguridad o comportamiento. Marca claramente objetivo/propuesta para no fingir implementación.
