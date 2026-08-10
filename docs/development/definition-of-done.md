# Definition of Done

Una tarea está terminada solo cuando:

- el alcance solicitado está implementado y no incluye cambios ajenos;
- se respetaron capas, agregados y límites de contexto;
- se comprobó aislamiento multi-tenant para recursos afectados;
- se evaluaron autenticación, autorización, permisos y capabilities;
- estados, dinero e inventario siguen siendo autoritativos en backend;
- pruebas relevantes positivas, negativas y de regresión pasan;
- backend compila;
- frontend compila y pasa lint cuando fue afectado;
- no hay secretos, PII o datos financieros sensibles nuevos;
- contratos y migraciones se versionaron/documentaron cuando aplica;
- documentación afectada está actualizada;
- `git diff --check`, status y diff final no muestran cambios accidentales;
- el resumen final incluye archivos, comandos, resultados, validaciones omitidas y riesgos.

Para una tarea solo documental, “implementado” significa que todos los archivos/enlaces/rutas son correctos, el contenido distingue actual/decisión/objetivo/deuda/propuesta, las validaciones no modifican locks y el diff solo contiene documentación/instrucciones además de cambios preexistentes identificados.

## Reglas de code review

Bloquea si hay bypass tenant/autorización, dinero con tipos inadecuados, estado directo, pérdida de historial, migración destructiva sin plan, side effect desde Domain, secretos, contrato roto sin versión o tests debilitados. Solicita evidencia ejecutada, no solo afirmaciones.
