# ADR 0008: Contrato de errores y de la API

- Estado: Aceptada
- Fecha: 2026-10-05 (Fase 4F)

## Contexto

La API respondía errores en cuatro formas: objetos `{ error }` en 147 respuestas de controllers, `ProblemDetails` del manejador de excepciones y del middleware de negocio, `ValidationProblemDetails` del enlace de modelos y cuerpos vacíos (401, 403 y 404 de rutas). El cliente distinguía los casos por el `title` en inglés ("Tenant capability denied", "Concurrency Conflict"), y varios `detail` eran mensajes técnicos en inglés. El contrato OpenAPI se regeneraba a mano, sin verificar en CI, y nada avisaba de cambios incompatibles.

## Decisión

- Todo error es `application/problem+json` con `status`, `title`, `detail`, `instance`, `traceId` y un `code` estable (`ApiErrorCodes`): `validation_failed`, `business_rule`, `domain_rule`, `unauthorized`, `forbidden`, `tenant_required`, `tenant_access_denied`, `tenant_suspended`, `module_disabled`, `not_found`, `conflict`, `concurrency_conflict`, `invalid_state_transition`, `too_many_requests`, `server_error` y `bad_request`. Agregar un código es compatible; renombrarlo o quitarlo rompe a los clientes.
- Los controllers responden con `this.ApiError(status, mensaje[, código])`; el código por omisión sale del estado (400 → `business_rule`). `AddProblemDetails` completa lo que escribe el framework (enlace de modelos, `NotFound()`) y `UseStatusCodePages` da cuerpo a los 401/403/404 vacíos de `/api`. El límite de peticiones responde `too_many_requests`.
- `detail` es un mensaje en español para la persona. Las excepciones técnicas (no encontrado, concurrencia, transición inválida, otro negocio) responden un texto claro y el mensaje técnico queda solo en el log; los nombres de permisos que faltan también van al log.
- Mientras haya lectores anteriores, `error` repite `detail`.
- El cliente decide por `code` (`getApiErrorMessage`, `getApiErrorCode`), nunca por el texto.
- Los enums siguen viajando como números. Pasarlos a texto obliga a cambiar todo el cliente y los mocks de una vez; el defecto que motivó la propuesta (inventario desfasado) lo impiden `ClientEnumContractTests` y los tipos generados. Se revisará si se agrega otro cliente.
- CI verifica que `client/openapi/openapi.json` sea el documento que sirve la API y que `schema.ts` sea el que produce; en cada PR reporta (sin bloquear) los cambios incompatibles contra la rama base con oasdiff, fijado por digest. Una propiedad de petición con `[DefaultValue]` queda opcional en el contrato.

## Consecuencias

Un cliente nuevo (otra app, una integración) puede reaccionar a los errores sin interpretar textos. Los mensajes del enlace de modelos de ASP.NET Core ("The Email field is required.") seguían en inglés; desde la Fase 5 `SpanishModelBinding` los responde en español con `validation_failed` y el cuerpo que no se lee ya no expone el mensaje del lector de JSON. El reporte de oasdiff de esta fase marca como incompatibles propiedades que el contrato marcaba obligatorias por error y que en C# siempre fueron anulables: es una corrección del contrato, no un cambio de comportamiento.
