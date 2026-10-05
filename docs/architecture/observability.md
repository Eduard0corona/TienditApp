# Observabilidad

## Estado actual

### Health checks

| Endpoint | Qué revisa | Uso |
| --- | --- | --- |
| `GET /health/live` | Nada: responde 200 mientras el proceso corre. | Reinicio del proceso. |
| `GET /health/ready` | Conexión a la base (`database`) y colas en segundo plano (`background-queues`). | Health check de App Service, balanceador y alertas. |
| `GET /health/schema` | Que la base tenga todas las migraciones de esta versión (`database-schema`). | Prueba de humo del despliegue (Fase 7). |

- Los tres son anónimos y solo devuelven estados (`{"status":"Healthy","checks":{...}}`), sin mensajes de error ni datos.
- `database-schema` queda **Unhealthy** (503) cuando falta en la base una migración de esta versión: producción no migra al arrancar y alguien publicó antes de aplicar su `apply.sql`. El log nombra la migración que falta. No forma parte de `/health/ready` a propósito: un esquema atrasado hace fallar el despliegue (o detiene el swap del slot), no saca de rotación a todas las instancias.
- `background-queues` queda **Degraded** (sigue respondiendo 200) cuando el outbox o la bandeja del webhook apartaron trabajo en las últimas 24 horas, o cuando hay trabajo pendiente con más de 15 minutos de retraso (workers detenidos o atorados).
- Sin base de datos, `/health/ready` responde 503.
- En App Service: Configuración → Health check → ruta `/health/ready`.

### Telemetría (OpenTelemetry)

Se exporta solo si hay destino configurado:

- `APPLICATIONINSIGHTS_CONNECTION_STRING`: distro de Azure Monitor (trazas y métricas de ASP.NET Core, HttpClient y SQL Client, más el meter `TienditApp`).
- `OTEL_EXPORTER_OTLP_ENDPOINT` (y las variables estándar `OTEL_*`): exportador OTLP hacia cualquier colector, con las mismas fuentes y métricas de runtime.

Sin ninguno de los dos no se registra OpenTelemetry. Los logs salen por Serilog a la consola (log stream de App Service) y, con destino, Serilog también se los entrega al proveedor de logs de OpenTelemetry (`writeToProviders`), así llegan junto con trazas y métricas (Fase 4). Los proveedores de logging predeterminados se quitan para no duplicar la consola.

### Etiqueta por negocio

`TenantResolutionMiddleware` agrega `tenant.id` (GUID interno, no es dato personal):

- a la traza de la petición (`Activity`);
- a la métrica integrada `http.server.request.duration` (`IHttpMetricsTagsFeature`), junto con ruta y código de estado.

`RequestTelemetryEnrichmentMiddleware` (después de resolver el negocio) etiqueta además la traza con `tienditapp.tenant_id` y `enduser.id` y agrega `TenantId` y `UserId` (solo identificadores, nunca nombre, correo ni teléfono) a cada log escrito durante la petición.

### Métricas propias (meter `TienditApp`)

| Métrica | Etiquetas | Significado |
| --- | --- | --- |
| `tienditapp.outbox.messages` | `outcome` (`processed`, `failed`, `dead_lettered`), `event.type`, `tenant.id` | Efectos durables de eventos. |
| `tienditapp.inbox.deliveries` | `outcome`, `source` | Entregas del webhook. |
| `tienditapp.messages.sent` | `channel`, `outcome` (`sent`, `transient_failure`, `rejected`), `tenant.id` | Mensajes entregados al proveedor. |

## Alertas sugeridas por negocio

Consultas de ejemplo para Application Insights (Log Analytics). Ajusta umbrales y ventanas a tu volumen.

Errores 5xx por negocio (más de 5 en 10 minutos):

```kusto
requests
| where timestamp > ago(10m) and toint(resultCode) >= 500
| extend tenant = tostring(customDimensions["tenant.id"])
| summarize errors = count() by tenant
| where errors > 5
```

Mensajes de WhatsApp fallidos por negocio:

```kusto
customMetrics
| where timestamp > ago(30m) and name == "tienditapp.messages.sent"
| extend outcome = tostring(customDimensions["outcome"]), tenant = tostring(customDimensions["tenant.id"])
| where outcome != "sent"
| summarize failures = sum(valueSum) by tenant, outcome
```

Trabajo apartado en el outbox (cualquier ocurrencia):

```kusto
customMetrics
| where timestamp > ago(15m) and name == "tienditapp.outbox.messages"
| where tostring(customDimensions["outcome"]) == "dead_lettered"
| summarize total = sum(valueSum) by tenant = tostring(customDimensions["tenant.id"]), event = tostring(customDimensions["event.type"])
```

Disponibilidad: una prueba de disponibilidad (o el health check de App Service) sobre `/health/ready`.

## Deuda

- No hay paneles ni reglas de alerta versionados; las consultas de arriba son el punto de partida.
- Los logs de los workers (outbox, mensajería, reportes) no llevan todavía el negocio como propiedad; las peticiones HTTP sí.
- Los mensajes apartados (dead-lettered) se revisan con SQL sobre `OutboxMessages` e `InboxMessages`; no hay pantalla para reintentarlos.
